param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
$ExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw "Startup helper not found: $ExecutablePath"
}

function Find-Dumpbin {
    $command = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        return $null
    }

    $installationPath = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($installationPath)) {
        return $null
    }

    return Get-ChildItem -Path (Join-Path $installationPath 'VC\Tools\MSVC\*\bin\Hostx64\x64\dumpbin.exe') -File -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

$dependencies = @()
$dumpbin = Find-Dumpbin
if ($dumpbin) {
    $analysis = (& $dumpbin /nologo /dependents $ExecutablePath 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "dumpbin dependency analysis failed with exit code $LASTEXITCODE."
    }
    $dependencies = [regex]::Matches($analysis, '(?im)^\s+([a-z0-9._-]+\.dll)\s*$') |
        ForEach-Object { $_.Groups[1].Value }
} else {
    $objdump = Get-Command objdump.exe -ErrorAction SilentlyContinue
    if (-not $objdump) {
        throw 'Neither dumpbin.exe nor objdump.exe is available for PE dependency analysis.'
    }
    $analysis = (& $objdump.Source -p $ExecutablePath 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "objdump dependency analysis failed with exit code $LASTEXITCODE."
    }
    $dependencies = [regex]::Matches($analysis, '(?im)^\s*DLL Name:\s*(\S+)\s*$') |
        ForEach-Object { $_.Groups[1].Value }
}

$dependencies = @($dependencies | Sort-Object -Unique)
if ($dependencies.Count -eq 0) {
    throw 'No PE import dependencies were found; dependency analysis may be incomplete.'
}

$forbidden = @($dependencies | Where-Object {
    $_ -match '^(?i:VCRUNTIME[^\\]*|MSVCP[^\\]*|ucrtbase|api-ms-win-crt-[^\\]*)\.dll$'
})

Write-Host "Startup helper dependencies ($($dependencies.Count)):"
$dependencies | ForEach-Object { Write-Host "  $_" }

if ($forbidden.Count -gt 0) {
    throw "The startup helper still depends on redistributable MSVC/UCRT DLLs: $($forbidden -join ', ')"
}

Write-Host 'PASS: no redistributable MSVC/UCRT DLL dependency was found.'
