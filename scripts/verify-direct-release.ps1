param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath,
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,
    [Parameter(Mandatory = $true)]
    [string]$ManualPath,
    [Parameter(Mandatory = $true)]
    [string]$ChecksumPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows-sdk-tools.ps1')
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$certificateFile = (Resolve-Path -LiteralPath $CertificatePath).Path
$manual = (Resolve-Path -LiteralPath $ManualPath).Path
$checksum = (Resolve-Path -LiteralPath $ChecksumPath).Path
$archiveDirectory = Split-Path -Parent $archive
$temporaryRoot = Join-Path $archiveDirectory ('.keisai-direct-verify-' + [guid]::NewGuid().ToString('N'))
$signTool = Get-LatestWindowsSdkTool -Name 'signtool.exe'

try {
    Expand-Archive -LiteralPath $archive -DestinationPath $temporaryRoot
    $manualFileName = -join @(
        [char]0x7F6B, [char]0x5F69, '_',
        [char]0x64CD, [char]0x4F5C, [char]0x8AAC, [char]0x660E, [char]0x66F8,
        '.pdf'
    )
    foreach ($requiredFile in @(
        'Keisai.exe',
        'Keisai.dll',
        'YTEC-Sticky-Note.exe',
        'YTEC-Sticky-Note.dll',
        'README.txt',
        'LICENSE.txt',
        'NOTICE.txt',
        'THIRD_PARTY_NOTICES.txt',
        'PRIVACY.txt',
        'CHANGELOG.txt',
        'startup-runtime-manifest.json',
        'startup-runtime-manifest.p7s',
        $manualFileName
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $temporaryRoot $requiredFile) -PathType Leaf)) {
            throw "The self-signed ZIP is missing a required file: $requiredFile"
        }
    }
    if (Test-Path -LiteralPath (Join-Path $temporaryRoot 'data')) {
        throw 'The self-signed ZIP must not contain a user data directory.'
    }
    if (Get-ChildItem -LiteralPath $temporaryRoot -Recurse -File | Where-Object Extension -in @('.pfx', '.p12', '.key')) {
        throw 'The self-signed ZIP contains a private-key file format.'
    }
    foreach ($licenseEvidence in @(
        'third-party-licenses\component-inventory.json',
        'third-party-licenses\dotnet-10.0.10\LICENSE.txt',
        'third-party-licenses\dotnet-10.0.10\ThirdPartyNotices.txt'
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $temporaryRoot $licenseEvidence) -PathType Leaf)) {
            throw "The self-signed ZIP is missing third-party license evidence: $licenseEvidence"
        }
    }

    $publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificateFile)
    if ($publicCertificate.HasPrivateKey) {
        throw 'The public CER contains a private key.'
    }
    $expectedThumbprint = $publicCertificate.Thumbprint.ToUpperInvariant()

    $manifestPath = Join-Path $temporaryRoot 'startup-runtime-manifest.json'
    $manifestSignaturePath = Join-Path $temporaryRoot 'startup-runtime-manifest.p7s'
    $manifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
    $manifestSignature = [System.IO.File]::ReadAllBytes($manifestSignaturePath)
    $manifestCms = [System.Security.Cryptography.Pkcs.SignedCms]::new(
        [System.Security.Cryptography.Pkcs.ContentInfo]::new($manifestBytes),
        $true
    )
    $manifestCms.Decode($manifestSignature)
    $manifestCms.CheckSignature($true)
    if ($manifestCms.SignerInfos.Count -ne 1 -or $null -eq $manifestCms.SignerInfos[0].Certificate) {
        throw 'The startup runtime manifest must have exactly one embedded signer certificate.'
    }
    $manifestSigner = $manifestCms.SignerInfos[0].Certificate
    if ([Convert]::ToBase64String($manifestSigner.RawData) -ne [Convert]::ToBase64String($publicCertificate.RawData)) {
        throw 'The startup runtime manifest signer does not match the published code-signing certificate.'
    }

    $manifest = [System.Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
    if ($manifest.Format -ne 1 -or $manifest.Product -ne 'Keisai' -or [string]::IsNullOrWhiteSpace($manifest.Version)) {
        throw 'The startup runtime manifest identity is invalid.'
    }
    $manifestNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in @($manifest.Files)) {
        if ([string]::IsNullOrWhiteSpace($entry.Name) -or
            $entry.Name -ne [System.IO.Path]::GetFileName($entry.Name) -or
            -not $manifestNames.Add([string]$entry.Name) -or
            [string]$entry.Sha256 -notmatch '^[0-9a-fA-F]{64}$') {
            throw 'The startup runtime manifest contains an invalid or duplicate entry.'
        }
        $target = Join-Path $temporaryRoot ([string]$entry.Name)
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
            throw "The startup runtime manifest file is missing: $($entry.Name)"
        }
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash.ToLowerInvariant()
        if ($actualHash -ne ([string]$entry.Sha256).ToLowerInvariant()) {
            throw "The startup runtime manifest hash does not match: $($entry.Name)"
        }
    }
    $actualRuntimeNames = @(Get-ChildItem -LiteralPath $temporaryRoot -File | Where-Object {
        $_.Extension -in @('.exe', '.dll', '.json', '.dat') -and
        $_.Name -ne 'startup-runtime-manifest.json'
    } | ForEach-Object Name)
    if ($actualRuntimeNames.Count -ne $manifestNames.Count -or
        @($actualRuntimeNames | Where-Object { -not $manifestNames.Contains($_) }).Count -ne 0) {
        throw 'The startup runtime manifest does not describe the exact runtime file set.'
    }

    foreach ($relativePath in @(
        'Keisai.exe',
        'Keisai.dll',
        'YTEC-Sticky-Note.exe',
        'YTEC-Sticky-Note.dll'
    )) {
        $targetPath = Join-Path $temporaryRoot $relativePath
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            $previousErrorActionPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                $verificationOutput = (& $signTool verify /pa /all /v $targetPath 2>&1 | Out-String)
                $verificationExitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }
            if ($verificationOutput -notmatch '0x000000E1' -or $attempt -eq 5) {
                break
            }
            Write-Warning "Security scanning temporarily blocked signature verification; retrying $relativePath ($attempt/5)."
            Start-Sleep -Seconds 2
        }
        $isExpectedUntrustedRoot =
            $verificationExitCode -eq 1 -and
            $verificationOutput -match 'terminated in a root' -and
            $verificationOutput -match 'certificate which is not trusted by the trust provider' -and
            $verificationOutput -match 'Number of errors:\s+1'
        if (($verificationExitCode -ne 0 -and -not $isExpectedUntrustedRoot) -or
            $verificationOutput -notmatch [regex]::Escape($expectedThumbprint) -or
            $verificationOutput -notmatch 'Hash of file \(sha256\):\s+[0-9A-F]{64}' -or
            $verificationOutput -notmatch 'The signature is timestamped:') {
            throw "Signature or timestamp verification failed: $relativePath`n$verificationOutput"
        }
    }

    if (Test-Path -LiteralPath (Join-Path $temporaryRoot 'YTEC-Sticky-Note-Startup.exe')) {
        throw 'The self-signed ZIP must not contain the retired startup helper.'
    }

    $expectedFiles = @($archive, $manual, $certificateFile)
    $checksumLines = Get-Content -LiteralPath $checksum
    foreach ($file in $expectedFiles) {
        $name = Split-Path -Leaf $file
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
        if ($checksumLines -notcontains "$actualHash  $name") {
            throw "SHA256SUMS.txt does not match the file: $name"
        }
    }

    Write-Output "Verified self-signed direct release: $archive"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $archivePrefix = [System.IO.Path]::GetFullPath($archiveDirectory).TrimEnd('\') + '\'
        $resolvedTemporary = [System.IO.Path]::GetFullPath($temporaryRoot)
        if (-not $resolvedTemporary.StartsWith($archivePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a verification directory outside the archive directory: $resolvedTemporary"
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
