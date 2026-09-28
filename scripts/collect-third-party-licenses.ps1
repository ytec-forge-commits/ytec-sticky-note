param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationRoot,
    [Parameter(Mandatory = $true)]
    [string]$DotnetExecutable
)

$ErrorActionPreference = 'Stop'

$destination = [System.IO.Path]::GetFullPath($DestinationRoot)
$dotnetPath = (Resolve-Path -LiteralPath $DotnetExecutable).Path
$dotnetRoot = Split-Path -Parent $dotnetPath
$dotnetVersion = ((Get-Item -LiteralPath $dotnetPath).VersionInfo.ProductVersion -split '\s+')[0]

New-Item -ItemType Directory -Force -Path $destination | Out-Null

$dotnetDestination = Join-Path $destination "dotnet-$dotnetVersion"
New-Item -ItemType Directory -Force -Path $dotnetDestination | Out-Null
foreach ($fileName in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    $source = Join-Path $dotnetRoot $fileName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "The .NET legal file is missing: $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $dotnetDestination $fileName)
}

$inventory = [ordered]@{
    generatedFrom = 'The exact local .NET release toolchain'
    dotnet = [ordered]@{
        runtimeVersion = $dotnetVersion
        source = "https://github.com/dotnet/runtime/tree/v$dotnetVersion"
        files = @('LICENSE.txt', 'ThirdPartyNotices.txt')
    }
}
$inventoryPath = Join-Path $destination 'component-inventory.json'
[System.IO.File]::WriteAllText(
    $inventoryPath,
    (($inventory | ConvertTo-Json -Depth 5) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Collected third-party license texts: $destination"
