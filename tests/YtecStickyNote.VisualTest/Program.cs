using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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

AppRuntimeOptions.EnableTestModeForCurrentProcess();

var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
app.Resources["AppFont"] = new FontFamily("Yu Gothic UI");

var window = new MainWindow
{
    Width = width,
    Height = height,
    Left = 2020,
    Top = 80
};

Exception? failure = null;
window.Loaded += async (_, _) =>
{
    try
    {
        window.Left = 2020;
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

        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(350);
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

    var restoredParagraph = restored.Blocks.OfType<List>().Single().ListItems.Single().Blocks.OfType<Paragraph>().Single();
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

static T Require<T>(FrameworkElement root, string name) where T : class
{
    return root.FindName(name) as T ?? throw new InvalidOperationException($"{name} が見つかりません。");
}
}
