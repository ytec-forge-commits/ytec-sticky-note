using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using YtecStickyNote.Models;
using YtecStickyNote.Services;

var tests = new (string Name, Action Run)[]
{
    ("10種類の背景が一意である", TestThemes),
    ("保存データを往復できる", TestPortableDataRoundTrip),
    ("旧保存データのバックアップを作る", TestBackup),
    ("壊れた保存データを上書き対象にしない", TestCorruptDataProtection),
    ("旧自動起動設定を含む保存データを読み込める", TestLegacyStartupSetting),
    ("旧形式を初回移行時の専用バックアップへ残す", TestMigrationBackup),
    ("モニター構成IDが順序に依存せず変化を識別する", TestMonitorLayoutId),
    ("モニター構成ごとに別のウィンドウ位置を保存する", TestWindowProfiles),
    ("旧共有位置を最初のモニター構成へ移行する", TestWindowProfileMigration),
    ("ウィンドウ位置プロファイルを最大12件に保つ", TestWindowProfileLimit),
    ("壊れた位置データをバックアップから復旧する", TestWindowProfileRecovery),
    ("画面構成が3回安定するまで復元を待つ", TestDisplayTransitionStabilization),
    ("画面切断中の自動移動を保存しない", TestDisplayTransitionBlocksPlacementSave),
    ("外部画面の復帰後に元の位置とサイズを復元する", TestDisplayReconnectRestoresPlacement),
    ("仮想デスクトップ外の位置を見える範囲へ戻す", TestWindowPlacement),
    ("モニター構成変更後の位置を実画面内へ戻す", TestChangedMonitorPlacement),
    ("画面より大きいウィンドウを作業領域内へ収める", TestOversizedWindowPlacement),
    ("PC内フォントを列挙してお気に入りを先頭に並べる", TestFontCatalog)
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
    Assert(NoteTheme.All.Count == 10, "背景数が10ではありません。");
    Assert(NoteTheme.All.Select(theme => theme.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 10, "背景IDが重複しています。");
    Assert(NoteTheme.All.Select(theme => theme.Paper).Distinct().Count() == 10, "背景色が重複しています。");
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
            RichTextXamlPackageBase64 = Convert.ToBase64String("xaml-package-test"u8.ToArray()),
            ThemeId = "mint",
            Window = new WindowStateData { Left = 120, Top = 80, Width = 480, Height = 560 }
        };

        service.Save(state);
        var loaded = service.Load();

        Assert(loaded.FileExisted, "保存ファイルを検出できません。");
        Assert(loaded.CanSave, "正常な保存データで保存が停止されています。");
        Assert(loaded.Warning is null, loaded.Warning ?? "読込警告が発生しました。");
        Assert(loaded.State.PlainText == state.PlainText, "本文が一致しません。");
        Assert(loaded.State.Version == AppState.CurrentVersion, "保存形式の版番号が更新されていません。");
        Assert(loaded.State.RichTextXamlPackageBase64 == state.RichTextXamlPackageBase64, "XAMLパッケージが一致しません。");
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

static void TestLegacyStartupSetting()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        File.WriteAllText(service.StateFilePath,
            """
            {
              "Version": 1,
              "PlainText": "旧版データ",
              "ThemeId": "lemon",
              "StartWithWindows": true,
              "Window": { "Width": 520, "Height": 620 }
            }
            """);

        var loaded = service.Load();
        Assert(loaded.CanSave, loaded.Warning ?? "旧版データを読み込めません。");
        Assert(loaded.State.PlainText == "旧版データ", "旧版の本文が失われました。");
    });
}

static void TestMigrationBackup()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        File.WriteAllText(service.StateFilePath,
            """
            {
              "Version": 1,
              "PlainText": "移行前データ",
              "ThemeId": "sky",
              "Window": { "Width": 520, "Height": 620 }
            }
            """);

        var loaded = service.Load();
        service.Save(loaded.State);
        service.Save(loaded.State);

        var migrationBackupPath = service.StateFilePath + ".v1.bak";
        Assert(File.Exists(migrationBackupPath), "v1専用バックアップがありません。");
        var backup = JsonSerializer.Deserialize<AppState>(File.ReadAllText(migrationBackupPath));
        Assert(backup?.Version == 1, "移行バックアップが旧形式ではありません。");
        Assert(backup?.PlainText == "移行前データ", "移行バックアップの本文が一致しません。");
    });
}

static void TestMonitorLayoutId()
{
    var primary = new MonitorGeometry("DISPLAY1", 0, 0, 1920, 1080, 0, 0, 1920, 1040, 1000);
    var secondary = new MonitorGeometry("DISPLAY2", 1920, 0, 2560, 1440, 1920, 0, 2560, 1400, 1250);
    var home = MonitorLayoutService.CreateLayoutId([primary, secondary]);
    var reversed = MonitorLayoutService.CreateLayoutId([secondary, primary]);
    var work = MonitorLayoutService.CreateLayoutId(
        [primary, secondary with { Width = 1920, Height = 1080, WorkWidth = 1920, WorkHeight = 1040, ScaleMilli = 1000 }]);

    Assert(home == reversed, "モニター列挙順によって構成IDが変化しました。");
    Assert(home.StartsWith("layout-2-", StringComparison.Ordinal), "モニター数が構成IDへ反映されていません。");
    Assert(home != work, "解像度・作業領域・拡大率の違いを識別できません。");
}

static void TestWindowProfiles()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new WindowProfileService(directory);
        var home = new WindowStateData { Left = 120, Top = 80, Width = 520, Height = 620 };
        var work = new WindowStateData { Left = 2100, Top = 140, Width = 480, Height = 560 };

        service.Save("layout-home", home);
        service.Save("layout-work", work);

        var loadedHome = service.LoadOrMigrate("layout-home", new WindowStateData());
        var loadedWork = service.LoadOrMigrate("layout-work", new WindowStateData());

        Assert(loadedHome.Placement?.Left == 120, "自宅用の位置を復元できません。");
        Assert(loadedHome.Placement?.Width == 520, "自宅用のサイズを復元できません。");
        Assert(loadedWork.Placement?.Left == 2100, "職場用の位置を復元できません。");
        Assert(loadedWork.Placement?.Width == 480, "職場用のサイズを復元できません。");
    });
}

static void TestWindowProfileMigration()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new WindowProfileService(directory);
        var legacy = new WindowStateData { Left = 840, Top = 90, Width = 500, Height = 600 };

        var migrated = service.LoadOrMigrate("layout-current", legacy);
        var unknown = service.LoadOrMigrate("layout-other", legacy);

        Assert(migrated.CanSave, migrated.Warning ?? "旧共有位置を移行できません。");
        Assert(migrated.Placement?.Left == legacy.Left, "移行後の位置が旧共有位置と一致しません。");
        Assert(migrated.NeedsSave, "画面内補正後の再保存が要求されていません。");
        Assert(File.Exists(service.StateFilePath), "PC別の位置ファイルが作成されていません。");
        Assert(unknown.Placement is null, "未知のモニター構成へ旧共有位置が流用されています。");
        Assert(unknown.NeedsSave, "未知のモニター構成が新規保存対象になっていません。");
    });
}

static void TestWindowProfileLimit()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new WindowProfileService(directory);
        for (var index = 0; index < WindowProfileService.MaximumProfiles + 3; index++)
        {
            service.Save(
                $"layout-{index}",
                new WindowStateData { Left = index * 10, Top = index * 5, Width = 520, Height = 620 });
        }

        var state = JsonSerializer.Deserialize<WindowProfileState>(
            File.ReadAllText(service.StateFilePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var profiles = state?.Profiles ?? throw new InvalidOperationException("位置プロファイルを読み込めません。");
        Assert(profiles.Count == WindowProfileService.MaximumProfiles, "位置プロファイル数が上限を超えています。");
        Assert(profiles.All(profile => profile.LayoutId is not "layout-0" and not "layout-1" and not "layout-2"),
            "古い位置プロファイルが上限超過時に整理されていません。");
    });
}

static void TestWindowProfileRecovery()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new WindowProfileService(directory);
        service.Save("layout-home", new WindowStateData { Left = 100, Top = 80, Width = 520, Height = 620 });
        service.Save("layout-home", new WindowStateData { Left = 200, Top = 160, Width = 520, Height = 620 });
        File.WriteAllText(service.StateFilePath, "{ broken json");

        var recovered = service.LoadOrMigrate("layout-home", new WindowStateData());

        Assert(recovered.CanSave, recovered.Warning ?? "バックアップから復旧できません。");
        Assert(recovered.Placement?.Left == 100, "直前バックアップの位置へ復旧していません。");
        Assert(Directory.GetFiles(service.DataDirectory, "window-state.corrupt-*.json").Length == 1,
            "壊れた位置ファイルが退避されていません。");
        Assert(JsonSerializer.Deserialize<WindowProfileState>(
            File.ReadAllText(service.StateFilePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) is not null,
            "復旧後の位置ファイルを読み込めません。");
    });
}

static void TestDisplayTransitionStabilization()
{
    var session = new WindowLayoutSession(requiredStableSamples: 3);
    session.Initialize("layout-home", placementDirty: false);
    session.BeginDisplayTransition();

    Assert(session.ObserveDisplayLayout("layout-single") is null, "1回目の観測で構成が確定しました。");
    Assert(session.ObserveDisplayLayout("layout-home") is null, "構成の揺れをまたいで観測回数が加算されました。");
    Assert(session.ObserveDisplayLayout("layout-home") is null, "2回目の観測で構成が確定しました。");
    Assert(session.ObserveDisplayLayout("layout-home") == "layout-home", "安定した構成を確定できません。");
}

static void TestDisplayTransitionBlocksPlacementSave()
{
    var session = new WindowLayoutSession(requiredStableSamples: 3);
    session.Initialize("layout-home", placementDirty: false);
    session.MarkUserPlacementChanged();
    Assert(session.CanSavePlacement("layout-home"), "利用者が変更した位置を保存できません。");

    session.BeginDisplayTransition();
    session.MarkUserPlacementChanged();
    Assert(!session.CanSavePlacement("layout-home"), "画面切断中の位置保存が許可されています。");
    Assert(!session.CanSavePlacement("layout-single"), "一時的な画面構成へ位置を保存できます。");
}

static void TestDisplayReconnectRestoresPlacement()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new WindowProfileService(directory);
        var home = new WindowStateData { Left = 2120, Top = 120, Width = 520, Height = 620 };
        var temporarySingleDisplay = new WindowStateData { Left = 10, Top = 10, Width = 360, Height = 400 };
        service.Save("layout-home", home);

        var session = new WindowLayoutSession(requiredStableSamples: 3);
        session.Initialize("layout-home", placementDirty: false);
        session.BeginDisplayTransition();
        session.ObserveDisplayLayout("layout-single");
        session.ObserveDisplayLayout("layout-single");
        var singleLayout = session.ObserveDisplayLayout("layout-single");
        Assert(singleLayout == "layout-single", "切断後の安定した構成を確定できません。");

        session.ApplyStableLayout(singleLayout!, placementDirty: true);
        service.Save(singleLayout!, temporarySingleDisplay);
        session.MarkPlacementSaved();

        session.BeginDisplayTransition();
        session.ObserveDisplayLayout("layout-home");
        session.ObserveDisplayLayout("layout-home");
        var restoredLayout = session.ObserveDisplayLayout("layout-home");
        var restored = service.LoadOrMigrate(restoredLayout!, new WindowStateData()).Placement;

        Assert(restored?.Left == home.Left && restored.Top == home.Top, "外部画面上の元の位置が失われました。");
        Assert(restored?.Width == home.Width && restored.Height == home.Height, "外部画面上の元のサイズが失われました。");
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

static void TestChangedMonitorPlacement()
{
    var currentWorkArea = new Rect(1920, 0, 1920, 1040);
    var savedOnRemovedMonitor = new Rect(3750, 1240, 520, 620);
    var restored = WindowPlacementService.ConstrainToWorkArea(savedOnRemovedMonitor, currentWorkArea);

    Assert(restored.Left >= currentWorkArea.Left, "左端が実在モニターの外です。");
    Assert(restored.Top >= currentWorkArea.Top, "上端が実在モニターの外です。");
    Assert(restored.Right <= currentWorkArea.Right, "右端が実在モニターの外です。");
    Assert(restored.Bottom <= currentWorkArea.Bottom, "下端が実在モニターの外です。");
    Assert(restored.Width == savedOnRemovedMonitor.Width, "収まるサイズが不要に変更されました。");
    Assert(restored.Height == savedOnRemovedMonitor.Height, "収まるサイズが不要に変更されました。");
}

static void TestOversizedWindowPlacement()
{
    var currentWorkArea = new Rect(-1600, 40, 1600, 860);
    var oversized = new Rect(-2400, -400, 2200, 1200);
    var restored = WindowPlacementService.ConstrainToWorkArea(oversized, currentWorkArea);

    Assert(restored == currentWorkArea, "画面より大きいウィンドウが作業領域全体へ収まっていません。");
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
