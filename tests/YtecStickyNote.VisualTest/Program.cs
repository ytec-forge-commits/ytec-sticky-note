using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YtecStickyNote;

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

        var editor = Require<RichTextBox>(window, "Editor");
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
        editor.CaretPosition = editor.Document.ContentEnd;

        var themeButtonName = themeId switch
        {
            "lemon" => "ThemeLemon",
            "sakura" => "ThemeSakura",
            "mint" => "ThemeMint",
            "sky" => "ThemeSky",
            "ivory" => "ThemeIvory",
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
        window.Close();
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
    list.ListItems.Add(new ListItem(CreateLine("箇条書きの項目", Brushes.DarkSlateGray)));
    list.ListItems.Add(new ListItem(CreateLine("次の項目", Brushes.DarkSlateGray)));
    return list;
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
