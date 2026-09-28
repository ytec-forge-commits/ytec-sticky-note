param(
    [string]$CertificateSubject = 'CN=Y-TEC',
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows-sdk-tools.ps1')

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'src\YtecStickyNote\YtecStickyNote.csproj'
[xml]$projectDefinition = Get-Content -LiteralPath $projectFile -Raw -Encoding UTF8
$appVersion = [string]$projectDefinition.Project.PropertyGroup.Version
$artifactRoot = Join-Path $projectRoot 'artifacts'
$unsignedArchive = Join-Path $artifactRoot "Keisai-$appVersion-win-x64.zip"
$signedArchive = Join-Path $artifactRoot "Keisai-$appVersion-win-x64-self-signed.zip"
$certificatePath = Join-Path $artifactRoot 'Keisai-Y-TEC-Code-Signing.cer'
$manualFileName = -join @(
    [char]0x7F6B, [char]0x5F69, '_',
    [char]0x64CD, [char]0x4F5C, [char]0x8AAC, [char]0x660E, [char]0x66F8,
    '.pdf'
)
$manualSource = Join-Path $projectRoot (Join-Path 'output\pdf' $manualFileName)
$manualPath = Join-Path $artifactRoot 'Keisai-Manual-ja.pdf'
$checksumPath = Join-Path $artifactRoot 'SHA256SUMS.txt'
$stagingPath = Join-Path $artifactRoot ('.self-signed-staging-' + [Guid]::NewGuid().ToString('N'))
$candidateRoot = Join-Path $artifactRoot ('.self-signed-candidate-' + [Guid]::NewGuid().ToString('N'))
$candidateArchive = Join-Path $candidateRoot (Split-Path -Leaf $signedArchive)
$candidateCertificate = Join-Path $candidateRoot (Split-Path -Leaf $certificatePath)
$candidateManual = Join-Path $candidateRoot (Split-Path -Leaf $manualPath)
$candidateChecksum = Join-Path $candidateRoot (Split-Path -Leaf $checksumPath)
$signTool = Get-LatestWindowsSdkTool -Name 'signtool.exe'
$releaseSucceeded = $false
$publishingStarted = $false
$signedRelativePaths = @(
    'Keisai.exe',
    'Keisai.dll',
    'YTEC-Sticky-Note.exe',
    'YTEC-Sticky-Note.dll'
)

function Get-RuntimeFiles([string]$Directory) {
    return @(Get-ChildItem -LiteralPath $Directory -File | Where-Object {
        $_.Extension -in @('.exe', '.dll', '.json', '.dat') -and
        $_.Name -ne 'YTEC-Sticky-Note-Startup.exe'
    } | Sort-Object Name)
}

function Test-CodeSigningUsage([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    return @($Certificate.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId }) -contains '1.3.6.1.5.5.7.3.3'
}

function Get-OrCreateCodeSigningCertificate {
    $now = Get-Date
    $eligibleCertificates = @(Get-ChildItem Cert:\CurrentUser\My |
        Where-Object {
            $_.Subject -eq $CertificateSubject -and
            $_.Issuer -eq $CertificateSubject -and
            $_.HasPrivateKey -and
            $_.NotBefore -le $now -and
            $_.NotAfter -gt $now.AddMonths(6) -and
            (Test-CodeSigningUsage -Certificate $_)
        })

    $certificate = $null
    if (Test-Path -LiteralPath $certificatePath -PathType Leaf) {
        $publishedCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
        try {
            $certificate = $eligibleCertificates |
                Where-Object Thumbprint -eq $publishedCertificate.Thumbprint |
                Select-Object -First 1
        }
        finally {
            $publishedCertificate.Dispose()
        }
    }

    if (-not $certificate) {
        $certificate = $eligibleCertificates |
            Sort-Object NotBefore |
            Select-Object -First 1
    }
    if (-not $certificate) {
        $certificate = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $CertificateSubject `
            -FriendlyName 'Y-TEC self-signed direct release' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -KeyAlgorithm RSA `
            -KeyLength 3072 `
            -HashAlgorithm SHA256 `
            -KeyExportPolicy NonExportable `
            -NotAfter $now.AddYears(3)
    }

    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
    try {
        if ($rsa -isnot [System.Security.Cryptography.RSACng] -or
            $rsa.Key.ExportPolicy -ne [System.Security.Cryptography.CngExportPolicies]::None) {
            throw 'The code-signing certificate private key is not confirmed as non-exportable.'
        }
    }
    finally {
        if ($null -ne $rsa) {
            $rsa.Dispose()
        }
    }
    return $certificate
}

try {
    foreach ($staleOutput in @($signedArchive, $checksumPath)) {
        if (Test-Path -LiteralPath $staleOutput) { Remove-Item -LiteralPath $staleOutput -Force }
    }

    & (Join-Path $PSScriptRoot 'package.ps1')
    if (-not (Test-Path -LiteralPath $unsignedArchive -PathType Leaf)) {
        throw "Unsigned ZIP not found: $unsignedArchive"
    }
    if (-not (Test-Path -LiteralPath $manualSource -PathType Leaf)) {
        throw "Operation manual not found: $manualSource"
    }

    foreach ($temporaryPath in @($stagingPath, $candidateRoot)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            throw "Signing staging directory unexpectedly exists: $temporaryPath"
        }
    }

    New-Item -ItemType Directory -Path $stagingPath | Out-Null
    New-Item -ItemType Directory -Path $candidateRoot | Out-Null
    Expand-Archive -LiteralPath $unsignedArchive -DestinationPath $stagingPath
    $initialRuntimeFiles = @(Get-RuntimeFiles -Directory $stagingPath)
    $initialRuntimeHashes = @{}
    foreach ($runtimeFile in $initialRuntimeFiles) {
        $initialRuntimeHashes[$runtimeFile.Name] = (Get-FileHash -Algorithm SHA256 -LiteralPath $runtimeFile.FullName).Hash
    }
    foreach ($relativePath in $signedRelativePaths) {
        if (-not $initialRuntimeHashes.ContainsKey($relativePath)) {
            throw "Required runtime file is missing from the unsigned package: $relativePath"
        }
    }

    $certificate = Get-OrCreateCodeSigningCertificate
    foreach ($relativePath in $signedRelativePaths) {
        $target = Join-Path $stagingPath $relativePath
        & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My /tr $TimestampUrl /td SHA256 $target
        if ($LASTEXITCODE -ne 0) { throw "Signing failed: $relativePath" }
    }

    $runtimeFiles = @(Get-RuntimeFiles -Directory $stagingPath)
    if ($runtimeFiles.Count -ne $initialRuntimeFiles.Count) {
        throw 'Runtime file inventory changed while signing.'
    }
    $finalRuntimeHashes = @{}
    foreach ($runtimeFile in $runtimeFiles) {
        if (-not $initialRuntimeHashes.ContainsKey($runtimeFile.Name)) {
            throw "Unexpected runtime file appeared while signing: $($runtimeFile.Name)"
        }
        $currentHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $runtimeFile.FullName).Hash.ToLowerInvariant()
        $finalRuntimeHashes[$runtimeFile.Name] = $currentHash
        if ($runtimeFile.Name -notin $signedRelativePaths) {
            if ($currentHash -ne $initialRuntimeHashes[$runtimeFile.Name].ToLowerInvariant()) {
                throw "Unsigned runtime dependency changed while signing: $($runtimeFile.Name)"
            }
        }
    }
    $manifest = [ordered]@{
        Format = 1
        Product = 'Keisai'
        Version = $appVersion
        Files = @($runtimeFiles | ForEach-Object {
            [ordered]@{
                Name = $_.Name
                Sha256 = $finalRuntimeHashes[$_.Name]
            }
        })
    }
    $manifestBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
        ($manifest | ConvertTo-Json -Depth 5 -Compress)
    )
    $manifestPath = Join-Path $stagingPath 'startup-runtime-manifest.json'
    $signaturePath = Join-Path $stagingPath 'startup-runtime-manifest.p7s'
    [System.IO.File]::WriteAllBytes($manifestPath, $manifestBytes)

    $contentInfo = [System.Security.Cryptography.Pkcs.ContentInfo]::new($manifestBytes)
    $signedCms = [System.Security.Cryptography.Pkcs.SignedCms]::new($contentInfo, $true)
    $cmsSigner = [System.Security.Cryptography.Pkcs.CmsSigner]::new($certificate)
    $cmsSigner.IncludeOption = [System.Security.Cryptography.X509Certificates.X509IncludeOption]::EndCertOnly
    $signedCms.ComputeSignature($cmsSigner, $false)
    [System.IO.File]::WriteAllBytes($signaturePath, $signedCms.Encode())

    Export-Certificate -Cert $certificate -FilePath $candidateCertificate -Type CERT | Out-Null
    Copy-Item -LiteralPath $manualSource -Destination $candidateManual
    Compress-Archive -Path (Join-Path $stagingPath '*') -DestinationPath $candidateArchive -CompressionLevel Optimal

    $publishedFiles = @($candidateArchive, $candidateManual, $candidateCertificate)
    $hashLines = foreach ($file in $publishedFiles) {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
        "$hash  $(Split-Path -Leaf $file)"
    }
    [System.IO.File]::WriteAllLines(
        $candidateChecksum,
        $hashLines,
        [System.Text.UTF8Encoding]::new($false)
    )

    & (Join-Path $PSScriptRoot 'verify-direct-release.ps1') `
        -ArchivePath $candidateArchive `
        -CertificatePath $candidateCertificate `
        -ManualPath $candidateManual `
        -ChecksumPath $candidateChecksum

    $publishingStarted = $true
    $moves = @(
        @($candidateArchive, $signedArchive),
        @($candidateCertificate, $certificatePath),
        @($candidateManual, $manualPath),
        @($candidateChecksum, $checksumPath)
    )
    foreach ($move in $moves) {
        if (Test-Path -LiteralPath $move[1]) { Remove-Item -LiteralPath $move[1] -Force }
        Move-Item -LiteralPath $move[0] -Destination $move[1]
    }
    $releaseSucceeded = $true

    Write-Output "Self-signed ZIP: $signedArchive"
    Write-Output "Public certificate: $certificatePath"
    Write-Output "Checksums: $checksumPath"
}
finally {
    if (-not $releaseSucceeded) {
        $failedOutputs = @($signedArchive, $checksumPath)
        if ($publishingStarted) {
            $failedOutputs += @($certificatePath, $manualPath)
        }
        foreach ($failedOutput in $failedOutputs) {
            if (Test-Path -LiteralPath $failedOutput) { Remove-Item -LiteralPath $failedOutput -Force }
        }
    }

    $artifactPrefix = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
    foreach ($temporaryPath in @($stagingPath, $candidateRoot)) {
        if (-not (Test-Path -LiteralPath $temporaryPath)) { continue }
        $resolvedTemporary = [System.IO.Path]::GetFullPath($temporaryPath)
        if (-not $resolvedTemporary.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a signing directory outside artifacts: $resolvedTemporary"
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
