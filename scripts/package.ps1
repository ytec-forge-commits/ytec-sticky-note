param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "YTEC-Sticky-Note-$Runtime"))
$zipPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "YTEC-Sticky-Note-1.0.0-$Runtime.zip"))
$projectFile = Join-Path $projectRoot 'src\YtecStickyNote\YtecStickyNote.csproj'
$dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnetExe)) {
    $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source
}

if (-not $publishDirectory.StartsWith($artifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish directory must stay under artifacts.'
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

& $dotnetExe publish $projectFile -c Release -r $Runtime --self-contained true -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\README-PORTABLE.txt') -Destination (Join-Path $publishDirectory 'README.txt')
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Publish directory: $publishDirectory"
Write-Host "Publish ZIP: $zipPath"
