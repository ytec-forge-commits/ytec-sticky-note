param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$testRoot = Join-Path $artifactRoot ('.startup-cache-registration-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $testRoot 'source'
$local = Join-Path $testRoot 'local'
$tamperedSource = Join-Path $testRoot 'tampered-source'
$tamperedLocal = Join-Path $testRoot 'tampered-local'
$tamperedHostSource = Join-Path $testRoot 'tampered-host-source'
$tamperedHostLocal = Join-Path $testRoot 'tampered-host-local'
$dotnetExe = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$testAssembly = Join-Path $projectRoot 'tests\YtecStickyNote.Tests\bin\Release\net10.0-windows10.0.17763.0\win-x64\YtecStickyNote.Tests.dll'

try {
    New-Item -ItemType Directory -Path $source, $local | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $source

    $valueName = 'Y-TEC Sticky Note Integration ' + [guid]::NewGuid().ToString('N')
    & $dotnetExe run `
        --project (Join-Path $projectRoot 'tests\YtecStickyNote.Tests\YtecStickyNote.Tests.csproj') `
        -c Release `
        --no-build `
        -- `
        --startup-registration-integration `
        $valueName `
        $source `
        $local
    if ($LASTEXITCODE -ne 0) {
        throw "Registration integration failed with exit code $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Path $tamperedSource, $tamperedLocal | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $tamperedSource -Recurse -Force
    Copy-Item -LiteralPath $testAssembly -Destination (Join-Path $tamperedSource 'Keisai.dll') -Force

    $tamperedValueName = 'Y-TEC Sticky Note Tampered Integration ' + [guid]::NewGuid().ToString('N')
    & $dotnetExe run `
        --project (Join-Path $projectRoot 'tests\YtecStickyNote.Tests\YtecStickyNote.Tests.csproj') `
        -c Release `
        --no-build `
        -- `
        --startup-registration-integration `
        $tamperedValueName `
        $tamperedSource `
        $tamperedLocal
    if ($LASTEXITCODE -eq 0) {
        throw 'Unsigned Keisai.dll was accepted into the startup cache.'
    }

    New-Item -ItemType Directory -Path $tamperedHostSource, $tamperedHostLocal | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $tamperedHostSource -Recurse -Force
    Copy-Item -LiteralPath $testAssembly -Destination (Join-Path $tamperedHostSource 'YTEC-Sticky-Note.dll') -Force

    $tamperedHostValueName = 'Y-TEC Sticky Note Tampered Host Integration ' + [guid]::NewGuid().ToString('N')
    & $dotnetExe run `
        --project (Join-Path $projectRoot 'tests\YtecStickyNote.Tests\YtecStickyNote.Tests.csproj') `
        -c Release `
        --no-build `
        -- `
        --startup-registration-integration `
        $tamperedHostValueName `
        $tamperedHostSource `
        $tamperedHostLocal
    if ($LASTEXITCODE -eq 0) {
        throw 'Unsigned YTEC-Sticky-Note.dll was accepted into the startup cache.'
    }

    $cachedExecutable = Get-ChildItem -LiteralPath (Join-Path $local 'app') -Filter 'Keisai.exe' -File -Recurse |
        Select-Object -First 1
    if (-not $cachedExecutable) {
        throw 'Cached Keisai.exe was not created.'
    }

    & pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'test-startup-cache.ps1') `
        -ApplicationDirectory $cachedExecutable.DirectoryName
    if ($LASTEXITCODE -ne 0) {
        throw "Cached application integration failed with exit code $LASTEXITCODE."
    }

    Write-Host 'PASS: signed Keisai registered, copied, verified, launched from the local cache, rejected both tampered application DLL aliases, and removed its test Run entries.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $artifactPrefix = $artifactRoot.TrimEnd('\') + '\'
        $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
        if (-not $resolvedTestRoot.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a startup registration test directory outside artifacts: $resolvedTestRoot"
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
