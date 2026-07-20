using System.IO;
using Microsoft.Win32;

namespace YtecStickyNote.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _valueName;
    private readonly string _executablePath;

    public StartupService(string valueName = "Y-TEC Sticky Note", string? executablePath = null)
    {
        _valueName = valueName;
        _executablePath = Path.GetFullPath(executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("実行ファイルの場所を取得できません。"));
    }

    public string ExecutablePath => _executablePath;

    public bool IsEnabledForCurrentExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var registered = key?.GetValue(_valueName) as string;
        return string.Equals(Unquote(registered), _executablePath, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
            ?? throw new InvalidOperationException("Windowsの自動起動設定を開けません。");

        if (enabled)
        {
            if (!File.Exists(_executablePath))
            {
                throw new FileNotFoundException("自動起動へ登録する実行ファイルが見つかりません。", _executablePath);
            }

            key.SetValue(_valueName, $"\"{_executablePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(_valueName, false);
        }
    }

    private static string? Unquote(string? command) => command?.Trim().Trim('"');
}
