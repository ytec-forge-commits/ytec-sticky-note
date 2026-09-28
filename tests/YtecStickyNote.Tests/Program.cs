using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using YtecStickyNote;
using YtecStickyNote.Models;
using YtecStickyNote.Services;

if (args.Length == 4 && string.Equals(args[0], "--startup-registration-integration", StringComparison.Ordinal))
{
    return RunStartupRegistrationIntegration(args[1], args[2], args[3]);
}

var tests = new (string Name, Action Run)[]
{
    ("10種類の背景が一意である", TestThemes),
    ("保存データを往復できる", TestPortableDataRoundTrip),
    ("旧保存データのバックアップを作る", TestBackup),
    ("壊れた保存データを上書き対象にしない", TestCorruptDataProtection),
    ("旧自動起動設定を含む保存データを読み込める", TestLegacyStartupSetting),
    ("旧形式を初回移行時の専用バックアップへ残す", TestMigrationBackup),
    ("移行バックアップ失敗後も旧版として再試行する", TestMigrationBackupRetryAfterFailure),
    ("v2の本文を最初のページへ安全に移行する", TestV2MigrationToSinglePage),
    ("ページIDと現在ページを正規化して保存できる", TestPageStateNormalization),
    ("壊れたページ配列を保存対象にしない", TestCorruptPageProtection),
    ("過大なページ配列を保存対象にしない", TestOversizedPageCollectionProtection),
    ("重複Pagesプロパティで事前上限検査を迂回できない", TestDuplicatePagesPropertyProtection),
    ("不正なUTF-8を含む保存データを上書き対象にしない", TestInvalidUtf8Protection),
    ("大文字小文字違いの重複Versionを拒否する", TestDuplicateVersionPropertyProtection),
    ("過大な保存ファイルを全量読込前に拒否する", TestOversizedStateFileProtection),
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
    ("PC内フォントを列挙してお気に入りを先頭に並べる", TestFontCatalog),
    ("ポータブル版は実行フォルダー横へ保存する", TestPortableRuntimeProfile),
    ("ローカル自動起動版は元のポータブル保存先を使う", TestStartupCacheRuntimeProfile),
    ("test-modeは実データと分離した保存先を使う", TestModeDataIsolation),
    ("Store版はパッケージLocalStateへ保存する", TestPackagedRuntimeProfile),
    ("自動起動キャッシュへ必要な実行ファイルだけを選ぶ", TestStartupCacheFileSelection),
    ("自動起動コマンドはローカル罫彩本体と元の保存先を固定する", TestStartupCacheCommand),
    ("Google Driveの保存先が安定するまで待ってから開始する", TestStartupDataAvailability),
    ("未署名の罫彩本体を自動起動キャッシュへ登録しない", TestStartupServiceRejectsUnsignedApplication),
    ("保存XAMLパッケージが任意のCLR型を生成しない", TestRestrictiveXamlPackageLoad),
    ("過大展開するXAMLパッケージを拒否する", TestOversizedXamlPackageProtection),
    ("深すぎるXAML本文をオブジェクト生成前に拒否する", TestDeepXamlPackageProtection)
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

static int RunStartupRegistrationIntegration(string valueName, string sourceDirectory, string localStartupDirectory)
{
    var service = new StartupService(valueName, sourceDirectory, localStartupDirectory);
    try
    {
        service.SetEnabled(true);
        Assert(service.GetRegistrationStatus() == StartupRegistrationStatus.Enabled, "署名済み罫彩本体の自動起動登録を確認できません。");
        Assert(File.Exists(service.RegistrationFilePath), "ローカル自動起動設定が作成されていません。");
        Assert(!File.Exists(service.LegacyHelperPath), "旧補助EXEがローカル自動起動先へ残っています。");
        Assert(Directory.EnumerateFiles(service.CacheRootDirectory, "Keisai.exe", SearchOption.AllDirectories).Any(), "ローカル自動起動用の罫彩本体がありません。");

        var cachedDll = Directory.EnumerateFiles(service.CacheRootDirectory, "Keisai.dll", SearchOption.AllDirectories).Single();
        File.Copy(typeof(Program).Assembly.Location, cachedDll, overwrite: true);
        Assert(service.GetRegistrationStatus() == StartupRegistrationStatus.NeedsSecurityUpgrade,
            "登録後に改ざんされたキャッシュDLLを安全性更新が必要な状態として検出できません。");
        File.Copy(Path.Combine(sourceDirectory, "Keisai.dll"), cachedDll, overwrite: true);
        Assert(service.GetRegistrationStatus() == StartupRegistrationStatus.Enabled,
            "正規DLLへ戻した自動起動キャッシュを再確認できません。");

        service.SetEnabled(false);
        Assert(service.GetRegistrationStatus() == StartupRegistrationStatus.Disabled, "検証用Run登録を解除できません。");
        Assert(!File.Exists(service.RegistrationFilePath), "解除後もローカル自動起動設定が残っています。");
        Console.WriteLine(service.CacheRootDirectory);
        return 0;
    }
    finally
    {
        service.SetEnabled(false);
    }
}

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

static void TestMigrationBackupRetryAfterFailure()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        File.WriteAllText(service.StateFilePath,
            """
            {
              "Version": 1,
              "PlainText": "移行再試行データ",
              "ThemeId": "mint",
              "Window": { "Width": 520, "Height": 620 }
            }
            """);

        var loaded = service.Load();
        var migrationBackupPath = service.StateFilePath + ".v1.bak";
        Directory.CreateDirectory(migrationBackupPath);

        AssertThrows<IOException>(() => service.Save(loaded.State), "移行バックアップ作成失敗が保存成功として扱われました。");
        Assert(loaded.State.Version == 1, "失敗した保存が生きている状態の版数を更新しました。");
        Assert(File.ReadAllText(service.StateFilePath).Contains("移行再試行データ", StringComparison.Ordinal), "失敗時に旧保存データが変更されました。");

        Directory.Delete(migrationBackupPath);
        service.Save(loaded.State);

        Assert(File.Exists(migrationBackupPath), "障害解消後の再試行でv1専用バックアップが作成されません。");
        var backup = JsonSerializer.Deserialize<AppState>(File.ReadAllText(migrationBackupPath));
        Assert(backup?.Version == 1, "再試行で作成した移行バックアップが旧形式ではありません。");
    });
}

static void TestStartupCacheFileSelection()
{
    WithTemporaryDirectory(directory =>
    {
        foreach (var fileName in new[]
        {
            "Keisai.exe",
            "YTEC-Sticky-Note.exe",
            "YTEC-Sticky-Note.dll",
            "YTEC-Sticky-Note.deps.json",
            "YTEC-Sticky-Note.runtimeconfig.json",
            "coreclr.dll",
            "icudt.dat",
            "README.txt",
            "YTEC-Sticky-Note-Startup.exe"
        })
        {
            File.WriteAllText(Path.Combine(directory, fileName), fileName);
        }
        Directory.CreateDirectory(Path.Combine(directory, "data"));
        File.WriteAllText(Path.Combine(directory, "data", "sticky-note.json"), "user data");

        var selected = StartupService.GetCacheableFileNamesForTests(directory);

        Assert(selected.Contains("Keisai.exe", StringComparer.OrdinalIgnoreCase), "ローカル起動対象が選択されていません。");
        Assert(selected.Contains("YTEC-Sticky-Note.dll", StringComparer.OrdinalIgnoreCase), "アプリDLLが選択されていません。");
        Assert(selected.Contains("coreclr.dll", StringComparer.OrdinalIgnoreCase), ".NETランタイムが選択されていません。");
        Assert(selected.Contains("icudt.dat", StringComparer.OrdinalIgnoreCase), "自己完結ランタイムのDATが選択されていません。");
        Assert(!selected.Contains("YTEC-Sticky-Note-Startup.exe", StringComparer.OrdinalIgnoreCase), "検知された旧補助EXEがキャッシュ対象に残っています。");
        Assert(!selected.Contains("README.txt", StringComparer.OrdinalIgnoreCase), "利用者向け文書が起動キャッシュへ混入しました。");
        Assert(!selected.Any(name => name.Contains("sticky-note.json", StringComparison.OrdinalIgnoreCase)), "利用者データが起動キャッシュへ混入しました。");
    });
}

static void TestStartupCacheCommand()
{
    var cachedExecutable = Path.GetFullPath(@"C:\Users\Tester\AppData\Local\Y-TEC\StickyNote\app\abc\Keisai.exe");
    var sourceDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(@"G:\マイドライブ\罫彩")) + Path.DirectorySeparatorChar;
    var command = StartupService.BuildCommandForTests(cachedExecutable, sourceDirectory);
    var parsedArguments = WindowsCommandLine.Parse(command);

    Assert(command.StartsWith($"\"{cachedExecutable}\"", StringComparison.Ordinal), "Run登録がローカルの罫彩本体を起動しません。");
    Assert(command.Contains("--startup-wait-for-data", StringComparison.Ordinal), "Google Drive待機モードがRun登録にありません。");
    Assert(!command.Contains("YTEC-Sticky-Note-Startup.exe", StringComparison.OrdinalIgnoreCase), "旧補助EXEがRun登録へ残っています。");
    Assert(parsedArguments.SequenceEqual([
            cachedExecutable,
            "--startup-data-root",
            sourceDirectory,
            "--startup-wait-for-data"
        ], StringComparer.Ordinal),
        $"末尾区切り付き保存先をWindowsが正しく引数分解できません。実際: {string.Join(" | ", parsedArguments)}");
}

static void TestStartupDataAvailability()
{
    WithTemporaryDirectory(directory =>
    {
        var delayedRoot = Path.Combine(directory, "delayed");
        var creator = Task.Run(async () =>
        {
            await Task.Delay(120);
            Directory.CreateDirectory(delayedRoot);
        });

        var becameReady = StartupDataAvailability.WaitUntilReadyAsync(
            delayedRoot,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(25)).GetAwaiter().GetResult();
        creator.GetAwaiter().GetResult();
        Assert(becameReady, "遅れて利用可能になった保存先を検出できません。");
        Assert(Directory.Exists(Path.Combine(delayedRoot, "data")), "保存先のdataフォルダーを準備できません。");
        Assert(!Directory.EnumerateFiles(Path.Combine(delayedRoot, "data"), ".keisai-startup-probe-*", SearchOption.TopDirectoryOnly).Any(), "読み書き確認用ファイルが残っています。");

        var lockedRoot = Path.Combine(directory, "locked");
        var lockedDataDirectory = Path.Combine(lockedRoot, "data");
        Directory.CreateDirectory(lockedDataDirectory);
        var lockedStatePath = Path.Combine(lockedDataDirectory, "sticky-note.json");
        File.WriteAllText(lockedStatePath, "{\"Version\":3,\"Pages\":[]}");
        using (var lockStream = new FileStream(lockedStatePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var lockedReady = StartupDataAvailability.WaitUntilReadyAsync(
                lockedRoot,
                TimeSpan.FromMilliseconds(150),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(25)).GetAwaiter().GetResult();
            Assert(!lockedReady, "既存保存データへ書き込めない状態を準備完了と判定しました。");
        }

        var missingRoot = Path.Combine(directory, "never-ready");
        var timedOut = StartupDataAvailability.WaitUntilReadyAsync(
            missingRoot,
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(25)).GetAwaiter().GetResult();
        Assert(!timedOut, "存在しない保存先の待機がタイムアウトしません。");
    });
}

static void TestStartupServiceRejectsUnsignedApplication()
{
    WithTemporaryDirectory(directory =>
    {
        var executable = Path.Combine(directory, "Keisai.exe");
        var localStartupDirectory = Path.Combine(directory, "local-startup");
        var valueName = $"Y-TEC Sticky Note Test {Guid.NewGuid():N}";
        File.WriteAllText(executable, "not a signed executable");

        var service = new StartupService(valueName, directory, localStartupDirectory);
        try
        {
            AssertThrows<InvalidOperationException>(
                () => service.SetEnabled(true),
                "未署名の罫彩本体が自動起動へ登録されました。");
        }
        finally
        {
            service.SetEnabled(false);
        }
    });
}

static void TestRestrictiveXamlPackageLoad()
{
    XamlActivationProbe.Created = false;
    using var package = new MemoryStream();
    var source = new FlowDocument(new Paragraph(new Run("safe")));
    new TextRange(source.ContentStart, source.ContentEnd).Save(package, DataFormats.XamlPackage);

    package.Position = 0;
    using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
    {
        var xamlEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));
        var entryName = xamlEntry.FullName;
        xamlEntry.Delete();
        using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
        writer.Write(
            """
            <Section xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:test="clr-namespace:;assembly=YtecStickyNote.Tests">
              <Section.Resources>
                <test:XamlActivationProbe x:Key="probe" />
              </Section.Resources>
              <Paragraph>safe</Paragraph>
            </Section>
            """);
    }

    package.Position = 0;
    var destination = new FlowDocument();
    try
    {
        new TextRange(destination.ContentStart, destination.ContentEnd).Load(package, DataFormats.XamlPackage);
    }
    catch (ArgumentException)
    {
        // RestrictiveXamlXmlReaderがパッケージ全体を拒否する結果も安全側として許可する。
    }

    Assert(!XamlActivationProbe.Created, "保存XAMLパッケージから任意のCLR型が生成されました。");
}

static void TestV2MigrationToSinglePage()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        var xaml = Convert.ToBase64String("xaml-v2"u8.ToArray());
        var rtf = Convert.ToBase64String("{\\rtf1 v2}"u8.ToArray());
        File.WriteAllText(service.StateFilePath,
            $$"""
            {
              "Version": 2,
              "PlainText": "v2の本文",
              "RichTextXamlPackageBase64": "{{xaml}}",
              "RichTextRtfBase64": "{{rtf}}",
              "ThemeId": "lavender",
              "Window": { "Width": 520, "Height": 620 }
            }
            """);

        var loaded = service.Load();
        Assert(loaded.CanSave, loaded.Warning ?? "v2保存データを読み込めません。");
        Assert(loaded.State.Version == 2, "移行前に保存形式の版番号が更新されています。");
        Assert(loaded.State.Pages.Count == 1, "v2保存データが1ページへ移行されません。");
        var page = loaded.State.Pages.Single();
        Assert(page.PlainText == "v2の本文", "v2本文が最初のページへ移行されません。");
        Assert(page.RichTextXamlPackageBase64 == xaml, "v2のXAML書式が移行されません。");
        Assert(page.RichTextRtfBase64 == rtf, "v2のRTF書式が移行されません。");
        Assert(page.ThemeId == "lavender", "v2背景が最初のページへ移行されません。");
        Assert(loaded.State.CurrentPageId == page.Id, "移行した最初のページが現在ページになっていません。");

        service.Save(loaded.State);
        Assert(File.Exists(service.StateFilePath + ".v2.bak"), "v2専用バックアップが作成されません。");
    });
}

static void TestPageStateNormalization()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        File.WriteAllText(service.StateFilePath,
            """
            {
              "Version": 3,
              "CurrentPageId": "unknown-page",
              "Pages": [
                { "Id": "duplicate", "PlainText": "一頁", "ThemeId": "mint" },
                { "Id": "duplicate", "PlainText": "二頁", "ThemeId": "sky" }
              ]
            }
            """);

        var loaded = service.Load();
        Assert(loaded.CanSave, loaded.Warning ?? "ページ保存データを読み込めません。");
        Assert(loaded.State.Pages.Count == 2, "ページ保存後にページ数が変化しました。");
        Assert(loaded.State.Pages.Select(page => page.Id).Distinct(StringComparer.Ordinal).Count() == 2,
            "ページIDの重複を解消できません。");
        Assert(loaded.State.CurrentPageId == loaded.State.Pages[0].Id, "不明な現在ページを先頭ページへ正規化できません。");
        Assert(loaded.State.Pages[0].PlainText == "一頁" && loaded.State.Pages[0].ThemeId == "mint", "一頁目の状態を保持できません。");
        Assert(loaded.State.Pages[1].PlainText == "二頁" && loaded.State.Pages[1].ThemeId == "sky", "二頁目の状態を保持できません。");

        service.Save(loaded.State);
        var roundTripped = service.Load();
        Assert(roundTripped.State.CurrentPageId == loaded.State.CurrentPageId, "現在ページIDを保存後に保持できません。");
    });
}

static void TestCorruptPageProtection()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        const string corruptJson = """
            {
              "Version": 3,
              "Pages": [null]
            }
            """;
        File.WriteAllText(service.StateFilePath, corruptJson);

        var loaded = service.Load();
        Assert(!loaded.CanSave, "壊れたページ配列に対する保存が許可されています。");
        Assert(!string.IsNullOrWhiteSpace(loaded.Warning), "壊れたページ配列への警告がありません。");
        Assert(File.ReadAllText(service.StateFilePath) == corruptJson, "壊れたページ保存データが変更されました。");
    });
}

static void TestOversizedPageCollectionProtection()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        var pages = string.Join(',', Enumerable.Range(0, 1001).Select(index => $$"""{"Id":"page-{{index}}"}"""));
        var json = $$"""{"Version":3,"Pages":[{{pages}}]}""";
        File.WriteAllText(service.StateFilePath, json);

        var loaded = service.Load();

        Assert(!loaded.CanSave, "上限を超えるページ配列が保存対象として受理されました。");
        Assert(File.ReadAllText(service.StateFilePath) == json, "上限を超える保存データが変更されました。");
    });
}

static void TestDuplicatePagesPropertyProtection()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        var pages = string.Join(',', Enumerable.Range(0, 1001).Select(index => $$"""{"Id":"duplicate-{{index}}"}"""));
        var json = $$"""{"Version":3,"Pages":[],"Pages":[{{pages}}]}""";
        File.WriteAllText(service.StateFilePath, json);

        var loaded = service.Load();

        Assert(!loaded.CanSave, "重複Pagesプロパティが保存対象として受理されました。");
        Assert(loaded.Warning?.Contains("重複", StringComparison.Ordinal) == true,
            "重複Pagesプロパティをデシリアライズ前に拒否したことが分かる警告ではありません。");
        Assert(File.ReadAllText(service.StateFilePath) == json, "重複Pagesプロパティを含む元データが変更されました。");
    });
}

static void TestInvalidUtf8Protection()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        var prefix = Encoding.UTF8.GetBytes("{\"Version\":3,\"Pages\":[{\"Id\":\"page\",\"PlainText\":\"");
        var suffix = Encoding.UTF8.GetBytes("\",\"ThemeId\":\"lemon\"}]}");
        var invalidBytes = prefix.Concat(new byte[] { 0xC3, 0x28 }).Concat(suffix).ToArray();
        File.WriteAllBytes(service.StateFilePath, invalidBytes);

        var loaded = service.Load();

        Assert(loaded.FileExisted && !loaded.CanSave, "不正なUTF-8を含む保存データが保存対象として受理されました。");
        Assert(File.ReadAllBytes(service.StateFilePath).SequenceEqual(invalidBytes), "不正なUTF-8を含む元データが変更されました。");
    });
}

static void TestDuplicateVersionPropertyProtection()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        const string json = "{\"Version\":999,\"version\":3,\"Pages\":[]}";
        File.WriteAllText(service.StateFilePath, json);

        var loaded = service.Load();

        Assert(!loaded.CanSave, "大文字小文字違いの重複Versionが保存対象として受理されました。");
        Assert(loaded.Warning?.Contains("重複", StringComparison.Ordinal) == true,
            "重複Versionをデシリアライズ前に拒否したことが分かる警告ではありません。");
        Assert(File.ReadAllText(service.StateFilePath) == json, "重複Versionを含む元データが変更されました。");
    });
}

static void TestOversizedStateFileProtection()
{
    WithTemporaryDirectory(directory =>
    {
        var service = new PortableDataService(directory);
        Directory.CreateDirectory(service.DataDirectory);
        using (var stream = new FileStream(service.StateFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength((64L * 1024 * 1024) + 1);
        }

        var loaded = service.Load();

        Assert(!loaded.CanSave, "上限を超える保存ファイルが保存対象として受理されました。");
        Assert(loaded.Warning?.Contains("大きすぎ", StringComparison.Ordinal) == true,
            "過大な保存ファイルを全量読込前に拒否したことが分かる警告ではありません。");
    });
}

static void TestOversizedXamlPackageProtection()
{
    using var package = new MemoryStream();
    var source = new FlowDocument(new Paragraph(new Run("safe")));
    new TextRange(source.ContentStart, source.ContentEnd).Save(package, DataFormats.XamlPackage);

    package.Position = 0;
    using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
    {
        var contentTypes = archive.GetEntry("[Content_Types].xml")
            ?? throw new InvalidOperationException("XAMLパッケージのContent Typesがありません。");
        string xml;
        using (var reader = new StreamReader(contentTypes.Open(), leaveOpen: false))
        {
            xml = reader.ReadToEnd();
        }
        contentTypes.Delete();
        xml = xml.Replace(
            "</Types>",
            "<Default Extension=\"bin\" ContentType=\"application/octet-stream\" /></Types>",
            StringComparison.Ordinal);
        using (var writer = new StreamWriter(archive.CreateEntry("[Content_Types].xml").Open()))
        {
            writer.Write(xml);
        }

        var oversized = archive.CreateEntry("Resources/oversized.bin", CompressionLevel.SmallestSize);
        using var output = oversized.Open();
        var buffer = new byte[1024 * 1024];
        for (var index = 0; index < 17; index++)
        {
            output.Write(buffer);
        }
    }

    var encoded = Convert.ToBase64String(package.ToArray());
    var destination = new FlowDocument();
    var result = DocumentPersistence.Restore(destination, encoded, null);

    Assert(result == DocumentRestoreResult.Failed, "展開後サイズ上限を超えるXAMLパッケージが復元されました。");
}

static void TestDeepXamlPackageProtection()
{
    var document = new FlowDocument();
    Block nested = new Paragraph(new Run("deep"));
    for (var depth = 0; depth < 160; depth++)
    {
        var section = new Section();
        section.Blocks.Add(nested);
        nested = section;
    }
    document.Blocks.Add(nested);

    using var package = new MemoryStream();
    new TextRange(document.ContentStart, document.ContentEnd).Save(package, DataFormats.XamlPackage);
    var destination = new FlowDocument();
    var result = DocumentPersistence.Restore(destination, Convert.ToBase64String(package.ToArray()), null);

    Assert(result == DocumentRestoreResult.Failed, "深さ上限を超えるXAML本文が復元されました。");
}

static void TestPortableRuntimeProfile()
{
    var executableDirectory = Path.GetFullPath(@"C:\Portable\Keisai");
    var profile = AppRuntimeProfile.CreateForTests(
        isPackaged: false,
        executableDirectory,
        packagedLocalStateDirectory: @"C:\Users\Tester\AppData\Local\Packages\ignored\LocalState");

    Assert(!profile.IsPackaged, "ポータブル版がStore版として判定されています。");
    Assert(profile.StorageBaseDirectory == executableDirectory, "ポータブル版の保存先が実行フォルダーではありません。");
    Assert(profile.StartupBackend == StartupBackend.PortableLocalCache, "ポータブル版がローカルキャッシュ自動起動方式ではありません。");
}

static void TestStartupCacheRuntimeProfile()
{
    var cachedExecutableDirectory = Path.GetFullPath(@"C:\Users\Tester\AppData\Local\Y-TEC\StickyNote\app\abc");
    var sourceDirectory = Path.GetFullPath(@"G:\マイドライブ\罫彩");
    var profile = AppRuntimeProfile.CreateForTests(
        isPackaged: false,
        cachedExecutableDirectory,
        packagedLocalStateDirectory: null,
        startupDataRoot: sourceDirectory);

    Assert(!profile.IsPackaged, "ローカル自動起動版がStore版として判定されています。");
    Assert(profile.StorageBaseDirectory == sourceDirectory, "ローカル自動起動版が元のポータブル保存先を使っていません。");
    Assert(profile.StartupBackend == StartupBackend.PortableLocalCache, "ローカル自動起動版の自動起動方式が不正です。");
}

static void TestModeDataIsolation()
{
    WithTemporaryDirectory(directory =>
    {
        AppRuntimeOptions.EnableTestModeForCurrentProcess(directory);
        Assert(
            AppRuntimeOptions.PortableDataRootOverride == Path.GetFullPath(directory),
            "test-modeの保存先が専用一時フォルダーへ分離されていません。");
    });
}

static void TestPackagedRuntimeProfile()
{
    var executableDirectory = Path.GetFullPath(@"C:\Program Files\WindowsApps\Y-TEC.Keisai");
    var localStateDirectory = Path.GetFullPath(@"C:\Users\Tester\AppData\Local\Packages\Y-TEC.Keisai_abc\LocalState");
    var profile = AppRuntimeProfile.CreateForTests(
        isPackaged: true,
        executableDirectory,
        localStateDirectory);

    Assert(profile.IsPackaged, "Store版がポータブル版として判定されています。");
    Assert(profile.StorageBaseDirectory == localStateDirectory, "Store版の保存先がパッケージLocalStateではありません。");
    Assert(profile.StorageBaseDirectory != executableDirectory, "読み取り専用のパッケージ配置先へ保存しようとしています。");
    Assert(profile.StartupBackend == StartupBackend.PackagedStartupTask, "Store版の自動起動方式がパッケージStartupTaskではありません。");
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

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

public sealed class XamlActivationProbe
{
    public XamlActivationProbe()
    {
        Created = true;
    }

    public static bool Created { get; set; }
}

internal static class WindowsCommandLine
{
    public static string[] Parse(string commandLine)
    {
        var pointer = CommandLineToArgvW(commandLine, out var count);
        if (pointer == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CommandLineToArgvWに失敗しました: {Marshal.GetLastWin32Error()}");
        }

        try
        {
            var arguments = new string[count];
            for (var index = 0; index < count; index++)
            {
                arguments[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size))
                    ?? string.Empty;
            }

            return arguments;
        }
        finally
        {
            _ = LocalFree(pointer);
        }
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
