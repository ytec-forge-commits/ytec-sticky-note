using System.IO;

namespace YtecStickyNote;

public static class AppRuntimeOptions
{
    private const string TestModeDataKey = "YtecStickyNote.TestMode";
    private const string TestModeDataRootKey = "YtecStickyNote.TestModeDataRoot";
    private static readonly string[] Arguments = Environment.GetCommandLineArgs();
    private static readonly string DefaultTestDataRoot = Path.Combine(
        Path.GetTempPath(),
        $"keisai-test-mode-{Environment.ProcessId}-{Guid.NewGuid():N}");

    public static bool IsTestMode =>
        AppContext.GetData(TestModeDataKey) is true ||
        Arguments.Any(argument => string.Equals(argument, "--test-mode", StringComparison.OrdinalIgnoreCase));

    public static string? StartupDataRoot => GetArgumentValue("--startup-data-root");

    public static string? PortableDataRootOverride =>
        !string.IsNullOrWhiteSpace(StartupDataRoot)
            ? Path.GetFullPath(StartupDataRoot)
            : AppContext.GetData(TestModeDataRootKey) is string testRoot && !string.IsNullOrWhiteSpace(testRoot)
                ? Path.GetFullPath(testRoot)
                : IsTestMode
                    ? DefaultTestDataRoot
                    : null;

    public static bool ShouldWaitForStartupData =>
        !string.IsNullOrWhiteSpace(StartupDataRoot) &&
        Arguments.Any(argument => string.Equals(argument, "--startup-wait-for-data", StringComparison.OrdinalIgnoreCase));

    public static TimeSpan StartupWaitTimeout
    {
        get
        {
            if (IsTestMode &&
                int.TryParse(GetArgumentValue("--startup-wait-timeout-ms"), out var milliseconds) &&
                milliseconds is >= 100 and <= 300_000)
            {
                return TimeSpan.FromMilliseconds(milliseconds);
            }

            return TimeSpan.FromMinutes(10);
        }
    }

    public static void EnableTestModeForCurrentProcess(string? dataRoot = null)
    {
        AppContext.SetData(TestModeDataKey, true);
        if (!string.IsNullOrWhiteSpace(dataRoot))
        {
            AppContext.SetData(TestModeDataRootKey, Path.GetFullPath(dataRoot));
        }
    }

    private static string? GetArgumentValue(string name)
    {
        for (var index = 0; index < Arguments.Length - 1; index++)
        {
            if (string.Equals(Arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return Arguments[index + 1];
            }
        }

        return null;
    }
}
