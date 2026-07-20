using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using YtecStickyNote.Models;
using YtecStickyNote.Services;

var tests = new (string Name, Action Run)[]
{
    ("5種類の背景が一意である", TestThemes),
    ("保存データを往復できる", TestPortableDataRoundTrip),
    ("旧保存データのバックアップを作る", TestBackup),
    ("壊れた保存データを上書き対象にしない", TestCorruptDataProtection),
    ("画面外の位置を見える範囲へ戻す", TestWindowPlacement),
    ("PC内フォントを列挙してお気に入りを先頭に並べる", TestFontCatalog),
    ("自動起動レジストリを登録・解除できる", TestStartupRegistry)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"[OK] {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.Error.WriteLine($"[NG] {test.Name}\n     {ex}");
    }
}

Console.WriteLine($"\n{tests.Length - failures.Count}/{tests.Length} 件成功");
return failures.Count == 0 ? 0 : 1;

static void TestThemes()
{
    Assert(NoteTheme.All.Count == 5, "背景数が5ではありません。");
    Assert(NoteTheme.All.Select(theme => theme.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 5, "背景IDが重複しています。");
    Assert(NoteTheme.Find("unknown").Id == "lemon", "不明な背景のフォールバックが不正です。");
}

static void TestPortableDataRoundTrip()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        var state = new AppState
        {
            PlainText = "テスト用メモ",
            RichTextRtfBase64 = Convert.ToBase64String("{\\rtf1 テスト}"u8.ToArray()),
            ThemeId = "mint",
            StartWithWindows = false,
            Window = new WindowStateData { Left = 120, Top = 80, Width = 480, Height = 560 }
        };

        service.Save(state);
        var loaded = service.Load();

        Assert(loaded.FileExisted, "保存ファイルを検出できません。");
        Assert(loaded.CanSave, "正常な保存データで保存が停止されています。");
        Assert(loaded.Warning is null, loaded.Warning ?? "読込警告が発生しました。");
        Assert(loaded.State.PlainText == state.PlainText, "本文が一致しません。");
        Assert(loaded.State.ThemeId == "mint", "背景が一致しません。");
        Assert(File.Exists(service.StateFilePath), "ポータブル保存先にファイルがありません。");
    });
}

static void TestCorruptDataProtection()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        File.WriteAllText(service.StateFilePath, "{ broken json");

        var loaded = service.Load();
        Assert(loaded.FileExisted, "壊れた保存ファイルを検出できません。");
        Assert(!loaded.CanSave, "壊れた保存データに対する保存が許可されています。");
        Assert(!string.IsNullOrWhiteSpace(loaded.Warning), "利用者向け警告がありません。");
        Assert(File.ReadAllText(service.StateFilePath) == "{ broken json", "壊れた元データが変更されました。");
    });
}

static void TestBackup()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        service.Save(new AppState { PlainText = "1回目" });
        service.Save(new AppState { PlainText = "2回目" });

        Assert(File.Exists(service.BackupFilePath), "バックアップが作成されていません。");
        var backup = JsonSerializer.Deserialize<AppState>(File.ReadAllText(service.BackupFilePath));
        Assert(backup?.PlainText == "1回目", "バックアップが直前データではありません。");
    });
}

static void TestWindowPlacement()
{
    var state = new WindowStateData { Left = 5000, Top = -800, Width = 520, Height = 620 };
    var desktop = new Rect(0, 0, 1920, 1080);
    var workArea = new Rect(0, 0, 1920, 1040);
    var restored = WindowPlacementService.GetRestoredBounds(state, desktop, workArea);

    Assert(restored.Left <= 1830, "ウィンドウの横位置が画面外です。");
    Assert(restored.Top >= 0 && restored.Top <= 1032, "ウィンドウの縦位置が画面外です。");
    Assert(restored.Width >= 360 && restored.Height >= 400, "最小サイズを下回っています。");
}

static void TestStartupRegistry()
{
    WithTemporaryDirectory(directory =>
    {
        var valueName = $"Y-TEC Sticky Note Test {Guid.NewGuid():N}";
        var sourceHelper = Path.Combine(directory, "source-helper.exe");
        var localStartupDirectory = Path.Combine(directory, "local-startup");
        var targetExecutable = Path.Combine(directory, "portable-app", "test-app.exe");
        var targetDataFile = Path.Combine(directory, "portable-app", "data", "sticky-note.json");
        Directory.CreateDirectory(Path.GetDirectoryName(targetDataFile)!);
        File.WriteAllBytes(sourceHelper, [0x4D, 0x5A, 0x01, 0x02]);
        File.WriteAllBytes(targetExecutable, [0x4D, 0x5A, 0x03, 0x04]);
        File.WriteAllText(targetDataFile, "{}");
        var service = new StartupService(valueName, targetExecutable, sourceHelper, localStartupDirectory);
        try
        {
            service.SetEnabled(true);
            Assert(service.IsEnabledForCurrentExecutable(), "自動起動を確認できません。");
            Assert(File.Exists(service.LocalHelperPath), "待機プログラムがローカルへ配置されていません。");
            var configLines = File.ReadLines(service.ConfigFilePath).ToArray();
            Assert(configLines[0] == Path.GetFullPath(targetExecutable), "起動対象の設定が一致しません。");
            Assert(configLines.Contains($"data={Path.GetFullPath(targetDataFile)}"), "既存の保存データが準備確認の対象になっていません。");
        }
        finally
        {
            service.SetEnabled(false);
        }

        Assert(!service.IsEnabledForCurrentExecutable(), "テスト用自動起動設定が残っています。");
        Assert(!File.Exists(service.ConfigFilePath), "解除後も起動対象の設定が残っています。");
    });
}

static void TestFontCatalog()
{
    var expectedCount = Fonts.SystemFontFamilies
        .Where(font => !string.IsNullOrWhiteSpace(font.Source))
        .Select(font => font.Source)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    var choices = FontCatalog.GetInstalledFonts();

    Assert(choices.Count == expectedCount, "インストール済みフォントの一部が一覧にありません。");
    Assert(choices.Count > 10, "フォント一覧が少なすぎます。");
    Assert(choices.TakeWhile(font => font.IsFavorite).Count() == choices.Count(font => font.IsFavorite),
        "よく使うフォントが一覧の先頭にまとまっていません。");
}

static void WithTemporaryDirectory(Action<string> action)
{
    var directory = Path.Combine(Path.GetTempPath(), $"ytec-sticky-note-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        action(directory);
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
