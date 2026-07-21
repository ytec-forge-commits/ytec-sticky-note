using System.IO;
using System.Text.Json;
using YtecStickyNote.Models;

namespace YtecStickyNote.Services;

public sealed class PortableDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public PortableDataService(string? baseDirectory = null)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        DataDirectory = Path.Combine(BaseDirectory, "data");
        StateFilePath = Path.Combine(DataDirectory, "sticky-note.json");
        BackupFilePath = StateFilePath + ".bak";
    }

    public string BaseDirectory { get; }

    public string DataDirectory { get; }

    public string StateFilePath { get; }

    public string BackupFilePath { get; }

    public LoadResult Load()
    {
        if (!File.Exists(StateFilePath))
        {
            return new LoadResult(new AppState(), false, true, null);
        }

        try
        {
            var json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
            if (state.Version > AppState.CurrentVersion)
            {
                return new LoadResult(new AppState(), true, false, "保存データがこのアプリより新しい形式です。安全のため編集と保存を停止しました。元データは変更していません。");
            }

            state.Window ??= new WindowStateData();
            state.ThemeId = NoteTheme.Find(state.ThemeId).Id;
            return new LoadResult(state, true, true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new LoadResult(new AppState(), true, false, $"保存データを読み込めませんでした。安全のため編集と保存を停止しました。元データは変更していません。{ex.Message}");
        }
    }

    public void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Directory.CreateDirectory(DataDirectory);
        var previousVersion = state.Version;
        if (File.Exists(StateFilePath) && previousVersion < AppState.CurrentVersion)
        {
            var migrationBackupPath = $"{StateFilePath}.v{Math.Max(0, previousVersion)}.bak";
            if (!File.Exists(migrationBackupPath))
            {
                File.Copy(StateFilePath, migrationBackupPath, overwrite: false);
            }
        }

        state.Version = AppState.CurrentVersion;
        state.LastSavedAt = DateTimeOffset.Now;

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var tempFile = StateFilePath + ".tmp";
        File.WriteAllText(tempFile, json);

        try
        {
            if (File.Exists(StateFilePath))
            {
                File.Copy(StateFilePath, BackupFilePath, true);
            }

            File.Move(tempFile, StateFilePath, true);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}

public sealed record LoadResult(AppState State, bool FileExisted, bool CanSave, string? Warning);
