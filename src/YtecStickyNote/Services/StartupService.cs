using System.IO;
using Microsoft.Win32;

namespace YtecStickyNote.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string HelperFileName = "YTEC-Sticky-Note-Startup.exe";
    private const string ConfigFileName = "startup-target.txt";
    private readonly string _valueName;
    private readonly string _executablePath;
    private readonly string _helperSourcePath;

    public StartupService(
        string valueName = "Y-TEC Sticky Note",
        string? executablePath = null,
        string? helperSourcePath = null,
        string? localStartupDirectory = null)
    {
        _valueName = valueName;
        _executablePath = Path.GetFullPath(executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("実行ファイルの場所を取得できません。"));
        _helperSourcePath = Path.GetFullPath(helperSourcePath ?? Path.Combine(AppContext.BaseDirectory, HelperFileName));

        var startupDirectory = Path.GetFullPath(localStartupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Y-TEC",
            "StickyNote"));
        LocalHelperPath = Path.Combine(startupDirectory, HelperFileName);
        ConfigFilePath = Path.Combine(startupDirectory, ConfigFileName);
    }

    public string ExecutablePath => _executablePath;

    public string LocalHelperPath { get; }

    public string ConfigFilePath { get; }

    public bool IsEnabledForCurrentExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var registered = key?.GetValue(_valueName) as string;
        if (!string.Equals(registered?.Trim(), BuildCommand(), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(LocalHelperPath) || !File.Exists(ConfigFilePath))
        {
            return false;
        }

        try
        {
            var configuredTarget = File.ReadLines(ConfigFilePath).FirstOrDefault()?.Trim().TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(configuredTarget))
            {
                return false;
            }

            return string.Equals(Path.GetFullPath(configuredTarget), _executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

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

        if (!File.Exists(_executablePath))
        {
            throw new FileNotFoundException("自動起動へ登録する実行ファイルが見つかりません。", _executablePath);
        }

        if (!File.Exists(_helperSourcePath))
        {
            throw new FileNotFoundException("自動起動の待機プログラムが見つかりません。", _helperSourcePath);
        }

        var directory = Path.GetDirectoryName(LocalHelperPath)
            ?? throw new InvalidOperationException("自動起動用フォルダーを取得できません。");
        Directory.CreateDirectory(directory);
        CopyHelperWithRetry();
        WriteConfigIfChanged();

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
            ?? throw new InvalidOperationException("Windowsの自動起動設定を開けません。");
        var command = BuildCommand();
        if (!string.Equals(key.GetValue(_valueName) as string, command, StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue(_valueName, command, RegistryValueKind.String);
        }
    }

    private void RemoveRegistration()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
        {
            key?.DeleteValue(_valueName, false);
        }

        TryDelete(ConfigFilePath);
        TryDelete(ConfigFilePath + ".tmp");
        TryDelete(LocalHelperPath);
        TryDeleteDirectoryIfEmpty(Path.GetDirectoryName(LocalHelperPath));
    }

    private string BuildCommand() => $"\"{LocalHelperPath}\" --config \"{ConfigFilePath}\"";

    private string BuildConfigContents()
    {
        var contents = _executablePath;
        var dataFilePath = Path.Combine(Path.GetDirectoryName(_executablePath)!, "data", "sticky-note.json");
        if (File.Exists(dataFilePath))
        {
            contents += $"{Environment.NewLine}data={dataFilePath}";
        }

        return contents;
    }

    private void CopyHelperWithRetry()
    {
        if (FilesMatch(_helperSourcePath, LocalHelperPath))
        {
            return;
        }

        IOException? lastError = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                File.Copy(_helperSourcePath, LocalHelperPath, overwrite: true);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                Thread.Sleep(100);
            }
        }

        throw lastError ?? new IOException("自動起動の待機プログラムをコピーできません。");
    }

    private void WriteConfigIfChanged()
    {
        var contents = BuildConfigContents();
        if (File.Exists(ConfigFilePath) && string.Equals(File.ReadAllText(ConfigFilePath), contents, StringComparison.Ordinal))
        {
            return;
        }

        var temporaryPath = ConfigFilePath + ".tmp";
        File.WriteAllText(temporaryPath, contents);
        File.Move(temporaryPath, ConfigFilePath, overwrite: true);
    }

    private static bool FilesMatch(string sourcePath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            return false;
        }

        var source = new FileInfo(sourcePath);
        var destination = new FileInfo(destinationPath);
        return source.Length == destination.Length && source.LastWriteTimeUtc == destination.LastWriteTimeUtc;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 登録自体は解除済みなので、使用中の旧ファイルが残っても再実行されない。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private static void TryDeleteDirectoryIfEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any())
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: false);
        }
        catch (IOException)
        {
            // 空フォルダーが残っても自動起動登録には影響しない。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }
}
