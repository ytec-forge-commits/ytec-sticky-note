using System.IO;
using System.Runtime.InteropServices;
using Windows.Storage;

namespace YtecStickyNote.Services;

public enum StartupBackend
{
    PortableLocalCache,
    PackagedStartupTask
}

public sealed record AppRuntimeProfile(
    bool IsPackaged,
    string StorageBaseDirectory,
    StartupBackend StartupBackend)
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    public static AppRuntimeProfile Detect()
    {
        var packaged = HasPackageIdentity();
        var executableDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var localStateDirectory = packaged
            ? ApplicationData.Current.LocalFolder.Path
            : null;
        return CreateForTests(packaged, executableDirectory, localStateDirectory, AppRuntimeOptions.PortableDataRootOverride);
    }

    public static AppRuntimeProfile CreateForTests(
        bool isPackaged,
        string executableDirectory,
        string? packagedLocalStateDirectory,
        string? startupDataRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        if (isPackaged)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packagedLocalStateDirectory);
        }

        return new AppRuntimeProfile(
            isPackaged,
            Path.GetFullPath(isPackaged
                ? packagedLocalStateDirectory!
                : string.IsNullOrWhiteSpace(startupDataRoot)
                    ? executableDirectory
                    : startupDataRoot),
            isPackaged ? StartupBackend.PackagedStartupTask : StartupBackend.PortableLocalCache);
    }

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        return result switch
        {
            ErrorInsufficientBuffer => true,
            AppModelErrorNoPackage => false,
            _ => throw new InvalidOperationException($"Windowsパッケージ情報を確認できませんでした（code: {result}）。")
        };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}
