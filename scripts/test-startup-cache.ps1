param(
    [string]$ApplicationDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ApplicationDirectory) {
    $ApplicationDirectory = Join-Path $projectRoot 'artifacts\Keisai-win-x64'
}
$applicationRoot = (Resolve-Path -LiteralPath $ApplicationDirectory).Path
$executable = Join-Path $applicationRoot 'Keisai.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Keisai executable not found: $executable"
}
if (Test-Path -LiteralPath (Join-Path $applicationRoot 'YTEC-Sticky-Note-Startup.exe')) {
    throw 'The retired startup helper is still present in the application directory.'
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('keisai-startup-cache-test-' + [guid]::NewGuid().ToString('N'))
$ownedProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class KeisaiTestWindowFinder
{
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    public static IntPtr FindVisibleWindow(int expectedProcessId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, parameter) =>
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (processId == expectedProcessId && IsWindowVisible(window))
            {
                found = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

function Start-ExactProcess([string[]]$Arguments) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executable
    $startInfo.WorkingDirectory = $applicationRoot
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if (-not $process) { throw 'Failed to start Keisai.' }
    $ownedProcesses.Add($process)
    return $process
}

function Wait-ForMainWindow([System.Diagnostics.Process]$Process, [int]$TimeoutMilliseconds, [string]$Description) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "$Description exited before creating a main window. ExitCode=$($Process.ExitCode)"
        }
        if ([KeisaiTestWindowFinder]::FindVisibleWindow($Process.Id) -ne [IntPtr]::Zero) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "$Description did not create a main window within $TimeoutMilliseconds ms."
}

function Stop-OwnedProcess([System.Diagnostics.Process]$Process) {
    if (-not $Process.HasExited) {
        $Process.Kill($true)
        $Process.WaitForExit(5000) | Out-Null
    }
    $ownedProcesses.Remove($Process) | Out-Null
    $Process.Dispose()
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

    $directDataRoot = Join-Path $temporaryRoot 'direct-data'
    New-Item -ItemType Directory -Path $directDataRoot | Out-Null
    $direct = Start-ExactProcess @('--test-mode', '--startup-data-root', $directDataRoot)
    Wait-ForMainWindow $direct 15000 'Direct test-mode Keisai'
    Write-Host "PASS: direct Keisai --test-mode created a main window (PID $($direct.Id))."
    Stop-OwnedProcess $direct

    $missingRoot = Join-Path $temporaryRoot 'never-ready'
    $timeoutProcess = Start-ExactProcess @(
        '--test-mode',
        '--startup-data-root', $missingRoot,
        '--startup-wait-for-data',
        '--startup-wait-timeout-ms', '1200'
    )
    if (-not $timeoutProcess.WaitForExit(6000)) {
        throw 'Keisai did not exit after the startup data wait timed out.'
    }
    if ($timeoutProcess.ExitCode -ne 0) {
        throw "Keisai returned a non-zero exit code after timeout: $($timeoutProcess.ExitCode)"
    }
    $ownedProcesses.Remove($timeoutProcess) | Out-Null
    $timeoutProcess.Dispose()
    Write-Host 'PASS: a missing startup data root timed out without showing an error.'

    $delayedRoot = Join-Path $temporaryRoot 'delayed-ready'
    $delayedProcess = Start-ExactProcess @(
        '--test-mode',
        '--startup-data-root', $delayedRoot,
        '--startup-wait-for-data',
        '--startup-wait-timeout-ms', '12000'
    )
    Start-Sleep -Milliseconds 700
    if ($delayedProcess.HasExited) {
        throw 'Keisai exited before the delayed startup data root became available.'
    }
    if ([KeisaiTestWindowFinder]::FindVisibleWindow($delayedProcess.Id) -ne [IntPtr]::Zero) {
        throw 'Keisai displayed its window before the startup data root became available.'
    }

    New-Item -ItemType Directory -Path $delayedRoot | Out-Null
    Wait-ForMainWindow $delayedProcess 10000 'Delayed-data Keisai'
    $dataDirectory = Join-Path $delayedRoot 'data'
    if (-not (Test-Path -LiteralPath $dataDirectory -PathType Container)) {
        throw 'Keisai did not prepare the portable data directory after the source became ready.'
    }
    if (Get-ChildItem -LiteralPath $dataDirectory -Filter '.keisai-startup-probe-*' -File) {
        throw 'A startup readiness probe file was left in the portable data directory.'
    }
    Write-Host "PASS: Keisai waited for delayed portable data and then displayed its window (PID $($delayedProcess.Id))."
    Stop-OwnedProcess $delayedProcess
}
finally {
    foreach ($process in @($ownedProcesses)) {
        try { Stop-OwnedProcess $process } catch { }
    }
    if (Test-Path -LiteralPath $temporaryRoot) {
        $tempPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        $resolvedTemporary = [System.IO.Path]::GetFullPath($temporaryRoot)
        if (-not $resolvedTemporary.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a startup test directory outside TEMP: $resolvedTemporary"
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
