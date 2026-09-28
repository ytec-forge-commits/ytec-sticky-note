using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YtecStickyNote;
using YtecStickyNote.Models;
using YtecStickyNote.Services;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
var width = args.Length > 0 && double.TryParse(args[0], out var requestedWidth) ? requestedWidth : 520;
var height = args.Length > 1 && double.TryParse(args[1], out var requestedHeight) ? requestedHeight : 620;
var outputPath = args.Length > 2
    ? Path.GetFullPath(args[2])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"visual-{width:0}x{height:0}.png"));
var themeId = args.Length > 3 ? args[3].ToLowerInvariant() : "sakura";
var testDataRoot = Path.Combine(Path.GetTempPath(), $"keisai-visual-test-{Environment.ProcessId}-{Guid.NewGuid():N}");

AppRuntimeOptions.EnableTestModeForCurrentProcess(testDataRoot);

var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
app.Resources["AppFont"] = new FontFamily("Yu Gothic UI");

var window = new MainWindow
{
    Width = width,
    Height = height,
    Left = 80,
    Top = 80
};

if (!Require<RichTextBox>(window, "Editor").IsReadOnly)
{
    throw new InvalidOperationException("非同期初期化が完了する前から編集欄が入力可能です。");
}

Exception? failure = null;
window.Loaded += async (_, _) =>
{
    try
    {
        window.Left = 80;
        window.Top = 80;
        window.Width = width;
        window.Height = height;

        if (window.ShowInTaskbar)
        {
            throw new InvalidOperationException("付箋ウィンドウがタスクバーへ表示される設定です。");
        }
        if (window.Icon is null)
        {
            throw new InvalidOperationException("アプリアイコンが読み込まれていません。");
        }
        var autoStartCheck = Require<CheckBox>(window, "AutoStartCheck");
        if (autoStartCheck.IsChecked == true)
        {
            throw new InvalidOperationException("検証モードで自動起動が有効になっています。");
        }
        window.HideToTray();
        if (window.IsVisible)
        {
            throw new InvalidOperationException("タスクトレイへ格納してもウィンドウが表示されています。");
        }
        window.RestoreFromTray();
        if (!window.IsVisible)
        {
            throw new InvalidOperationException("タスクトレイから付箋を再表示できません。");
        }
        window.WindowState = WindowState.Minimized;
        if (window.IsVisible)
        {
            throw new InvalidOperationException("最小化しても付箋が表示されたままです。");
        }
        window.RestoreFromTray();
        window.Close();
        if (window.IsVisible)
        {
            throw new InvalidOperationException("×で閉じても付箋が表示されたままです。");
        }
        window.RestoreFromTray();

        VerifyDocumentPersistence();

        var editor = Require<RichTextBox>(window, "Editor");
        VerifyFormattingToolbarSynchronization(window, editor);
        VerifyBulletToolbarSynchronization(window, editor);
        VerifyBulletMarkerNormalization(window, editor);
        VerifyBulletHierarchySynchronization(window, editor);
        VerifyListEditingBoundaries(editor);
        VerifyListDeletionBoundaries(window, editor);
        VerifyUndoRedo(window, editor);
        VerifyClearCharacterFormatting(window, editor);
        VerifySearch(window, editor);
        VerifyPlainTextPaste(window, editor);
        VerifyPages(window, editor);
        if (Require<ComboBox>(window, "FontColorCombo").Items.Count != 10)
        {
            throw new InvalidOperationException("文字色が10色ではありません。");
        }
        editor.Document.Blocks.Clear();
        editor.Document.Blocks.Add(CreateTitle());
        editor.Document.Blocks.Add(CreateLine("・資料を確認する", Brushes.DarkSlateGray));
        editor.Document.Blocks.Add(CreateLine("・14時に打ち合わせ", new SolidColorBrush(Color.FromRgb(38, 74, 103)), italic: true));
        editor.Document.Blocks.Add(CreateDecoratedLine());
        editor.Document.Blocks.Add(CreateLine("罫線と文字の行送りを確認", Brushes.DarkSlateGray));
        var centered = CreateLine("中央揃え", new SolidColorBrush(Color.FromRgb(104, 74, 121)));
        centered.TextAlignment = TextAlignment.Center;
        editor.Document.Blocks.Add(centered);
        editor.Document.Blocks.Add(CreateBulletList());
        var titleRun = ((Paragraph)editor.Document.Blocks.FirstBlock!).Inlines.OfType<Run>().First();
        var titleCaret = titleRun.ContentStart.GetPositionAtOffset(1, LogicalDirection.Forward) ?? titleRun.ContentStart;
        editor.Selection.Select(titleCaret, titleCaret);

        var themeButtonName = themeId switch
        {
            "lemon" => "ThemeLemon",
            "sakura" => "ThemeSakura",
            "mint" => "ThemeMint",
            "sky" => "ThemeSky",
            "ivory" => "ThemeIvory",
            "lavender" => "ThemeLavender",
            "peach" => "ThemePeach",
            "aqua" => "ThemeAqua",
            "gray" => "ThemeGray",
            "mocha" => "ThemeMocha",
            _ => throw new ArgumentOutOfRangeException(nameof(themeId), themeId, "不明な背景です。")
        };
        var themeButton = Require<ToggleButton>(window, themeButtonName);
        themeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        var addPage = Require<Button>(window, "AddPageButton");
        var previousPage = Require<Button>(window, "PreviousPageButton");
        addPage.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        addPage.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        previousPage.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        previousPage.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (!Require<TextBlock>(window, "PageIndicatorText").Text.Contains("1 / 3", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("公開スクリーンショットで複数ページ操作を確認できません。");
        }
        Require<TextBox>(window, "SearchBox").Text = "14時";
        if (!Require<TextBlock>(window, "SearchResultText").Text.Contains("1 / 1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("公開スクリーンショットで検索結果を確認できません。");
        }

        // Tray restore and window-profile checks intentionally exercise the saved
        // placement. Reapply the requested dimensions before rendering so the
        // visual artifact actually proves the width passed by the test runner.
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(350);
        window.WindowState = WindowState.Normal;
        window.MinWidth = width;
        window.MaxWidth = width;
        window.MinHeight = height;
        window.MaxHeight = height;
        window.Width = width;
        window.Height = height;
        window.UpdateLayout();

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var stream = File.Create(outputPath);
        encoder.Save(stream);
        Console.WriteLine(outputPath);
    }
    catch (Exception ex)
    {
        failure = ex;
        Console.Error.WriteLine(ex);
    }
    finally
    {
        window.TryExitApplication();
        app.Shutdown(failure is null ? 0 : 1);
    }
};

app.Run(window);
if (Directory.Exists(testDataRoot))
{
    var tempPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    var resolvedTestDataRoot = Path.GetFullPath(testDataRoot);
    if (!resolvedTestDataRoot.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"テスト用一時フォルダー外の削除を拒否しました: {resolvedTestDataRoot}");
    }
    Directory.Delete(resolvedTestDataRoot, recursive: true);
}
return failure is null ? 0 : 1;
    }

static Paragraph CreateTitle()
{
    var paragraph = NewParagraph();
    paragraph.Inlines.Add(new Run("明日の予定")
    {
        FontWeight = FontWeights.Bold,
        FontSize = 22,
        Foreground = new SolidColorBrush(Color.FromRgb(163, 62, 62))
    });
    return paragraph;
}

static Paragraph CreateLine(string text, Brush foreground, bool italic = false)
{
    var paragraph = NewParagraph();
    paragraph.Inlines.Add(new Run(text)
    {
        FontSize = 18,
        FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
        Foreground = foreground
    });
    return paragraph;
}

static Paragraph CreateDecoratedLine()
{
    var paragraph = NewParagraph();
    paragraph.Inlines.Add(new Run("完了したら連絡 ") { FontSize = 18 });
    paragraph.Inlines.Add(new Run("忘れずに")
    {
        FontSize = 18,
        FontWeight = FontWeights.Bold,
        TextDecorations = TextDecorations.Underline,
        Foreground = new SolidColorBrush(Color.FromRgb(53, 107, 76))
    });
    paragraph.Inlines.Add(new Run(" 旧予定")
    {
        FontSize = 16,
        TextDecorations = TextDecorations.Strikethrough,
        Foreground = Brushes.DimGray
    });
    return paragraph;
}

static List CreateBulletList()
{
    var list = new List { MarkerStyle = TextMarkerStyle.Disc };
    var wrappedItem = CreateLine("箇条書きの長い項目は、次の行も文字の開始位置へ自然に揃います", Brushes.DarkSlateGray);
    wrappedItem.Inlines.Add(new LineBreak());
    wrappedItem.Inlines.Add(new Run("Shift+Enterの項目内改行も同じ位置を保ちます") { FontSize = 18 });
    list.ListItems.Add(new ListItem(wrappedItem));
    list.ListItems.Add(new ListItem(CreateLine("次の項目", Brushes.DarkSlateGray)));
    return list;
}

static void VerifyDocumentPersistence()
{
    var paragraph = NewParagraph();
    paragraph.Inlines.Add(new Run("箇条書きの先頭"));
    paragraph.Inlines.Add(new LineBreak());
    paragraph.Inlines.Add(new Run("項目内の改行"));
    var source = new FlowDocument(new List(new ListItem(paragraph)) { MarkerStyle = TextMarkerStyle.Disc });

    var snapshot = DocumentPersistence.Capture(source);
    if (string.IsNullOrWhiteSpace(snapshot.XamlPackageBase64) || string.IsNullOrWhiteSpace(snapshot.RtfBase64))
    {
        throw new InvalidOperationException("本文を2形式で保存できません。");
    }

    var restored = new FlowDocument();
    var result = DocumentPersistence.Restore(restored, snapshot.XamlPackageBase64, snapshot.RtfBase64);
    if (result != DocumentRestoreResult.XamlPackage)
    {
        throw new InvalidOperationException("XAMLパッケージから本文を復元できません。");
    }

    var restoredList = restored.Blocks.OfType<List>().Single();
    var restoredParagraph = restoredList.ListItems.Single().Blocks.OfType<Paragraph>().Single();
    if (restoredList.MarkerStyle != TextMarkerStyle.Disc || restoredList.ListItems.Count != 1)
    {
        throw new InvalidOperationException("保存・再起動相当の復元後に箇条書きの項目数またはマーカーが変化しました。");
    }
    if (!restoredParagraph.Inlines.OfType<LineBreak>().Any())
    {
        throw new InvalidOperationException("箇条書き内の改行が復元されていません。");
    }

    var fallback = new FlowDocument();
    if (DocumentPersistence.Restore(fallback, "破損データ", snapshot.RtfBase64) != DocumentRestoreResult.RtfFallback)
    {
        throw new InvalidOperationException("新形式が壊れた場合にRTFへフォールバックできません。");
    }
}

static void VerifyFormattingToolbarSynchronization(MainWindow window, RichTextBox editor)
{
    var paragraph = NewParagraph();
    var first = new Run("最初の範囲")
    {
        FontFamily = new FontFamily("Meiryo"),
        FontSize = 14,
        Foreground = new SolidColorBrush(Color.FromRgb(163, 62, 62))
    };
    var second = new Run("次の範囲")
    {
        FontFamily = new FontFamily("Yu Gothic UI"),
        FontSize = 14,
        Foreground = new SolidColorBrush(Color.FromRgb(53, 107, 76))
    };
    paragraph.Inlines.Add(first);
    paragraph.Inlines.Add(new Run(" "));
    paragraph.Inlines.Add(second);
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(paragraph);

    var fontCombo = Require<ComboBox>(window, "FontFamilyCombo");
    var sizeCombo = Require<ComboBox>(window, "FontSizeCombo");
    var colorCombo = Require<ComboBox>(window, "FontColorCombo");
    var repeatedFont = fontCombo.Items.OfType<FontChoice>().First(font =>
        !string.Equals(font.FamilyName, "Meiryo", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(font.FamilyName, "Yu Gothic UI", StringComparison.OrdinalIgnoreCase));

    editor.Selection.Select(first.ContentStart, first.ContentEnd);
    AssertSelectedFont(fontCombo, "Meiryo");
    AssertSelectedTag(sizeCombo, "14");
    AssertSelectedTag(colorCombo, "#A33E3E");
    fontCombo.SelectedItem = repeatedFont;
    sizeCombo.SelectedItem = FindTaggedItem(sizeCombo, "16");
    colorCombo.SelectedItem = FindTaggedItem(colorCombo, "#264A67");
    AssertRunValue(first, TextElement.FontSizeProperty, 16d, "最初の範囲へ16を適用できません。");

    editor.Selection.Select(second.ContentStart, second.ContentEnd);
    AssertSelectedFont(fontCombo, "Yu Gothic UI");
    AssertSelectedTag(sizeCombo, "14");
    AssertSelectedTag(colorCombo, "#356B4C");
    fontCombo.SelectedItem = repeatedFont;
    sizeCombo.SelectedItem = FindTaggedItem(sizeCombo, "16");
    colorCombo.SelectedItem = FindTaggedItem(colorCombo, "#264A67");
    AssertRunValue(second, TextElement.FontSizeProperty, 16d, "別の範囲へ同じ16を続けて適用できません。");

    var secondFamily = second.GetValue(TextElement.FontFamilyProperty) as FontFamily;
    if (!string.Equals(secondFamily?.Source, repeatedFont.FamilyName, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("別の範囲へ同じフォントを続けて適用できません。");
    }

    var secondColor = second.GetValue(TextElement.ForegroundProperty) as SolidColorBrush;
    if (secondColor?.Color != Color.FromRgb(38, 74, 103))
    {
        throw new InvalidOperationException("別の範囲へ同じ文字色を続けて適用できません。");
    }
}

static void VerifyBulletToolbarSynchronization(MainWindow window, RichTextBox editor)
{
    var plain = CreateLine("通常の段落", Brushes.DarkSlateGray);
    var bulletParagraph = CreateLine("箇条書き", Brushes.DarkSlateGray);
    var list = new List(new ListItem(bulletParagraph)) { MarkerStyle = TextMarkerStyle.Disc };
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(plain);
    editor.Document.Blocks.Add(list);

    var bulletButton = Require<ToggleButton>(window, "BulletButton");
    var bulletCaret = bulletParagraph.ContentStart.GetNextInsertionPosition(LogicalDirection.Forward) ?? bulletParagraph.ContentStart;
    editor.Selection.Select(bulletCaret, bulletCaret);
    if (bulletButton.IsChecked != true)
    {
        throw new InvalidOperationException("箇条書き内のカーソル位置がONとして表示されません。");
    }

    editor.Selection.Select(plain.ContentStart, bulletParagraph.ContentEnd);
    if (!bulletButton.IsThreeState || bulletButton.IsChecked is not null)
    {
        throw new InvalidOperationException("通常段落と箇条書きの混在選択が中間状態として表示されません。");
    }

    editor.Selection.Select(bulletCaret, bulletCaret);
    if (!RichTextListEditing.TryRemoveListMarkerAtItemStart(editor))
    {
        throw new InvalidOperationException("項目先頭のBackspace相当操作を処理できません。");
    }
    if (bulletButton.IsChecked != false)
    {
        throw new InvalidOperationException("項目先頭のBackspace後に箇条書き表示がOFFへ同期されません。");
    }
    var resultingParagraph = editor.Selection.Start.Paragraph;
    var expectedCaret = resultingParagraph?.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
    var actualCaret = editor.Selection.Start.GetInsertionPosition(LogicalDirection.Forward);
    if (!editor.Selection.IsEmpty || resultingParagraph is null || expectedCaret is null || actualCaret is null ||
        expectedCaret.CompareTo(actualCaret) != 0)
    {
        throw new InvalidOperationException("項目先頭のBackspace後に選択範囲を残さず、本文先頭へカーソルを戻せません。");
    }
}

static void VerifyBulletMarkerNormalization(MainWindow window, RichTextBox editor)
{
    var discParagraph = CreateLine("丸付き", Brushes.DarkSlateGray);
    var squareParagraph = CreateLine("四角付き", Brushes.DarkSlateGray);
    var numberedParagraph = CreateLine("番号付き", Brushes.DarkSlateGray);
    var disc = new List(new ListItem(discParagraph)) { MarkerStyle = TextMarkerStyle.Disc };
    var square = new List(new ListItem(squareParagraph)) { MarkerStyle = TextMarkerStyle.Square };
    var numbered = new List(new ListItem(numberedParagraph)) { MarkerStyle = TextMarkerStyle.Decimal };
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(disc);
    editor.Document.Blocks.Add(square);
    editor.Document.Blocks.Add(numbered);

    var bulletButton = Require<ToggleButton>(window, "BulletButton");
    editor.Selection.Select(discParagraph.ContentStart, squareParagraph.ContentEnd);
    if (bulletButton.IsChecked is not null)
    {
        throw new InvalidOperationException("異なる箇条書きマーカーの選択が中間状態になりません。");
    }

    editor.Selection.Select(discParagraph.ContentStart, numberedParagraph.ContentEnd);
    if (bulletButton.IsChecked is not null ||
        disc.MarkerStyle != TextMarkerStyle.Disc ||
        square.MarkerStyle != TextMarkerStyle.Square ||
        numbered.MarkerStyle != TextMarkerStyle.Decimal)
    {
        throw new InvalidOperationException("異なるマーカー種別を混在状態として表示し、操作前の外部リストを維持できません。");
    }

    editor.Selection.Select(numberedParagraph.ContentEnd, discParagraph.ContentStart);
    if (bulletButton.IsChecked is not null)
    {
        throw new InvalidOperationException("逆方向の複数行選択で箇条書きの混在状態を判定できません。");
    }

    editor.Selection.Select(discParagraph.ContentStart, numberedParagraph.ContentEnd);
    ResetUndo(editor);
    bulletButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    var selectedLists = editor.Document.Blocks.OfType<List>().ToList();
    if (selectedLists.Count != 3 || selectedLists.Any(list => list.MarkerStyle != TextMarkerStyle.Disc))
    {
        throw new InvalidOperationException("混在選択を単純な丸付き箇条書きへ統一できません。");
    }
    if (!editor.CanUndo)
    {
        throw new InvalidOperationException("箇条書きの統一を1回のUndoへまとめられていません。");
    }
    editor.Undo();
    if (disc.MarkerStyle != TextMarkerStyle.Disc || square.MarkerStyle != TextMarkerStyle.Square ||
        numbered.MarkerStyle != TextMarkerStyle.Decimal || editor.CanUndo)
    {
        throw new InvalidOperationException("箇条書きの統一を1回のUndoで元のマーカーへ戻せません。");
    }
    editor.Redo();
}

static void VerifyBulletHierarchySynchronization(MainWindow window, RichTextBox editor)
{
    var outerParagraph = CreateLine("親階層", Brushes.DarkSlateGray);
    var nestedParagraph = CreateLine("子階層", Brushes.DarkSlateGray);
    var outerItem = new ListItem(outerParagraph);
    var outerList = new List(outerItem) { MarkerStyle = TextMarkerStyle.Disc };
    var nestedList = new List(new ListItem(nestedParagraph)) { MarkerStyle = TextMarkerStyle.Disc };
    outerItem.Blocks.Add(nestedList);
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(outerList);

    editor.Selection.Select(outerParagraph.ContentStart, nestedParagraph.ContentEnd);
    var bulletButton = Require<ToggleButton>(window, "BulletButton");
    if (bulletButton.IsChecked is not null)
    {
        throw new InvalidOperationException("異なる箇条書き階層の選択が中間状態になりません。");
    }
}

static void VerifyListEditingBoundaries(RichTextBox editor)
{
    var first = CreateLine("最初", Brushes.DarkSlateGray);
    var list = new List(new ListItem(first)) { MarkerStyle = TextMarkerStyle.Disc };
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(list);
    editor.Selection.Select(first.ContentEnd, first.ContentEnd);
    editor.CaretPosition = editor.CaretPosition.InsertParagraphBreak();
    if (list.ListItems.Count != 2)
    {
        throw new InvalidOperationException($"通常Enterで次の箇条書き項目を作成できません（項目数: {list.ListItems.Count}、先頭項目内段落数: {list.ListItems.FirstListItem.Blocks.Count}）。");
    }

    editor.CaretPosition = editor.CaretPosition.InsertLineBreak();
    var secondParagraph = list.ListItems.LastListItem.Blocks.OfType<Paragraph>().Single();
    if (!secondParagraph.Inlines.OfType<LineBreak>().Any())
    {
        throw new InvalidOperationException("Shift+Enter相当の標準項目内改行を維持できません。");
    }

    var emptyParagraph = NewParagraph();
    emptyParagraph.Inlines.Add(new Run());
    var emptyList = new List(new ListItem(emptyParagraph)) { MarkerStyle = TextMarkerStyle.Disc };
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(emptyList);
    editor.Selection.Select(emptyParagraph.ContentStart, emptyParagraph.ContentStart);
    var exitedEmptyItem = RichTextListEditing.TryExitEmptyListItem(editor);
    var trailingParagraph = editor.Document.Blocks.OfType<Paragraph>().LastOrDefault();
    if (!exitedEmptyItem || trailingParagraph is null)
    {
        throw new InvalidOperationException("空の箇条書き項目でEnterした時に箇条書きを終了できません。");
    }
}

static void VerifyListDeletionBoundaries(MainWindow window, RichTextBox editor)
{
    var characterParagraph = CreateLine("ABC", Brushes.DarkSlateGray);
    var characterRun = characterParagraph.Inlines.OfType<Run>().Single();
    var characterList = new List(new ListItem(characterParagraph)) { MarkerStyle = TextMarkerStyle.Disc };
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(characterList);
    var characterStart = characterRun.ContentStart.GetPositionAtOffset(1, LogicalDirection.Forward)
        ?? throw new InvalidOperationException("文字削除テストの開始位置を取得できません。");
    var characterEnd = characterRun.ContentEnd.GetPositionAtOffset(-1, LogicalDirection.Backward)
        ?? throw new InvalidOperationException("文字削除テストの終了位置を取得できません。");
    editor.Selection.Select(characterStart, characterEnd);
    EditingCommands.Delete.Execute(null, editor);
    var characterText = new TextRange(characterRun.ContentStart, characterRun.ContentEnd).Text;
    if (!characterText.EndsWith("AC", StringComparison.Ordinal) || characterText.Contains('B') || characterParagraph.Parent is not ListItem ||
        Require<ToggleButton>(window, "BulletButton").IsChecked != true)
    {
        throw new InvalidOperationException($"箇条書き内の文字削除後に本文または箇条書き判定が壊れました（本文: {characterText}、状態: {Require<ToggleButton>(window, "BulletButton").IsChecked}）。");
    }

    editor.Selection.Select(characterRun.ContentStart, characterRun.ContentEnd);
    EditingCommands.Delete.Execute(null, editor);
    if (characterParagraph.Parent is not ListItem || Require<ToggleButton>(window, "BulletButton").IsChecked != true)
    {
        throw new InvalidOperationException("箇条書きの行全体を削除した後に箇条書き判定が壊れました。");
    }

    var first = CreateLine("前", Brushes.DarkSlateGray);
    var second = CreateLine("後", Brushes.DarkSlateGray);
    var boundaryList = new List { MarkerStyle = TextMarkerStyle.Disc };
    boundaryList.ListItems.Add(new ListItem(first));
    boundaryList.ListItems.Add(new ListItem(second));
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(boundaryList);
    editor.Selection.Select(first.ContentEnd, first.ContentEnd);
    EditingCommands.Delete.Execute(null, editor);
    var remainingText = new TextRange(boundaryList.ContentStart, boundaryList.ContentEnd).Text;
    if (boundaryList.MarkerStyle != TextMarkerStyle.Disc || boundaryList.ListItems.Count != 1 ||
        !remainingText.Contains("前後", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("箇条書き項目末尾のDeleteで次項目を自然に結合できません。");
    }
}

static void VerifyUndoRedo(MainWindow window, RichTextBox editor)
{
    var paragraph = CreateLine("元の文字", Brushes.DarkSlateGray);
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(paragraph);
    editor.Selection.Select(paragraph.ContentEnd, paragraph.ContentEnd);
    ResetUndo(editor);
    editor.Selection.Text = "追記";

    var undoButton = Require<Button>(window, "UndoButton");
    var redoButton = Require<Button>(window, "RedoButton");
    undoButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Contains("追記", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("ツールバーから文字入力をUndoできません。");
    }

    redoButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (!new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Contains("追記", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("ツールバーから文字入力をRedoできません。");
    }

    editor.Selection.Select(paragraph.ContentStart, paragraph.ContentStart.GetPositionAtOffset(1, LogicalDirection.Forward)!);
    ResetUndo(editor);
    EditingCommands.Delete.Execute(null, editor);
    var deletedText = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
    editor.Undo();
    var restoredText = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
    editor.Redo();
    var redoneDeletedText = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
    if (deletedText == restoredText || deletedText != redoneDeletedText)
    {
        throw new InvalidOperationException("文字削除をUndo/Redoで往復できません。");
    }
}

static void VerifyClearCharacterFormatting(MainWindow window, RichTextBox editor)
{
    var paragraph = NewParagraph();
    var decorated = new Run("装飾文字")
    {
        FontFamily = new FontFamily("Meiryo"),
        FontSize = 22,
        FontWeight = FontWeights.Bold,
        FontStyle = FontStyles.Italic,
        Foreground = Brushes.Red,
        TextDecorations = TextDecorations.Underline
    };
    paragraph.Inlines.Add(decorated);
    var list = new List(new ListItem(paragraph)) { MarkerStyle = TextMarkerStyle.Disc };
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(list);
    editor.Selection.Select(decorated.ContentStart, decorated.ContentEnd);
    ResetUndo(editor);

    Require<Button>(window, "ClearFormattingButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (decorated.FontWeight != FontWeights.Normal ||
        decorated.FontStyle != FontStyles.Normal ||
        decorated.TextDecorations is { Count: > 0 } ||
        Math.Abs(decorated.FontSize - editor.FontSize) > 0.01 ||
        paragraph.Parent is not ListItem)
    {
        throw new InvalidOperationException("文字装飾だけを既定値へ戻し、箇条書き構造を維持できません。");
    }

    if (!editor.CanUndo)
    {
        throw new InvalidOperationException("文字装飾クリアを1回のUndoで戻せません。");
    }

    editor.Undo();
    if (decorated.FontWeight != FontWeights.Bold || decorated.FontStyle != FontStyles.Italic ||
        decorated.TextDecorations is not { Count: > 0 } || editor.CanUndo)
    {
        throw new InvalidOperationException("文字装飾クリアを1回のUndoで元の装飾へ戻せません。");
    }
    editor.Redo();
    if (decorated.FontWeight != FontWeights.Normal || decorated.FontStyle != FontStyles.Normal)
    {
        throw new InvalidOperationException("文字装飾クリアをRedoできません。");
    }
}

static void VerifySearch(MainWindow window, RichTextBox editor)
{
    var paragraph = NewParagraph();
    paragraph.Inlines.Add(new Run("罫") { FontWeight = FontWeights.Bold });
    paragraph.Inlines.Add(new Run("彩と罫彩"));
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(paragraph);
    ResetUndo(editor);

    Require<Button>(window, "SearchButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    var searchPanel = Require<FrameworkElement>(window, "SearchPanel");
    if (searchPanel.Visibility != Visibility.Visible)
    {
        throw new InvalidOperationException("検索ボタンで検索バーを表示できません。");
    }

    var searchBox = Require<TextBox>(window, "SearchBox");
    searchBox.Text = "罫彩";
    if (!string.Equals(editor.Selection.Text, "罫彩", StringComparison.Ordinal) ||
        !Require<TextBlock>(window, "SearchResultText").Text.Contains("1 / 2", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("書式境界をまたぐ最初の検索結果と件数を表示できません。");
    }

    Require<Button>(window, "SearchNextButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (!Require<TextBlock>(window, "SearchResultText").Text.Contains("2 / 2", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("次の検索結果へ移動できません。");
    }

    Require<Button>(window, "SearchNextButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (!Require<TextBlock>(window, "SearchResultText").Text.Contains("1 / 2", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("次の検索結果を末尾から先頭へ折り返せません。");
    }

    Require<Button>(window, "SearchPreviousButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (!Require<TextBlock>(window, "SearchResultText").Text.Contains("2 / 2", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("前の検索結果を先頭から末尾へ折り返せません。");
    }

    paragraph.Inlines.Add(new Run("と罫彩"));
    if (!Require<TextBlock>(window, "SearchResultText").Text.Contains("1 / 3", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("文書変更後に検索結果を再計算できません。");
    }
    ResetUndo(editor);

    if (editor.CanUndo)
    {
        throw new InvalidOperationException("検索操作が本文のUndo履歴を変更しています。");
    }
}

static void VerifyPlainTextPaste(MainWindow window, RichTextBox editor)
{
    var paragraph = NewParagraph();
    paragraph.Inlines.Add(new Run());
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(paragraph);
    editor.Selection.Select(paragraph.ContentStart, paragraph.ContentStart);
    ResetUndo(editor);

    var previousClipboard = Clipboard.GetDataObject();
    try
    {
        Clipboard.SetText("一行目\r\n二行目");
        var contextMenu = editor.ContextMenu ?? throw new InvalidOperationException("編集欄のメニューがありません。");
        var pastePlainText = contextMenu.Items.OfType<MenuItem>().SingleOrDefault(item => item.Name == "PastePlainTextMenuItem")
            ?? throw new InvalidOperationException("テキストのみ貼り付けメニューがありません。");
        pastePlainText.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        var text = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
        if (!text.Contains("一行目", StringComparison.Ordinal) || !text.Contains("二行目", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("プレーンテキスト貼り付けで文字と改行を挿入できません。");
        }

        if (!editor.CanUndo)
        {
            throw new InvalidOperationException("プレーンテキスト貼り付けを1回のUndoで戻せません。");
        }

        editor.Undo();
        var undoneText = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
        if (undoneText.Contains("一行目", StringComparison.Ordinal) || editor.CanUndo)
        {
            throw new InvalidOperationException("プレーンテキスト貼り付けを1回のUndoで取り消せません。");
        }
        editor.Redo();
        var redoneText = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
        if (!redoneText.Contains("一行目", StringComparison.Ordinal) || !redoneText.Contains("二行目", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("プレーンテキスト貼り付けをRedoできません。");
        }
    }
    finally
    {
        if (previousClipboard is not null)
        {
            Clipboard.SetDataObject(previousClipboard, true);
        }
        else
        {
            Clipboard.Clear();
        }
    }
}

static void VerifyPages(MainWindow window, RichTextBox editor)
{
    var previous = Require<Button>(window, "PreviousPageButton");
    var next = Require<Button>(window, "NextPageButton");
    var add = Require<Button>(window, "AddPageButton");
    var delete = Require<Button>(window, "DeletePageButton");
    var indicator = Require<TextBlock>(window, "PageIndicatorText");

    // The visual runner can be invoked repeatedly from the same build output.
    // Start from one page so that page positions and deletion behavior are deterministic.
    while (delete.IsEnabled)
    {
        delete.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    if (previous.IsEnabled || next.IsEnabled || delete.IsEnabled || !indicator.Text.Contains("1 / 1", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("単一ページ時の前後移動または最終ページ保護が不正です。");
    }

    var first = NewParagraph();
    var firstRun = new Run("一頁だけの本文")
    {
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.DarkGreen
    };
    first.Inlines.Add(firstRun);
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(first);
    Require<ToggleButton>(window, "ThemeMint").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    ResetUndo(editor);
    editor.Selection.Select(firstRun.ContentEnd, firstRun.ContentEnd);
    editor.Selection.Text = " 編集";
    if (!editor.CanUndo)
    {
        throw new InvalidOperationException("一頁目の編集履歴を作れません。");
    }

    add.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (!indicator.Text.Contains("2 / 2", StringComparison.Ordinal) ||
        !string.IsNullOrWhiteSpace(new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text) ||
        Require<ToggleButton>(window, "ThemeMint").IsChecked != true)
    {
        throw new InvalidOperationException("現在ページの次へ空ページと背景を追加できません。");
    }

    var second = NewParagraph();
    var secondRun = new Run("二頁だけの本文")
    {
        FontStyle = FontStyles.Italic,
        Foreground = Brushes.DarkBlue
    };
    second.Inlines.Add(secondRun);
    editor.Document.Blocks.Clear();
    editor.Document.Blocks.Add(second);
    Require<ToggleButton>(window, "ThemeSky").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    ResetUndo(editor);
    editor.Selection.Select(secondRun.ContentEnd, secondRun.ContentEnd);
    editor.Selection.Text = " 変更";

    previous.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    var firstText = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
    if (!firstText.Contains("一頁だけの本文 編集", StringComparison.Ordinal) ||
        firstText.Contains("二頁だけの本文", StringComparison.Ordinal) ||
        Require<ToggleButton>(window, "ThemeMint").IsChecked != true ||
        editor.CanUndo || editor.CanRedo)
    {
        throw new InvalidOperationException("ページ往復で本文・背景・Undo履歴を独立して保持できません。");
    }

    var searchPanel = Require<FrameworkElement>(window, "SearchPanel");
    if (searchPanel.Visibility != Visibility.Visible)
    {
        Require<Button>(window, "SearchButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    var searchBox = Require<TextBox>(window, "SearchBox");
    searchBox.Text = "二頁だけの本文";
    if (!Require<TextBlock>(window, "SearchResultText").Text.Contains("0 / 0", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("検索が表示中ではないページの本文を対象にしています。");
    }

    next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    var secondText = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
    if (!secondText.Contains("二頁だけの本文 変更", StringComparison.Ordinal) ||
        secondText.Contains("一頁だけの本文", StringComparison.Ordinal) ||
        Require<ToggleButton>(window, "ThemeSky").IsChecked != true ||
        editor.CanUndo || editor.CanRedo)
    {
        throw new InvalidOperationException("二頁目の本文・背景・Undo履歴を独立して復元できません。");
    }

    if (searchPanel.Visibility != Visibility.Visible)
    {
        Require<Button>(window, "SearchButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    searchBox.Text = "二頁だけの本文";
    if (!Require<TextBlock>(window, "SearchResultText").Text.Contains("1 / 1", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("表示中ページの検索結果を取得できません。");
    }

    previous.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    add.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (!indicator.Text.Contains("2 / 3", StringComparison.Ordinal) ||
        !string.IsNullOrWhiteSpace(new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text))
    {
        throw new InvalidOperationException("現在ページ直後への挿入位置が不正です。");
    }

    delete.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (!indicator.Text.Contains("2 / 2", StringComparison.Ordinal) ||
        !new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.Contains("二頁だけの本文 変更", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("現在ページを削除後に次のページを表示できません。");
    }

    delete.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (delete.IsEnabled || !indicator.Text.Contains("1 / 1", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("最後の1ページを削除できてしまいます。");
    }

    if (searchPanel.Visibility != Visibility.Visible)
    {
        Require<Button>(window, "SearchButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    searchBox.Text = string.Empty;
}

static ComboBoxItem FindTaggedItem(ComboBox comboBox, string tag) =>
    comboBox.Items.OfType<ComboBoxItem>().Single(item => string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase));

static void AssertSelectedTag(ComboBox comboBox, string expected)
{
    if (comboBox.SelectedItem is not ComboBoxItem item ||
        !string.Equals(item.Tag as string, expected, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"ツールバーへ {expected} が反映されていません。");
    }
}

static void AssertSelectedFont(ComboBox comboBox, string expected)
{
    if (comboBox.SelectedItem is not FontChoice font ||
        !string.Equals(font.FamilyName, expected, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"ツールバーへフォント {expected} が反映されていません。");
    }
}

static void AssertRunValue(Run run, DependencyProperty property, object expected, string message)
{
    if (!Equals(run.GetValue(property), expected))
    {
        throw new InvalidOperationException(message);
    }
}

static Paragraph NewParagraph() => new()
{
    Margin = new Thickness(0),
    LineHeight = 30,
    LineStackingStrategy = LineStackingStrategy.BlockLineHeight
};

static void ResetUndo(RichTextBox editor)
{
    editor.UndoLimit = 0;
    editor.UndoLimit = -1;
}

static T Require<T>(FrameworkElement root, string name) where T : class
{
    return root.FindName(name) as T ?? throw new InvalidOperationException($"{name} が見つかりません。");
}
}
