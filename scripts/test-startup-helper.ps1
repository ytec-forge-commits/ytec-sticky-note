param(
    [string]$ApplicationDirectory,
    [string]$HelperPath
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class YtecStickyNoteWindowProbe
{
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    public static bool HasVisibleWindow(int expectedProcessId)
    {
        var found = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processId == (uint)expectedProcessId && IsWindowVisible(window))
            {
                found = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($ApplicationDirectory)) {
    $ApplicationDirectory = Join-Path $projectRoot 'artifacts\Keisai-win-x64'
}
if ([string]::IsNullOrWhiteSpace($HelperPath)) {
    $HelperPath = Join-Path $ApplicationDirectory 'YTEC-Sticky-Note-Startup.exe'
}

$ApplicationDirectory = [System.IO.Path]::GetFullPath($ApplicationDirectory)
$HelperPath = [System.IO.Path]::GetFullPath($HelperPath)
$applicationPath = Join-Path $ApplicationDirectory 'Keisai.exe'
foreach ($requiredPath in @($ApplicationDirectory, $HelperPath, $applicationPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required test input not found: $requiredPath"
    }
}

function Copy-TestApplication([string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $ApplicationDirectory -Force |
        Where-Object { $_.Name -ne 'data' } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force }
}

function Start-ExactProcess([string]$FilePath, [string]$WorkingDirectory, [string[]]$Arguments) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    return [System.Diagnostics.Process]::Start($startInfo)
}

function Wait-ForExitOrFail([System.Diagnostics.Process]$Process, [int]$TimeoutMilliseconds, [string]$Label) {
    if (-not $Process.WaitForExit($TimeoutMilliseconds)) {
        try { $Process.Kill($true) } catch { }
        throw "$Label did not exit within $TimeoutMilliseconds ms."
    }
}

function Wait-ForMainWindow([System.Diagnostics.Process]$Process, [int]$TimeoutMilliseconds, [string]$Label) {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        if ($Process.HasExited) {
            throw "$Label exited before its main window appeared (exit code $($Process.ExitCode))."
        }
        if ([YtecStickyNoteWindowProbe]::HasVisibleWindow($Process.Id)) {
            return
        }
        Start-Sleep -Milliseconds 100
    }
    throw "$Label did not create a main window within $TimeoutMilliseconds ms."
}

function Find-TestApplicationProcess([string]$ExpectedPath, [int]$TimeoutMilliseconds) {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        foreach ($candidate in @(Get-Process -Name 'Keisai' -ErrorAction SilentlyContinue)) {
            try {
                if ([string]::Equals($candidate.Path, $ExpectedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $candidate
                }
            } catch {
                # Ignore processes whose path cannot be queried.
            }
        }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

function Stop-OwnedProcess([System.Diagnostics.Process]$Process) {
    if (-not $Process.HasExited) {
        try { $Process.Kill($true) } catch { $Process.Kill() }
        $Process.WaitForExit(5000) | Out-Null
    }
    $Process.Dispose()
}

$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
$tempRoot = [System.IO.Path]::GetFullPath((Join-Path $tempBase "ytec-sticky-note-startup-integration-$([Guid]::NewGuid().ToString('N'))"))
if (-not $tempRoot.StartsWith($tempBase + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Temporary test directory must stay under the Windows temporary directory.'
}

$ownedProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    $helperDirectory = Join-Path $tempRoot 'helper-only'
    New-Item -ItemType Directory -Path $helperDirectory | Out-Null
    $standaloneHelper = Join-Path $helperDirectory 'YTEC-Sticky-Note-Startup.exe'
    Copy-Item -LiteralPath $HelperPath -Destination $standaloneHelper

    $missingTarget = Join-Path $tempRoot 'missing\Keisai.exe'
    $timeoutConfig = Join-Path $helperDirectory 'timeout-target.txt'
    Set-Content -LiteralPath $timeoutConfig -Value $missingTarget -Encoding utf8NoBOM
    $timeoutWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $timeoutProcess = Start-ExactProcess $standaloneHelper $helperDirectory @(
        '--config', $timeoutConfig,
        '--timeout-seconds', '1',
        '--poll-milliseconds', '50'
    )
    $ownedProcesses.Add($timeoutProcess)
    Wait-ForExitOrFail $timeoutProcess 6000 'Timeout test helper'
    $timeoutWatch.Stop()
    if ($timeoutProcess.ExitCode -ne 0 -or $timeoutWatch.ElapsedMilliseconds -lt 900) {
        throw "Timeout behavior was invalid (exit=$($timeoutProcess.ExitCode), elapsed=$($timeoutWatch.ElapsedMilliseconds) ms)."
    }
    $ownedProcesses.Remove($timeoutProcess) | Out-Null
    $timeoutProcess.Dispose()
    Write-Host "PASS: nonexistent target waited and timed out after $($timeoutWatch.ElapsedMilliseconds) ms."

    $directDirectory = Join-Path $tempRoot 'direct-test-mode'
    Copy-TestApplication $directDirectory
    $directProcess = Start-ExactProcess (Join-Path $directDirectory 'Keisai.exe') $directDirectory @('--test-mode')
    $ownedProcesses.Add($directProcess)
    Wait-ForMainWindow $directProcess 15000 'Direct --test-mode Keisai'
    Write-Host "PASS: direct Keisai --test-mode created a main window (PID $($directProcess.Id))."
    Stop-OwnedProcess $directProcess
    $ownedProcesses.Remove($directProcess) | Out-Null

    $delayedDirectory = Join-Path $tempRoot 'delayed-target'
    $delayedTarget = Join-Path $delayedDirectory 'Keisai.exe'
    $delayedConfig = Join-Path $helperDirectory 'delayed-target.txt'
    Set-Content -LiteralPath $delayedConfig -Value $delayedTarget -Encoding utf8NoBOM
    $waitingHelper = Start-ExactProcess $standaloneHelper $helperDirectory @(
        '--config', $delayedConfig,
        '--timeout-seconds', '15',
        '--poll-milliseconds', '50',
        '--target-argument', '--test-mode'
    )
    $ownedProcesses.Add($waitingHelper)
    Start-Sleep -Milliseconds 350
    if ($waitingHelper.HasExited) {
        throw 'The helper exited before the delayed target became available.'
    }

    Copy-TestApplication $delayedDirectory
    $launchedProcess = Find-TestApplicationProcess $delayedTarget 12000
    if (-not $launchedProcess) {
        throw 'The helper did not launch Keisai after the delayed target became ready.'
    }
    $ownedProcesses.Add($launchedProcess)
    Wait-ForMainWindow $launchedProcess 15000 'Helper-launched Keisai'
    Wait-ForExitOrFail $waitingHelper 5000 'Helper after target launch'
    if ($waitingHelper.ExitCode -ne 0) {
        throw "Helper launch test returned exit code $($waitingHelper.ExitCode)."
    }
    Write-Host "PASS: standalone helper launched delayed Keisai in --test-mode (PID $($launchedProcess.Id))."
    Stop-OwnedProcess $launchedProcess
    $ownedProcesses.Remove($launchedProcess) | Out-Null
    $ownedProcesses.Remove($waitingHelper) | Out-Null
    $waitingHelper.Dispose()

    $helperFiles = @(Get-ChildItem -LiteralPath $helperDirectory -File | Select-Object -ExpandProperty Name)
    if ($helperFiles | Where-Object { $_ -match '(?i)^(VCRUNTIME|MSVCP|ucrtbase|api-ms-win-crt-).*\.dll$' }) {
        throw 'A redistributable CRT DLL was copied beside the standalone helper.'
    }
    Write-Host 'PASS: helper-only tests required no companion CRT DLL.'
} finally {
    foreach ($process in @($ownedProcesses)) {
        try { Stop-OwnedProcess $process } catch { }
    }
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTempRoot = [System.IO.Path]::GetFullPath($tempRoot)
        if (-not $resolvedTempRoot.StartsWith($tempBase + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a test directory outside the Windows temporary directory.'
        }
        Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
    }
}
