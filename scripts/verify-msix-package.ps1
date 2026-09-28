param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedPackageName,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedPublisher,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedDisplayName
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows-sdk-tools.ps1')

$packagePath = (Resolve-Path -LiteralPath $Path).Path
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('keisai-msix-verify-' + [guid]::NewGuid().ToString('N'))
$unpackPath = Join-Path $temporaryRoot 'unpacked'
$makeAppx = Get-LatestWindowsSdkTool -Name 'makeappx.exe'

try {
    New-Item -ItemType Directory -Force -Path $unpackPath | Out-Null
    & $makeAppx unpack /o /p $packagePath /d $unpackPath
    if ($LASTEXITCODE -ne 0) { throw 'MakeAppx failed to unpack the MSIX package.' }

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create((Join-Path $unpackPath 'AppxManifest.xml'), $settings)
    try {
        $manifest = [System.Xml.XmlDocument]::new()
        $manifest.XmlResolver = $null
        $manifest.Load($reader)
    }
    finally {
        $reader.Dispose()
    }

    $namespaces = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaces.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $namespaces.AddNamespace('desktop', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10')
    $namespaces.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')

    $identity = $manifest.SelectSingleNode('/f:Package/f:Identity', $namespaces)
    if (-not $identity) { throw 'The MSIX manifest has no Identity element.' }
    if ($identity.GetAttribute('Name') -ne $ExpectedPackageName) { throw 'Package Identity Name does not match the expected value.' }
    if ($identity.GetAttribute('Publisher') -ne $ExpectedPublisher) { throw 'Package Publisher does not match the expected value.' }
    if ($identity.GetAttribute('Version') -ne $ExpectedVersion) { throw 'Package version does not match the expected value.' }
    if ($identity.GetAttribute('ProcessorArchitecture') -ne 'x64') { throw 'Package architecture is not x64.' }

    $displayName = $manifest.SelectSingleNode('/f:Package/f:Properties/f:DisplayName', $namespaces)
    if (-not $displayName -or $displayName.InnerText -ne $ExpectedDisplayName) {
        throw "Package DisplayName does not match the reserved Store name: $ExpectedDisplayName"
    }

    $application = $manifest.SelectSingleNode('/f:Package/f:Applications/f:Application', $namespaces)
    if (-not $application -or $application.GetAttribute('Executable') -ne 'app\Keisai.exe') {
        throw 'The Keisai full-trust executable declaration is invalid.'
    }
    if ($application.GetAttribute('EntryPoint') -ne 'Windows.FullTrustApplication') {
        throw 'The Keisai EntryPoint is not a full-trust application.'
    }
    if (-not $manifest.SelectSingleNode("//desktop:StartupTask[@TaskId='KeisaiStartup']", $namespaces)) {
        throw 'The KeisaiStartup StartupTask declaration is missing.'
    }
    if (-not $manifest.SelectSingleNode("/f:Package/f:Capabilities/rescap:Capability[@Name='runFullTrust']", $namespaces)) {
        throw 'The runFullTrust capability is missing.'
    }
    if ($manifest.SelectSingleNode("/f:Package/f:Capabilities/rescap:Capability[@Name='unvirtualizedResources']", $namespaces)) {
        throw 'The Store package must not declare unvirtualizedResources because it uses LocalState.'
    }

    $requiredFiles = @(
        'app\Keisai.exe',
        'app\YTEC-Sticky-Note.dll',
        'assets\StoreLogo.png',
        'assets\Square44x44Logo.png',
        'assets\Square71x71Logo.png',
        'assets\Square150x150Logo.png',
        'legal\LICENSE.txt',
        'legal\NOTICE',
        'legal\THIRD_PARTY_NOTICES.md',
        'legal\PRIVACY.md',
        'legal\third-party-licenses\component-inventory.json',
        'legal\third-party-licenses\dotnet-10.0.10\LICENSE.txt',
        'legal\third-party-licenses\dotnet-10.0.10\ThirdPartyNotices.txt'
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $unpackPath $requiredFile) -PathType Leaf)) {
            throw "The MSIX package is missing a required file: $requiredFile"
        }
    }
    if (Test-Path -LiteralPath (Join-Path $unpackPath 'app\data')) {
        throw 'The MSIX package must not contain a user data directory.'
    }
    if (Test-Path -LiteralPath (Join-Path $unpackPath 'app\YTEC-Sticky-Note-Startup.exe')) {
        throw 'The Store package must not contain the retired startup helper.'
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagePath).Hash.ToLowerInvariant()
    Write-Output "Verified MSIX structure: $packagePath"
    Write-Output "Package version: $($identity.GetAttribute('Version'))"
    Write-Output "SHA-256: $hash"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $tempPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        $resolvedTemporary = [System.IO.Path]::GetFullPath($temporaryRoot)
        if (-not $resolvedTemporary.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a verification directory outside TEMP: $resolvedTemporary"
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
