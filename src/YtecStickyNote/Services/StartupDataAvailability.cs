using System.Diagnostics;
using System.IO;

namespace YtecStickyNote.Services;

public static class StartupDataAvailability
{
    private static readonly string[] StateFileNames = ["sticky-note.json", "window-state.json"];

    public static async Task<bool> WaitUntilReadyAsync(
        string baseDirectory,
        TimeSpan timeout,
        TimeSpan? stableDuration = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var requiredStableDuration = stableDuration ?? TimeSpan.FromSeconds(3);
        var delay = pollInterval ?? TimeSpan.FromSeconds(1);
        if (requiredStableDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stableDuration));
        }
        if (delay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        var fullBaseDirectory = Path.GetFullPath(baseDirectory);
        var stopwatch = Stopwatch.StartNew();
        TimeSpan? readySince = null;

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryProbe(fullBaseDirectory))
            {
                readySince ??= stopwatch.Elapsed;
                if (stopwatch.Elapsed - readySince.Value >= requiredStableDuration)
                {
                    return true;
                }
            }
            else
            {
                readySince = null;
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(delay < remaining ? delay : remaining, cancellationToken);
        }

        return false;
    }

    private static bool TryProbe(string baseDirectory)
    {
        if (!Directory.Exists(baseDirectory))
        {
            return false;
        }

        var dataDirectory = Path.Combine(baseDirectory, "data");
        var probePath = Path.Combine(dataDirectory, $".keisai-startup-probe-{Guid.NewGuid():N}");
        var movedProbePath = probePath + ".moved";
        try
        {
            Directory.CreateDirectory(dataDirectory);
            foreach (var fileName in StateFileNames)
            {
                var statePath = Path.Combine(dataDirectory, fileName);
                if (!File.Exists(statePath))
                {
                    continue;
                }

                using var stateStream = new FileStream(
                    statePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete);
                stateStream.CopyTo(Stream.Null);
            }

            using (var probeStream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                probeStream.WriteByte(0x4B);
                probeStream.Flush(flushToDisk: true);
            }
            File.Move(probePath, movedProbePath);
            File.Delete(movedProbePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDeleteProbe(probePath);
            TryDeleteProbe(movedProbePath);
            return false;
        }
    }

    private static void TryDeleteProbe(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 次回の準備確認を妨げない一意名なので、クラウド側が使用中の場合は残置する。
        }
    }
}
