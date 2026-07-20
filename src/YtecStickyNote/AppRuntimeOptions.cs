namespace YtecStickyNote;

public static class AppRuntimeOptions
{
    private const string TestModeDataKey = "YtecStickyNote.TestMode";

    public static bool IsTestMode =>
        AppContext.GetData(TestModeDataKey) is true ||
        Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "--test-mode", StringComparison.OrdinalIgnoreCase));

    public static void EnableTestModeForCurrentProcess() => AppContext.SetData(TestModeDataKey, true);
}
