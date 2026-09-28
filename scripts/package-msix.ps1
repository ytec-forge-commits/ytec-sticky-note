param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9.-]+$')]
    [string]$PackageIdentityName,
    [Parameter(Mandatory = $true)]
    [string]$Publisher,
    [string]$PackageVersion,
    [switch]$CreateUpload
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows-sdk-tools.ps1')

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'src\YtecStickyNote\YtecStickyNote.csproj'
[xml]$projectDefinition = Get-Content -LiteralPath $projectFile -Raw -Encoding UTF8
$appVersion = [string]$projectDefinition.Project.PropertyGroup.Version
if (-not $PackageVersion) {
    if ($appVersion -notmatch '^(?<major>[1-9]\d*)\.(?<minor>\d+)\.(?<patch>\d+)$') {
        throw 'Cannot infer the Store version. Specify -PackageVersion (for example, 1.6.0.0).'
    }
    $PackageVersion = "$($Matches.major).$($Matches.minor).$($Matches.patch).0"
}
if ($PackageVersion -notmatch '^[1-9]\d{0,4}\.(\d{1,5})\.(\d{1,5})\.0$') {
    throw 'The MSIX version must use major.minor.build.0 format.'
}
if ($PackageVersion.Split('.') | Where-Object { [int]$_ -gt 65535 }) {
    throw 'Every MSIX version component must be 65535 or lower.'
}

$artifactRoot = Join-Path $projectRoot 'artifacts'
$storeRoot = Join-Path $artifactRoot 'store'
$stagingPath = Join-Path $artifactRoot ('.msix-staging-' + [Guid]::NewGuid().ToString('N'))
$uploadStagingPath = Join-Path $artifactRoot ('.msixupload-staging-' + [Guid]::NewGuid().ToString('N'))
$msixPath = Join-Path $storeRoot "Keisai-$appVersion-store-x64.msix"
$uploadPath = Join-Path $storeRoot "Keisai-$appVersion-store-x64.msixupload"
$manifestTemplate = Join-Path $projectRoot 'packaging\msix\AppxManifest.xml.in'
$assetRoot = Join-Path $projectRoot 'packaging\msix\Assets'
$makeAppx = Get-LatestWindowsSdkTool -Name 'makeappx.exe'
$expectedDisplayName = -join ([char]0x7F6B, [char]0x5F69)
$dotnetExe = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe -PathType Leaf)) {
    $dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
}

try {
    New-Item -ItemType Directory -Force -Path $storeRoot | Out-Null
    foreach ($temporaryPath in @($stagingPath, $uploadStagingPath)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            throw "Packaging staging directory unexpectedly exists: $temporaryPath"
        }
    }
    foreach ($staleOutput in @($msixPath, $uploadPath)) {
        if (Test-Path -LiteralPath $staleOutput) { Remove-Item -LiteralPath $staleOutput -Force }
    }

    New-Item -ItemType Directory -Path $stagingPath | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingPath 'app') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingPath 'assets') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingPath 'legal') | Out-Null

    & $dotnetExe publish $projectFile -c Release -r win-x64 --self-contained true -o (Join-Path $stagingPath 'app')
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed for the Store package.' }

    Copy-Item -LiteralPath (Join-Path $stagingPath 'app\YTEC-Sticky-Note.exe') -Destination (Join-Path $stagingPath 'app\Keisai.exe')
    foreach ($assetName in @('StoreLogo.png', 'Square44x44Logo.png', 'Square71x71Logo.png', 'Square150x150Logo.png')) {
        Copy-Item -LiteralPath (Join-Path $assetRoot $assetName) -Destination (Join-Path $stagingPath "assets\$assetName")
    }
    foreach ($legalFile in @('LICENSE.txt', 'NOTICE', 'THIRD_PARTY_NOTICES.md', 'PRIVACY.md', 'CODE_SIGNING_POLICY.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $legalFile) -Destination (Join-Path $stagingPath "legal\$legalFile")
    }
    & (Join-Path $PSScriptRoot 'collect-third-party-licenses.ps1') `
        -DestinationRoot (Join-Path $stagingPath 'legal\third-party-licenses') `
        -DotnetExecutable $dotnetExe

    $manifest = [System.IO.File]::ReadAllText($manifestTemplate, [System.Text.UTF8Encoding]::new($false))
    $manifest = $manifest.Replace('__PACKAGE_IDENTITY_NAME__', $PackageIdentityName)
    $manifest = $manifest.Replace('__PUBLISHER__', [System.Security.SecurityElement]::Escape($Publisher))
    $manifest = $manifest.Replace('__PACKAGE_VERSION__', $PackageVersion)
    if ($manifest -match '__[A-Z_]+__') { throw 'The MSIX manifest contains an unreplaced placeholder.' }
    [System.IO.File]::WriteAllText((Join-Path $stagingPath 'AppxManifest.xml'), $manifest, [System.Text.UTF8Encoding]::new($false))

    if (Test-Path -LiteralPath $msixPath) { Remove-Item -LiteralPath $msixPath -Force }
    & $makeAppx pack /o /h SHA256 /d $stagingPath /p $msixPath
    if ($LASTEXITCODE -ne 0) { throw 'MakeAppx failed to create the MSIX package.' }

    & (Join-Path $PSScriptRoot 'verify-msix-package.ps1') `
        -Path $msixPath `
        -ExpectedVersion $PackageVersion `
        -ExpectedPackageName $PackageIdentityName `
        -ExpectedPublisher $Publisher `
        -ExpectedDisplayName $expectedDisplayName

    if ($CreateUpload) {
        New-Item -ItemType Directory -Path $uploadStagingPath | Out-Null
        Copy-Item -LiteralPath $msixPath -Destination (Join-Path $uploadStagingPath (Split-Path -Leaf $msixPath))
        $temporaryZip = [System.IO.Path]::ChangeExtension($uploadPath, '.zip')
        foreach ($output in @($temporaryZip, $uploadPath)) {
            if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
        }
        Compress-Archive -Path (Join-Path $uploadStagingPath '*') -DestinationPath $temporaryZip -CompressionLevel Optimal
        Move-Item -LiteralPath $temporaryZip -Destination $uploadPath
    }

    Write-Output "Store MSIX: $msixPath"
    if ($CreateUpload) { Write-Output "Store upload: $uploadPath" }
}
finally {
    $artifactPrefix = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
    foreach ($temporaryPath in @($stagingPath, $uploadStagingPath)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            $resolvedTemporary = [System.IO.Path]::GetFullPath($temporaryPath)
            if (-not $resolvedTemporary.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove a temporary directory outside artifacts: $resolvedTemporary"
            }
            Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
        }
    }
}
