using System.Globalization;
using System.IO;
using System.Text.Json;
using YtecStickyNote.Models;

namespace YtecStickyNote.Services;

public sealed class WindowProfileService
{
    public const int MaximumProfiles = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WindowProfileService(string? baseDirectory = null)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        DataDirectory = Path.Combine(BaseDirectory, "data");
        StateFilePath = Path.Combine(DataDirectory, "window-state.json");
        BackupFilePath = Path.Combine(DataDirectory, "window-state.backup.json");
    }

    public string BaseDirectory { get; }

    public string DataDirectory { get; }

    public string StateFilePath { get; }

    public string BackupFilePath { get; }

    public WindowProfileLoadResult LoadOrMigrate(string layoutId, WindowStateData legacyPlacement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutId);
        ArgumentNullException.ThrowIfNull(legacyPlacement);

        if (!File.Exists(StateFilePath))
        {
            var migrated = new WindowProfileState
            {
                Profiles = [WindowProfile.From(layoutId, legacyPlacement)]
            };
            try
            {
                WriteState(migrated);
                return new WindowProfileLoadResult(
                    migrated.Profiles[0].ToWindowStateData(),
                    false,
                    true,
                    true,
                    null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new WindowProfileLoadResult(
                    legacyPlacement,
                    false,
                    false,
                    false,
                    $"PC別のウィンドウ位置ファイルを作成できません。{ex.Message}");
            }
        }

        try
        {
            var state = ReadStateWithRecovery();
            if (state.Version != WindowProfileState.CurrentVersion)
            {
                return new WindowProfileLoadResult(
                    null,
                    true,
                    false,
                    false,
                    $"未対応のウィンドウ位置データです（version: {state.Version}）。元ファイルは変更していません。");
            }

            state.Profiles ??= [];
            var profile = state.Profiles.LastOrDefault(
                candidate => string.Equals(candidate.LayoutId, layoutId, StringComparison.Ordinal));
            return new WindowProfileLoadResult(
                profile?.ToWindowStateData(),
                true,
                true,
                profile is null,
                null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return new WindowProfileLoadResult(
                null,
                true,
                false,
                false,
                $"ウィンドウ位置を読み込めません。位置データは変更していません。{ex.Message}");
        }
    }

    public void Save(string layoutId, WindowStateData placement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutId);
        ArgumentNullException.ThrowIfNull(placement);

        var state = File.Exists(StateFilePath)
            ? ReadStateWithRecovery()
            : new WindowProfileState();
        if (state.Version != WindowProfileState.CurrentVersion)
        {
            throw new InvalidDataException($"未対応のウィンドウ位置データです（version: {state.Version}）。");
        }

        state.Profiles ??= [];
        state.Profiles.RemoveAll(
            profile => string.Equals(profile.LayoutId, layoutId, StringComparison.Ordinal));
        state.Profiles.Add(WindowProfile.From(layoutId, placement));
        if (state.Profiles.Count > MaximumProfiles)
        {
            state.Profiles.RemoveRange(0, state.Profiles.Count - MaximumProfiles);
        }

        WriteState(state);
    }

    private WindowProfileState ReadStateWithRecovery()
    {
        try
        {
            return ReadState(StateFilePath);
        }
        catch (Exception primaryException) when (
            primaryException is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            WindowProfileState recovered;
            try
            {
                recovered = ReadState(BackupFilePath);
            }
            catch (Exception recoveryException) when (
                recoveryException is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                throw primaryException;
            }

            var timestamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            var corruptPath = Path.Combine(DataDirectory, $"window-state.corrupt-{timestamp}.json");
            File.Copy(StateFilePath, corruptPath, overwrite: false);
            File.Copy(BackupFilePath, StateFilePath, overwrite: true);
            return recovered;
        }
    }

    private static WindowProfileState ReadState(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("ウィンドウ位置ファイルがありません。", path);
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WindowProfileState>(json, JsonOptions)
            ?? throw new InvalidDataException("ウィンドウ位置ファイルが空です。");
    }

    private void WriteState(WindowProfileState state)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        var tempFilePath = StateFilePath + ".tmp";
        using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        try
        {
            if (File.Exists(StateFilePath))
            {
                File.Copy(StateFilePath, BackupFilePath, overwrite: true);
            }

            File.Move(tempFilePath, StateFilePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(BackupFilePath))
            {
                File.Copy(BackupFilePath, StateFilePath, overwrite: true);
            }

            throw;
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }
}

public sealed record WindowProfileLoadResult(
    WindowStateData? Placement,
    bool FileExisted,
    bool CanSave,
    bool NeedsSave,
    string? Warning);
