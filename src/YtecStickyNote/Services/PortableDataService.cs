using System.IO;
using System.Text;
using System.Text.Json;
using YtecStickyNote.Models;

namespace YtecStickyNote.Services;

public sealed class PortableDataService
{
    public const int MaximumStateFileBytes = 64 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

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
            var fileBytes = ReadStateFileBytes();
            using var memory = new MemoryStream(fileBytes, writable: false);
            using var reader = new StreamReader(memory, StrictUtf8, detectEncodingFromByteOrderMarks: true);
            var json = reader.ReadToEnd();
            using (var document = JsonDocument.Parse(json))
            {
                ValidateStateProperties(document.RootElement);
            }
            var state = JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
            if (state.Version > AppState.CurrentVersion)
            {
                return new LoadResult(new AppState(), true, false, "保存データがこのアプリより新しい形式です。安全のため編集と保存を停止しました。元データは変更していません。");
            }

            state.Window ??= new WindowStateData();
            state.ThemeId = NoteTheme.Find(state.ThemeId).Id;
            return new LoadResult(state, true, true, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
        {
            return new LoadResult(new AppState(), true, false, $"保存データを読み込めませんでした。安全のため編集と保存を停止しました。元データは変更していません。{ex.Message}");
        }
    }

    public void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.NormalizePages();
        state.ValidateResourceLimits();
        var previousVersion = state.Version;
        var previousLastSavedAt = state.LastSavedAt;
        var savedAt = DateTimeOffset.Now;
        byte[] jsonBytes;
        try
        {
            state.Version = AppState.CurrentVersion;
            state.LastSavedAt = savedAt;
            jsonBytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        }
        finally
        {
            state.Version = previousVersion;
            state.LastSavedAt = previousLastSavedAt;
        }
        if (jsonBytes.Length > MaximumStateFileBytes)
        {
            throw new InvalidDataException("保存データが大きすぎるため保存できません。");
        }

        Directory.CreateDirectory(DataDirectory);
        if (File.Exists(StateFilePath) && previousVersion < AppState.CurrentVersion)
        {
            var migrationBackupPath = $"{StateFilePath}.v{Math.Max(0, previousVersion)}.bak";
            if (!File.Exists(migrationBackupPath))
            {
                File.Copy(StateFilePath, migrationBackupPath, overwrite: false);
            }
        }

        var tempFile = StateFilePath + ".tmp";
        File.WriteAllBytes(tempFile, jsonBytes);

        try
        {
            if (File.Exists(StateFilePath))
            {
                File.Copy(StateFilePath, BackupFilePath, true);
            }

            File.Move(tempFile, StateFilePath, true);
            state.Version = AppState.CurrentVersion;
            state.LastSavedAt = savedAt;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private byte[] ReadStateFileBytes()
    {
        using var stream = new FileStream(StateFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumStateFileBytes)
        {
            throw new InvalidDataException("保存データが大きすぎるため読み込めません。");
        }

        var bytes = new byte[(int)stream.Length];
        var total = 0;
        while (total < bytes.Length)
        {
            var read = stream.Read(bytes, total, bytes.Length - total);
            if (read == 0)
            {
                Array.Resize(ref bytes, total);
                return bytes;
            }
            total += read;
        }

        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("保存データが読込中に大きすぎる状態へ変化しました。");
        }
        return bytes;
    }

    private static void ValidateStateProperties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var pagesPropertyCount = 0;
        var versionPropertyCount = 0;
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "Version", StringComparison.OrdinalIgnoreCase))
            {
                if (++versionPropertyCount > 1)
                {
                    throw new JsonException("Versionプロパティが重複しています。");
                }

                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out _))
                {
                    throw new JsonException("Versionプロパティは32ビット整数である必要があります。");
                }

                continue;
            }

            if (!string.Equals(property.Name, "Pages", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (++pagesPropertyCount > 1)
            {
                throw new JsonException("Pagesプロパティが重複しています。");
            }

            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Pagesプロパティは配列である必要があります。");
            }

            if (property.Value.GetArrayLength() > AppState.MaximumPageCount)
            {
                throw new JsonException($"ページ数が上限（{AppState.MaximumPageCount}ページ）を超えています。");
            }
        }
    }
}

public sealed record LoadResult(AppState State, bool FileExisted, bool CanSave, string? Warning);
