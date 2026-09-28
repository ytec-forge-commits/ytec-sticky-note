using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Win32;

namespace YtecStickyNote.Services;

public enum StartupRegistrationStatus
{
    Disabled,
    Enabled,
    NeedsSecurityUpgrade
}

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string PreferredExecutableName = "Keisai.exe";
    private const string FallbackExecutableName = "YTEC-Sticky-Note.exe";
    private const string LegacyHelperFileName = "YTEC-Sticky-Note-Startup.exe";
    private const string LegacyConfigFileName = "startup-target.txt";
    private const string RegistrationFileName = "startup-registration.json";
    private static readonly HashSet<string> CacheableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".json", ".dat"
    };

    private readonly string _valueName;
    private readonly string _sourceApplicationDirectory;
    private readonly string _sourceExecutablePath;
    private readonly byte[]? _sourceSigningCertificateRawData;

    public StartupService(
        string valueName = "Y-TEC Sticky Note",
        string? sourceApplicationDirectory = null,
        string? localStartupDirectory = null)
    {
        _valueName = valueName;
        _sourceApplicationDirectory = Path.GetFullPath(sourceApplicationDirectory ?? AppContext.BaseDirectory);
        _sourceExecutablePath = ResolveSourceExecutablePath(_sourceApplicationDirectory);
        var processPath = Environment.ProcessPath;
        var processFileName = processPath is null ? null : Path.GetFileName(processPath);
        var signingAnchorPath = processPath is not null &&
            (string.Equals(processFileName, PreferredExecutableName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(processFileName, FallbackExecutableName, StringComparison.OrdinalIgnoreCase))
                ? processPath
                : _sourceExecutablePath;
        _sourceSigningCertificateRawData = TryCaptureSigningCertificate(signingAnchorPath);
        StartupDirectory = Path.GetFullPath(localStartupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Y-TEC",
            "StickyNote"));
        CacheRootDirectory = Path.Combine(StartupDirectory, "app");
        LegacyHelperPath = Path.Combine(StartupDirectory, LegacyHelperFileName);
        LegacyConfigFilePath = Path.Combine(StartupDirectory, LegacyConfigFileName);
        RegistrationFilePath = Path.Combine(StartupDirectory, RegistrationFileName);
    }

    public string SourceApplicationDirectory => _sourceApplicationDirectory;

    public string StartupDirectory { get; }

    public string CacheRootDirectory { get; }

    public string LegacyHelperPath { get; }

    public string LegacyConfigFilePath { get; }

    public string RegistrationFilePath { get; }

    public StartupRegistrationStatus GetRegistrationStatus()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var registered = key?.GetValue(_valueName) as string;
        if (string.IsNullOrWhiteSpace(registered))
        {
            return StartupRegistrationStatus.Disabled;
        }

        if (IsLegacyHelperCommand(registered))
        {
            return StartupRegistrationStatus.NeedsSecurityUpgrade;
        }

        try
        {
            var registration = ReadRegistration();
            if (registration.Format != 2 ||
                !string.Equals(
                    Path.GetFullPath(registration.SourceApplicationDirectory),
                    _sourceApplicationDirectory,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsDirectCacheChild(registration.CacheDirectory))
            {
                return StartupRegistrationStatus.NeedsSecurityUpgrade;
            }

            var cacheDirectory = Path.GetFullPath(registration.CacheDirectory);
            var cachedExecutable = Path.Combine(cacheDirectory, Path.GetFileName(_sourceExecutablePath));
            var expectedCommand = BuildCommand(cachedExecutable, _sourceApplicationDirectory);
            if (!string.Equals(registered.Trim(), expectedCommand, StringComparison.OrdinalIgnoreCase))
            {
                return StartupRegistrationStatus.NeedsSecurityUpgrade;
            }

            if (!File.Exists(_sourceExecutablePath) ||
                !File.Exists(cachedExecutable) ||
                File.Exists(Path.Combine(cacheDirectory, LegacyHelperFileName)) ||
                _sourceSigningCertificateRawData is null)
            {
                return StartupRegistrationStatus.NeedsSecurityUpgrade;
            }

            EnsureCertificateMatchesCapturedSource(_sourceExecutablePath);
            var cachedManifest = StartupRuntimeManifest.VerifyDirectory(
                cacheDirectory,
                _sourceSigningCertificateRawData,
                allowUnrelatedSourceFiles: false);
            if (!string.Equals(
                    Path.GetFileName(cacheDirectory),
                    cachedManifest.ManifestDigest[..24],
                    StringComparison.OrdinalIgnoreCase))
            {
                return StartupRegistrationStatus.NeedsSecurityUpgrade;
            }
            EnsureCertificateMatchesCapturedSource(cachedExecutable);
            return StartupRegistrationStatus.Enabled;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or CryptographicException or InvalidOperationException or JsonException)
        {
            return StartupRegistrationStatus.NeedsSecurityUpgrade;
        }
    }

    public bool IsEnabledForCurrentExecutable() =>
        GetRegistrationStatus() == StartupRegistrationStatus.Enabled;

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            RemoveRegistration();
            return;
        }

        if (IsEnabledForCurrentExecutable())
        {
            return;
        }

        if (!File.Exists(_sourceExecutablePath))
        {
            throw new FileNotFoundException("自動起動へ登録する罫彩本体が見つかりません。", _sourceExecutablePath);
        }

        if (_sourceSigningCertificateRawData is null)
        {
            throw new InvalidOperationException("罫彩本体の署名証明書を起動時に確認できませんでした。");
        }

        EnsureCertificateMatchesCapturedSource(_sourceExecutablePath);
        var verifiedManifest = StartupRuntimeManifest.VerifyDirectory(
            _sourceApplicationDirectory,
            _sourceSigningCertificateRawData,
            allowUnrelatedSourceFiles: true);
        var cachedExecutable = PrepareLocalCache(verifiedManifest);
        WriteRegistration(cachedExecutable);

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
            ?? throw new InvalidOperationException("Windowsの自動起動設定を開けません。");
        var command = BuildCommand(cachedExecutable, _sourceApplicationDirectory);
        if (!string.Equals(key.GetValue(_valueName) as string, command, StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue(_valueName, command, RegistryValueKind.String);
        }

        RemoveLegacyFiles();
    }

    public static IReadOnlyList<string> GetCacheableFileNamesForTests(string sourceDirectory) =>
        GetCacheableFiles(Path.GetFullPath(sourceDirectory))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToArray();

    public static string BuildCommandForTests(string cachedExecutable, string sourceDirectory) =>
        BuildCommand(Path.GetFullPath(cachedExecutable), Path.GetFullPath(sourceDirectory));

    private string PrepareLocalCache(VerifiedStartupRuntimeManifest verifiedManifest)
    {
        Directory.CreateDirectory(CacheRootDirectory);
        var cacheDirectory = Path.Combine(CacheRootDirectory, verifiedManifest.ManifestDigest[..24]);
        var cachedExecutable = Path.Combine(cacheDirectory, Path.GetFileName(_sourceExecutablePath));
        if (TryVerifyCacheDirectory(cacheDirectory))
        {
            EnsureCertificateMatchesCapturedSource(cachedExecutable);
            return cachedExecutable;
        }

        var stagingDirectory = Path.Combine(CacheRootDirectory, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            foreach (var fileName in verifiedManifest.RuntimeFileNames
                         .Append(StartupRuntimeManifest.ManifestFileName)
                         .Append(StartupRuntimeManifest.SignatureFileName))
            {
                File.Copy(
                    Path.Combine(_sourceApplicationDirectory, fileName),
                    Path.Combine(stagingDirectory, fileName),
                    overwrite: false);
            }

            var stagedManifest = StartupRuntimeManifest.VerifyDirectory(
                stagingDirectory,
                _sourceSigningCertificateRawData!,
                allowUnrelatedSourceFiles: false);
            if (!string.Equals(
                    stagedManifest.ManifestDigest,
                    verifiedManifest.ManifestDigest,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("コピー中に自動起動マニフェストが変更されました。");
            }
            var stagedExecutable = Path.Combine(stagingDirectory, Path.GetFileName(_sourceExecutablePath));
            EnsureCertificateMatchesCapturedSource(stagedExecutable);

            if (Directory.Exists(cacheDirectory))
            {
                var replacedDirectory = Path.Combine(CacheRootDirectory, $".replaced-{Guid.NewGuid():N}");
                Directory.Move(cacheDirectory, replacedDirectory);
                try
                {
                    Directory.Move(stagingDirectory, cacheDirectory);
                }
                catch
                {
                    Directory.Move(replacedDirectory, cacheDirectory);
                    throw;
                }
                TryDeleteCacheDirectory(replacedDirectory);
            }
            else
            {
                Directory.Move(stagingDirectory, cacheDirectory);
            }

            var cachedManifest = StartupRuntimeManifest.VerifyDirectory(
                cacheDirectory,
                _sourceSigningCertificateRawData!,
                allowUnrelatedSourceFiles: false);
            if (!string.Equals(
                    cachedManifest.ManifestDigest,
                    verifiedManifest.ManifestDigest,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("配置後の自動起動マニフェストが一致しません。");
            }
            EnsureCertificateMatchesCapturedSource(cachedExecutable);
            return cachedExecutable;
        }
        finally
        {
            TryDeleteCacheDirectory(stagingDirectory);
        }
    }

    private void RemoveRegistration()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
        {
            key?.DeleteValue(_valueName, false);
        }

        RemoveLegacyFiles();
        TryDeleteFile(RegistrationFilePath);
    }

    private void RemoveLegacyFiles()
    {
        TryDeleteFile(LegacyConfigFilePath);
        TryDeleteFile(LegacyConfigFilePath + ".tmp");
        TryDeleteFile(LegacyHelperPath);
    }

    private void WriteRegistration(string cachedExecutable)
    {
        Directory.CreateDirectory(StartupDirectory);
        var registration = new StartupRegistration(
            2,
            _sourceApplicationDirectory,
            Path.GetDirectoryName(cachedExecutable)
                ?? throw new InvalidOperationException("ローカル自動起動用フォルダーを取得できません。"));
        var temporaryPath = RegistrationFilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(registration));
        File.Move(temporaryPath, RegistrationFilePath, overwrite: true);
    }

    private StartupRegistration ReadRegistration()
    {
        if (!File.Exists(RegistrationFilePath))
        {
            throw new FileNotFoundException("ローカル自動起動設定が見つかりません。", RegistrationFilePath);
        }

        return JsonSerializer.Deserialize<StartupRegistration>(File.ReadAllText(RegistrationFilePath))
            ?? throw new InvalidDataException("ローカル自動起動設定を読み取れません。");
    }

    private bool IsDirectCacheChild(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        return parent is not null &&
            string.Equals(Path.GetFullPath(parent), Path.GetFullPath(CacheRootDirectory), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetCacheableFiles(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"罫彩の実行フォルダーが見つかりません: {sourceDirectory}");
        }

        return Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => CacheableExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !string.Equals(Path.GetFileName(path), StartupRuntimeManifest.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(Path.GetFileName(path), LegacyHelperFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveSourceExecutablePath(string sourceDirectory)
    {
        var preferred = Path.Combine(sourceDirectory, PreferredExecutableName);
        if (File.Exists(preferred))
        {
            return preferred;
        }

        var fallback = Path.Combine(sourceDirectory, FallbackExecutableName);
        return File.Exists(fallback) ? fallback : preferred;
    }

    private bool TryVerifyCacheDirectory(string cacheDirectory)
    {
        if (!Directory.Exists(cacheDirectory) || _sourceSigningCertificateRawData is null)
        {
            return false;
        }

        try
        {
            var manifest = StartupRuntimeManifest.VerifyDirectory(
                cacheDirectory,
                _sourceSigningCertificateRawData,
                allowUnrelatedSourceFiles: false);
            return string.Equals(
                Path.GetFileName(cacheDirectory),
                manifest.ManifestDigest[..24],
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or CryptographicException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    private static string BuildCommand(string cachedExecutable, string sourceDirectory)
    {
        if (cachedExecutable.Contains('"') || sourceDirectory.Contains('"'))
        {
            throw new ArgumentException("自動起動パスに利用できない文字が含まれています。");
        }

        return $"{QuoteWindowsCommandLineArgument(cachedExecutable)} --startup-data-root {QuoteWindowsCommandLineArgument(sourceDirectory)} --startup-wait-for-data";
    }

    private static string QuoteWindowsCommandLineArgument(string value)
    {
        var trailingBackslashCount = 0;
        for (var index = value.Length - 1; index >= 0 && value[index] == '\\'; index--)
        {
            trailingBackslashCount++;
        }

        return $"\"{value}{new string('\\', trailingBackslashCount)}\"";
    }

    private bool IsLegacyHelperCommand(string command) =>
        command.Contains(LegacyHelperPath, StringComparison.OrdinalIgnoreCase) ||
        command.Contains(LegacyHelperFileName, StringComparison.OrdinalIgnoreCase);

    private void EnsureCertificateMatchesCapturedSource(string executable)
    {
        VerifyAuthenticodeIntegrity(executable, "罫彩本体");
        using var certificate = GetSigningCertificate(executable, "罫彩本体");
        if (_sourceSigningCertificateRawData is null || !HasCodeSigningUsage(certificate) ||
            !CryptographicOperations.FixedTimeEquals(certificate.RawData, _sourceSigningCertificateRawData))
        {
            throw new InvalidOperationException("罫彩本体の署名者が、起動時に確認した署名者と一致しません。");
        }
    }

    private static byte[]? TryCaptureSigningCertificate(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var certificate = GetSigningCertificate(path, "罫彩本体");
            return HasCodeSigningUsage(certificate) ? certificate.RawData.ToArray() : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static X509Certificate2 GetSigningCertificate(string path, string description)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return new X509Certificate2(certificate);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException($"{description}のAuthenticode署名を読み取れません。", ex);
        }
    }

    private static bool HasCodeSigningUsage(X509Certificate2 certificate) =>
        certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(usage => string.Equals(usage.Value, "1.3.6.1.5.5.7.3.3", StringComparison.Ordinal));

    private static void VerifyAuthenticodeIntegrity(string path, string description)
    {
        var verificationResult = NativeAuthenticodeVerifier.Verify(path);
        if (verificationResult is not NativeAuthenticodeVerifier.Success and not NativeAuthenticodeVerifier.UntrustedRoot)
        {
            throw new InvalidOperationException($"{description}のAuthenticode署名を検証できません。Windowsエラー: 0x{verificationResult:x8}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Run登録は先に解除済みなので、隔離済み・使用中の旧ファイルが残っても再実行されない。
        }
    }

    private sealed record StartupRegistration(
        int Format,
        string SourceApplicationDirectory,
        string CacheDirectory);

    private void TryDeleteCacheDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var cachePrefix = Path.GetFullPath(CacheRootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(path);
        if (!target.StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"自動起動キャッシュ外のフォルダー削除を拒否しました: {target}");
        }

        try
        {
            Directory.Delete(target, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 旧版が実行中なら後から自然に置き換えられるため、キャッシュは安全側で残す。
        }
    }

    private static class NativeAuthenticodeVerifier
    {
        public const uint Success = 0;
        public const uint UntrustedRoot = 0x800B0109;
        private const uint WtdUiNone = 2;
        private const uint WtdRevokeNone = 0;
        private const uint WtdChoiceFile = 1;
        private const uint WtdRevocationCheckNone = 0x00000010;
        private static readonly Guid VerifyV2Action = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        public static uint Verify(string filePath)
        {
            var fileInfo = new WinTrustFileInfo(filePath);
            var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);
                var trustData = new WinTrustData(fileInfoPointer);
                return WinVerifyTrust(IntPtr.Zero, VerifyV2Action, ref trustData);
            }
            finally
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
                Marshal.FreeHGlobal(fileInfoPointer);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [In] Guid pgActionId, ref WinTrustData trustData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;

            public WinTrustFileInfo(string filePath)
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
                FilePath = filePath;
                FileHandle = IntPtr.Zero;
                KnownSubject = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;

            public WinTrustData(IntPtr fileInfo)
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>();
                PolicyCallbackData = IntPtr.Zero;
                SipClientData = IntPtr.Zero;
                UiChoice = WtdUiNone;
                RevocationChecks = WtdRevokeNone;
                UnionChoice = WtdChoiceFile;
                FileInfo = fileInfo;
                StateAction = 0;
                StateData = IntPtr.Zero;
                UrlReference = IntPtr.Zero;
                ProviderFlags = WtdRevocationCheckNone;
                UiContext = 0;
                SignatureSettings = IntPtr.Zero;
            }
        }
    }
}
