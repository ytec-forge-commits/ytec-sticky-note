using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using YtecStickyNote.Models;
using YtecStickyNote.Services;

namespace YtecStickyNote;

public partial class MainWindow : Window
{
    private const double NoteLineHeight = 30;
    private static readonly Thickness NotePagePadding = new(64, 10, 22, 24);
    private readonly PortableDataService _dataService = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _statusTimer;
    private AppState _state = new();
    private bool _isLoading = true;
    private bool _isClosing;
    private bool _allowApplicationExit;
    private bool _trayAvailable = true;
    private bool _savingBlocked;
    private bool _isNormalizingDocument;
    private WindowState _windowStateBeforeTray = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            TrySave(showErrorDialog: false);
        };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            SaveStatusText.Text = "保存済み";
        };

        Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Editor_ScrollChanged));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var loaded = _dataService.Load();
        _state = loaded.State;
        _savingBlocked = !loaded.CanSave;

        var bounds = WindowPlacementService.GetRestoredBounds(_state.Window);
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        LoadInstalledFonts();
        ApplyTheme(_state.ThemeId);
        LoadEditorContent(_state.RichTextRtfBase64);
        NormalizeDocumentLayout();

        Topmost = _state.AlwaysOnTop;
        TopmostButton.IsChecked = Topmost;

        _isLoading = false;
        UpdatePlaceholder();
        UpdateFormattingButtons();

        var testMode = AppRuntimeOptions.IsTestMode;
        if (!string.IsNullOrWhiteSpace(loaded.Warning))
        {
            ShowStatus("読込エラー", sticky: true);
            Editor.IsReadOnly = true;
            MessageBox.Show(loaded.Warning, "Y-TEC 付箋", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            ShowStatus(testMode ? "検証モード" : "保存済み", sticky: testMode);
        }

        if (!loaded.FileExisted)
        {
            ScheduleSave();
        }

        Editor.Focus();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        _saveTimer.Stop();
        if (!TrySave(showErrorDialog: true))
        {
            e.Cancel = true;
            return;
        }

        if (!_allowApplicationExit && _trayAvailable)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _isClosing = true;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && !_allowApplicationExit && _trayAvailable)
        {
            HideToTray();
        }
        else if (WindowState is WindowState.Normal or WindowState.Maximized)
        {
            _windowStateBeforeTray = WindowState;
        }
    }

    private void Window_BoundsChanged(object? sender, EventArgs e)
    {
        if (!_isLoading && WindowState == WindowState.Normal)
        {
            ScheduleSave();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_trayAvailable)
        {
            HideToTray();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    public void HideToTray()
    {
        if (WindowState is WindowState.Normal or WindowState.Maximized)
        {
            _windowStateBeforeTray = WindowState;
        }
        Hide();
    }

    public void EnableTaskbarFallback()
    {
        _trayAvailable = false;
        ShowInTaskbar = true;
    }

    public void RestoreFromTray()
    {
        var restoredState = _windowStateBeforeTray == WindowState.Maximized
            ? WindowState.Maximized
            : WindowState.Normal;
        WindowState = WindowState.Normal;
        Show();
        WindowState = restoredState;
        Activate();
        Editor.Focus();
    }

    public bool TryExitApplication()
    {
        if (_isClosing)
        {
            return true;
        }

        _allowApplicationExit = true;
        Close();
        if (_isClosing)
        {
            return true;
        }

        _allowApplicationExit = false;
        return false;
    }

    private void TopmostButton_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Topmost = TopmostButton.IsChecked == true;
        _state.AlwaysOnTop = Topmost;
        ScheduleSave();
    }

    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBold.Execute(null, Editor);
        Editor.Focus();
        ScheduleSave();
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleItalic.Execute(null, Editor);
        Editor.Focus();
        ScheduleSave();
    }

    private void UnderlineButton_Click(object sender, RoutedEventArgs e) => ToggleDecoration(TextDecorationLocation.Underline);

    private void StrikeButton_Click(object sender, RoutedEventArgs e) => ToggleDecoration(TextDecorationLocation.Strikethrough);

    private void CenterAlignButton_Click(object sender, RoutedEventArgs e)
    {
        var isCentered = IsSelectionValue(Block.TextAlignmentProperty, TextAlignment.Center);
        Editor.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, isCentered ? TextAlignment.Left : TextAlignment.Center);
        Editor.Focus();
        UpdateFormattingButtons();
        ScheduleSave();
    }

    private void BulletButton_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBullets.Execute(null, Editor);
        NormalizeDocumentLayout();
        Editor.Focus();
        UpdateFormattingButtons();
        ScheduleSave();
    }

    private void ToggleDecoration(TextDecorationLocation location)
    {
        var propertyValue = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        var current = propertyValue as TextDecorationCollection;
        var decorations = new TextDecorationCollection();

        if (current is not null)
        {
            foreach (var decoration in current.Where(item => item.Location != location))
            {
                decorations.Add(decoration.CloneCurrentValue());
            }
        }

        var hasDecoration = current?.Any(item => item.Location == location) == true;
        if (!hasDecoration)
        {
            var source = location == TextDecorationLocation.Underline ? TextDecorations.Underline : TextDecorations.Strikethrough;
            decorations.Add(source[0].CloneCurrentValue());
        }

        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, decorations);
        Editor.Focus();
        UpdateFormattingButtons();
        ScheduleSave();
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isLoading || FontFamilyCombo.SelectedItem is not FontChoice font)
        {
            return;
        }

        Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(font.FamilyName));
        Editor.Focus();
        ScheduleSave();
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isLoading || FontSizeCombo.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(item.Tag as string, out var size))
        {
            return;
        }

        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        Editor.Focus();
        ScheduleSave();
    }

    private void FontColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isLoading || FontColorCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string colorText)
        {
            return;
        }

        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorText)!;
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        Editor.Focus();
        ScheduleSave();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string themeId)
        {
            return;
        }

        ApplyTheme(themeId);
        _state.ThemeId = themeId;
        ScheduleSave();
    }

    private void ApplyTheme(string? themeId)
    {
        var theme = NoteTheme.Find(themeId);
        _state.ThemeId = theme.Id;

        Paper.PaperColor = theme.Paper;
        Paper.RuleColor = theme.Rule;
        Paper.MarginColor = theme.Margin;
        ShellBorder.Background = new SolidColorBrush(theme.Paper);
        ShellBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(95, theme.Accent.R, theme.Accent.G, theme.Accent.B));
        TitleBar.Background = new SolidColorBrush(theme.Chrome);
        var bandColor = Blend(theme.Paper, theme.Chrome, 0.48);
        FormattingBar.Background = new SolidColorBrush(bandColor);
        SettingsBar.Background = new SolidColorBrush(bandColor);

        foreach (var themeButton in GetThemeButtons())
        {
            themeButton.IsChecked = string.Equals(themeButton.Tag as string, theme.Id, StringComparison.OrdinalIgnoreCase);
        }
    }

    private IEnumerable<ToggleButton> GetThemeButtons()
    {
        yield return ThemeLemon;
        yield return ThemeSakura;
        yield return ThemeMint;
        yield return ThemeSky;
        yield return ThemeIvory;
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_dataService.DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", _dataService.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            MessageBox.Show($"保存場所を開けませんでした。\n\n{ex.Message}", "Y-TEC 付箋", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholder();
        if (!_isLoading && !_isNormalizingDocument)
        {
            NormalizeDocumentLayout();
            ScheduleSave();
        }
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
        {
            UpdateFormattingButtons();
        }
    }

    private void Editor_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is ScrollViewer)
        {
            Paper.ScrollOffset = e.VerticalOffset;
        }
    }

    private void UpdatePlaceholder()
    {
        if (Editor?.Document is null || PlaceholderText is null)
        {
            return;
        }

        var text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
        PlaceholderText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateFormattingButtons()
    {
        BoldButton.IsChecked = IsSelectionValue(TextElement.FontWeightProperty, FontWeights.Bold);
        ItalicButton.IsChecked = IsSelectionValue(TextElement.FontStyleProperty, FontStyles.Italic);

        var propertyValue = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        var decorations = propertyValue as TextDecorationCollection;
        UnderlineButton.IsChecked = decorations?.Any(item => item.Location == TextDecorationLocation.Underline) == true;
        StrikeButton.IsChecked = decorations?.Any(item => item.Location == TextDecorationLocation.Strikethrough) == true;
        CenterAlignButton.IsChecked = IsSelectionValue(Block.TextAlignmentProperty, TextAlignment.Center);
        BulletButton.IsChecked = Editor.Selection.Start.Paragraph?.Parent is ListItem;
    }

    private bool IsSelectionValue(DependencyProperty property, object expected)
    {
        var value = Editor.Selection.GetPropertyValue(property);
        return value != DependencyProperty.UnsetValue && Equals(value, expected);
    }

    private void LoadInstalledFonts()
    {
        var fonts = FontCatalog.GetInstalledFonts();
        FontFamilyCombo.ItemsSource = fonts;
        FontFamilyCombo.SelectedItem = fonts.FirstOrDefault(font =>
            string.Equals(font.FamilyName, "Yu Gothic UI", StringComparison.OrdinalIgnoreCase)) ??
            fonts.FirstOrDefault(font => string.Equals(font.FamilyName, "Meiryo", StringComparison.OrdinalIgnoreCase)) ??
            fonts.FirstOrDefault();
    }

    private void NormalizeDocumentLayout()
    {
        if (_isNormalizingDocument)
        {
            return;
        }

        _isNormalizingDocument = true;
        try
        {
            Editor.Document.PagePadding = NotePagePadding;
            Editor.Document.LineHeight = NoteLineHeight;
            Editor.Document.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            NormalizeBlocks(Editor.Document.Blocks);
        }
        finally
        {
            _isNormalizingDocument = false;
        }
    }

    private static void NormalizeBlocks(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            block.LineHeight = NoteLineHeight;
            block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            block.Margin = new Thickness(0);
            block.Padding = new Thickness(0);

            if (block is Paragraph paragraph)
            {
                paragraph.TextIndent = 0;
            }
            else if (block is Section section)
            {
                NormalizeBlocks(section.Blocks);
            }
            else if (block is List list)
            {
                list.Margin = new Thickness(14, 0, 0, 0);
                list.MarkerOffset = 12;
                foreach (var item in list.ListItems)
                {
                    NormalizeBlocks(item.Blocks);
                }
            }
        }
    }

    private void LoadEditorContent(string rtfBase64)
    {
        if (string.IsNullOrWhiteSpace(rtfBase64))
        {
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(rtfBase64);
            using var stream = new MemoryStream(bytes);
            new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Load(stream, DataFormats.Rtf);
            NormalizeDocumentLayout();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or IOException)
        {
            ShowStatus("本文を復元できません", sticky: true);
        }
    }

    private void ScheduleSave()
    {
        if (_isLoading || _isClosing || _savingBlocked)
        {
            return;
        }

        SaveStatusText.Text = "保存中…";
        _statusTimer.Stop();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private bool TrySave(bool showErrorDialog)
    {
        if (_savingBlocked)
        {
            return true;
        }

        try
        {
            CaptureState();
            _dataService.Save(_state);
            ShowStatus("保存済み");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowStatus("保存できません", sticky: true);
            if (showErrorDialog)
            {
                MessageBox.Show(
                    $"付箋を保存できないため、アプリを閉じませんでした。\n保存先: {_dataService.StateFilePath}\n\n{ex.Message}",
                    "Y-TEC 付箋",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
    }

    private void CaptureState()
    {
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Rtf);
        _state.RichTextRtfBase64 = Convert.ToBase64String(stream.ToArray());
        _state.PlainText = range.Text.TrimEnd('\r', '\n');
        _state.AlwaysOnTop = Topmost;

        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _state.Window.Left = bounds.Left;
        _state.Window.Top = bounds.Top;
        _state.Window.Width = bounds.Width;
        _state.Window.Height = bounds.Height;
    }

    private void ShowStatus(string message, bool sticky = false)
    {
        SaveStatusText.Text = message;
        _statusTimer.Stop();
        if (!sticky && message != "保存済み")
        {
            _statusTimer.Start();
        }
    }

    private static Color Blend(Color paper, Color chrome, double chromeWeight)
    {
        var paperWeight = 1 - chromeWeight;
        return Color.FromRgb(
            (byte)Math.Round((paper.R * paperWeight) + (chrome.R * chromeWeight)),
            (byte)Math.Round((paper.G * paperWeight) + (chrome.G * chromeWeight)),
            (byte)Math.Round((paper.B * paperWeight) + (chrome.B * chromeWeight)));
    }
}
