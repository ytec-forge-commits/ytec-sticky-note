param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "YTEC-Sticky-Note-$Runtime"))
$stagingDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot ".package-staging-$Runtime"))
$zipPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "YTEC-Sticky-Note-1.3.0-$Runtime.zip"))
$projectFile = Join-Path $projectRoot 'src\YtecStickyNote\YtecStickyNote.csproj'
$dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnetExe)) {
    $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source
}

$artifactPrefix = $artifactRoot.TrimEnd('\') + '\'
foreach ($path in @($publishDirectory, $stagingDirectory, $zipPath)) {
    if (-not $path.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Package outputs must stay under artifacts.'
    }
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

& $dotnetExe publish $projectFile -c Release -r $Runtime --self-contained true -o $stagingDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\README-PORTABLE.txt') -Destination (Join-Path $stagingDirectory 'README.txt')
Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
$publishPrefix = $publishDirectory.TrimEnd('\') + '\'
Get-ChildItem -LiteralPath $publishDirectory -Force |
    Where-Object { $_.Name -ne 'data' } |
    ForEach-Object {
        if (-not $_.FullName.StartsWith($publishPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to replace an item outside the publish directory.'
        }
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }

Get-ChildItem -LiteralPath $stagingDirectory -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $publishDirectory -Recurse -Force
}
Remove-Item -LiteralPath $stagingDirectory -Recurse -Force

Write-Host "Publish directory: $publishDirectory"
Write-Host "Publish ZIP: $zipPath"
Write-Host 'Existing publish-directory data was preserved; data is not included in the ZIP.'
