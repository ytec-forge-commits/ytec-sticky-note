param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$productName = 'Keisai'
$productVersion = '1.5.0'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "$productName-$Runtime"))
$stagingDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot ".package-staging-$Runtime"))
$zipPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "$productName-$productVersion-$Runtime.zip"))
$checksumPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "$productName-$productVersion-$Runtime.sha256.txt"))
$projectFile = Join-Path $projectRoot 'src\YtecStickyNote\YtecStickyNote.csproj'
$startupManifest = Join-Path $projectRoot 'src\YtecStickyNote.Startup\Cargo.toml'
$startupExecutable = Join-Path $projectRoot 'src\YtecStickyNote.Startup\target\release\YTEC-Sticky-Note-Startup.exe'
$dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$cargoExe = (Get-Command cargo -ErrorAction Stop).Source

if (-not (Test-Path -LiteralPath $dotnetExe)) {
    $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source
}

$artifactPrefix = $artifactRoot.TrimEnd('\') + '\'
foreach ($path in @($publishDirectory, $stagingDirectory, $zipPath, $checksumPath)) {
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
if (Test-Path -LiteralPath $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}

& $dotnetExe publish $projectFile -c Release -r $Runtime --self-contained true -o $stagingDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

& $cargoExe build --manifest-path $startupManifest --release --locked
if ($LASTEXITCODE -ne 0) {
    throw "cargo build failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath $startupExecutable -Destination (Join-Path $stagingDirectory 'YTEC-Sticky-Note-Startup.exe')
Copy-Item -LiteralPath (Join-Path $stagingDirectory 'YTEC-Sticky-Note.exe') -Destination (Join-Path $stagingDirectory 'Keisai.exe')
Copy-Item -LiteralPath (Join-Path $stagingDirectory 'YTEC-Sticky-Note.dll') -Destination (Join-Path $stagingDirectory 'Keisai.dll')
Copy-Item -LiteralPath (Join-Path $stagingDirectory 'YTEC-Sticky-Note.deps.json') -Destination (Join-Path $stagingDirectory 'Keisai.deps.json')
Copy-Item -LiteralPath (Join-Path $stagingDirectory 'YTEC-Sticky-Note.runtimeconfig.json') -Destination (Join-Path $stagingDirectory 'Keisai.runtimeconfig.json')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\README-PORTABLE.txt') -Destination (Join-Path $stagingDirectory 'README.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.md') -Destination (Join-Path $stagingDirectory 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\PRIVACY.txt') -Destination (Join-Path $stagingDirectory 'PRIVACY.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination (Join-Path $stagingDirectory 'CHANGELOG.txt')
Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$zipHash  $([System.IO.Path]::GetFileName($zipPath))" -Encoding ascii

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
Write-Host "SHA-256: $zipHash"
Write-Host "Checksum file: $checksumPath"
Write-Host 'Existing publish-directory data was preserved; data is not included in the ZIP.'
