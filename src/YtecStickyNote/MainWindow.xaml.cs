using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using YtecStickyNote.Models;
using YtecStickyNote.Services;

namespace YtecStickyNote;

public partial class MainWindow : Window
{
    private const int WmDisplayChange = 0x007E;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmDpiChanged = 0x02E0;
    private const double NoteLineHeight = 30;
    private static readonly Thickness NotePagePadding = new(64, 10, 22, 24);
    private enum BulletState
    {
        Off,
        On,
        Mixed
    }

    private sealed record SearchMatch(TextPointer Start, TextPointer End);

    private readonly AppRuntimeProfile _runtimeProfile;
    private readonly PortableDataService _dataService;
    private readonly WindowProfileService _windowProfileService;
    private readonly IStartupController _startupController;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _displayStabilizationTimer;
    private readonly WindowLayoutSession _windowLayoutSession = new();
    private readonly List<SearchMatch> _searchMatches = new();
    private AppState _state = new();
    private bool _isLoading = true;
    private bool _isClosing;
    private bool _allowApplicationExit;
    private bool _trayAvailable = true;
    private bool _savingBlocked;
    private bool _isUpdatingFormattingControls;
    private bool _isUpdatingAutoStartControl;
    private bool _isSwitchingPage;
    private bool _documentRestoreFailed;
    private bool _isRestoringWindowPlacement;
    private bool _displaySettingsSubscribed;
    private bool _windowPlacementSavingBlocked;
    private bool _isUserMovingOrResizing;
    private int _searchMatchIndex = -1;
    private HwndSource? _windowSource;
    private WindowState _windowStateBeforeTray = WindowState.Normal;

    private bool CanMutatePersistedState => !_isLoading && !_savingBlocked && !_documentRestoreFailed;

    public MainWindow()
    {
        _runtimeProfile = AppRuntimeProfile.Detect();
        _dataService = new PortableDataService(_runtimeProfile.StorageBaseDirectory);
        _windowProfileService = new WindowProfileService(_runtimeProfile.StorageBaseDirectory);
        _startupController = _runtimeProfile.StartupBackend == StartupBackend.PackagedStartupTask
            ? new PackagedStartupController()
            : new PortableStartupController(new StartupService(sourceApplicationDirectory: _runtimeProfile.StorageBaseDirectory));

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

        _displayStabilizationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _displayStabilizationTimer.Tick += DisplayStabilizationTimer_Tick;

        Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Editor_ScrollChanged));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var loaded = _dataService.Load();
        _state = loaded.State;
        _savingBlocked = !loaded.CanSave;

        var initialLayoutId = MonitorLayoutService.GetCurrentLayoutId();
        var windowProfile = LoadAndApplyWindowProfile(initialLayoutId);
        _windowLayoutSession.Initialize(initialLayoutId, windowProfile.NeedsSave);

        LoadInstalledFonts();
        _state.NormalizePages();
        ApplyTheme(CurrentPage.ThemeId);
        LoadEditorContent(CurrentPage);

        Topmost = _state.AlwaysOnTop;
        TopmostButton.IsChecked = Topmost;

        UpdatePlaceholder();
        UpdateFormattingButtons();
        UpdateUndoRedoControls();
        UpdatePageNavigationControls();
        _isLoading = false;
        UpdateMutationAvailability();

        var testMode = AppRuntimeOptions.IsTestMode;
        if (!testMode)
        {
            try
            {
                var startupStatus = await _startupController.GetStatusAsync();
                if (startupStatus == StartupRegistrationStatus.NeedsSecurityUpgrade)
                {
                    var response = MessageBox.Show(
                        "以前の自動起動登録を、ウイルス対策ソフトと共存しやすい1.6.0方式へ更新する必要があります。\n\n" +
                        "［はい］: 署名済みの罫彩本体をWindowsのローカル領域へコピーし、Google Drive上の保存データが準備できてから開く方式へ更新します。\n" +
                        "［いいえ］: 古い自動起動登録を解除します。\n\n" +
                        "安全のため、古い方式のまま残すことはできません。",
                        "罫彩 - 自動起動の安全性更新",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.Yes);
                    await _startupController.SetEnabledAsync(response == MessageBoxResult.Yes);
                    startupStatus = await _startupController.GetStatusAsync();
                    ShowStatus(
                        startupStatus == StartupRegistrationStatus.Enabled
                            ? "自動起動を安全な方式へ更新しました"
                            : "古い自動起動登録を解除しました",
                        sticky: true);
                }

                SetAutoStartCheckState(startupStatus == StartupRegistrationStatus.Enabled);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or
                SecurityException or COMException or System.Text.Json.JsonException)
            {
                SetAutoStartCheckState(false);
                ShowStatus("自動起動状態を確認できません", sticky: true);
            }
        }
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        _displaySettingsSubscribed = true;
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(WindowMessageHook);
        if (!string.IsNullOrWhiteSpace(loaded.Warning))
        {
            ShowStatus("読込エラー", sticky: true);
            Editor.IsReadOnly = true;
            MessageBox.Show(loaded.Warning, "罫彩", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (_documentRestoreFailed)
        {
            ShowStatus("本文を復元できません", sticky: true);
            Editor.IsReadOnly = true;
            MessageBox.Show(
                "本文データを復元できなかったため、安全のため編集と保存を停止しました。元の保存ファイルは変更していません。",
                "罫彩",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else if (!string.IsNullOrWhiteSpace(windowProfile.Warning))
        {
            ShowStatus("位置設定エラー", sticky: true);
            MessageBox.Show(windowProfile.Warning, "罫彩", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            ShowStatus(testMode ? "検証モード" : "保存済み", sticky: testMode);
        }

        if (!loaded.FileExisted || windowProfile.NeedsSave)
        {
            ScheduleSave();
        }

        Editor.Focus();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _displayStabilizationTimer.Stop();
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;

        if (_displaySettingsSubscribed)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            _displaySettingsSubscribed = false;
        }
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(BeginDisplayTransition);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case WmEnterSizeMove:
                _isUserMovingOrResizing = true;
                break;
            case WmExitSizeMove:
                if (_isUserMovingOrResizing)
                {
                    _isUserMovingOrResizing = false;
                    Dispatcher.BeginInvoke(MarkUserPlacementChanged);
                }
                break;
            case WmDisplayChange:
            case WmDpiChanged when !_isUserMovingOrResizing:
                Dispatcher.BeginInvoke(BeginDisplayTransition);
                break;
        }

        return IntPtr.Zero;
    }

    private void BeginDisplayTransition()
    {
        if (_isClosing || _isLoading)
        {
            return;
        }

        _windowLayoutSession.BeginDisplayTransition();
        _displayStabilizationTimer.Stop();
        _displayStabilizationTimer.Start();
    }

    private void DisplayStabilizationTimer_Tick(object? sender, EventArgs e)
    {
        var stableLayoutId = _windowLayoutSession.ObserveDisplayLayout(MonitorLayoutService.GetCurrentLayoutId());
        if (stableLayoutId is null)
        {
            return;
        }

        _displayStabilizationTimer.Stop();
        ApplyStableWindowLayout(stableLayoutId, scheduleSave: true);
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if (e.Key == Key.F && modifiers == ModifierKeys.Control)
        {
            OpenSearchPanel();
            e.Handled = true;
            return;
        }

        if (SearchPanel.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CloseSearchPanel();
            e.Handled = true;
            return;
        }

        if (SearchPanel.Visibility == Visibility.Visible && e.Key == Key.F3)
        {
            MoveSearchResult((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (Editor.IsKeyboardFocusWithin &&
            e.Key == Key.V &&
            modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            PastePlainText();
            e.Handled = true;
            return;
        }

        if (CanMutatePersistedState && Editor.IsKeyboardFocusWithin && e.Key == Key.Enter && modifiers == ModifierKeys.Shift)
        {
            EditingCommands.EnterLineBreak.Execute(null, Editor);
            e.Handled = true;
        }
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!CanMutatePersistedState || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var handled = e.Key switch
        {
            Key.Enter => RichTextListEditing.TryExitEmptyListItem(Editor),
            Key.Back => RichTextListEditing.TryRemoveListMarkerAtItemStart(Editor),
            _ => false
        };
        if (!handled)
        {
            return;
        }

        e.Handled = true;
        UpdateFormattingButtons();
        UpdateUndoRedoControls();
        ScheduleSave();
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
        RestoreCurrentWindowLayout();
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
        if (!CanMutatePersistedState)
        {
            return;
        }

        Topmost = TopmostButton.IsChecked == true;
        _state.AlwaysOnTop = Topmost;
        ScheduleSave();
    }

    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutatePersistedState)
        {
            return;
        }

        EditingCommands.ToggleBold.Execute(null, Editor);
        Editor.Focus();
        ScheduleSave();
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutatePersistedState)
        {
            return;
        }

        EditingCommands.ToggleItalic.Execute(null, Editor);
        Editor.Focus();
        ScheduleSave();
    }

    private void UnderlineButton_Click(object sender, RoutedEventArgs e) => ToggleDecoration(TextDecorationLocation.Underline);

    private void StrikeButton_Click(object sender, RoutedEventArgs e) => ToggleDecoration(TextDecorationLocation.Strikethrough);

    private void CenterAlignButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutatePersistedState)
        {
            return;
        }

        var isCentered = IsSelectionValue(Block.TextAlignmentProperty, TextAlignment.Center);
        Editor.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, isCentered ? TextAlignment.Left : TextAlignment.Center);
        Editor.Focus();
        UpdateFormattingButtons();
        ScheduleSave();
    }

    private void BulletButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutatePersistedState)
        {
            return;
        }

        var state = GetBulletState();
        var selectionStart = Editor.Selection.Start;
        var selectionEnd = Editor.Selection.End;

        Editor.BeginChange();
        try
        {
            if (state == BulletState.On)
            {
                RemoveBulletsFromSelectedParagraphs();
            }
            else
            {
                ApplyDiscBulletsToUnbulletedParagraphs();
            }

            Editor.Selection.Select(selectionStart, selectionEnd);
            ConfigureSelectedBulletLists();
        }
        finally
        {
            Editor.EndChange();
        }

        Editor.Focus();
        UpdateFormattingButtons();
        UpdateUndoRedoControls();
        ScheduleSave();
    }

    private void ApplyDiscBulletsToUnbulletedParagraphs()
    {
        var selectedParagraphs = GetSelectedParagraphs();
        if (selectedParagraphs.Count > 0 && selectedParagraphs.All(paragraph => GetContainingList(paragraph) is null))
        {
            Editor.Selection.Select(selectedParagraphs[0].ContentStart, selectedParagraphs[^1].ContentEnd);
            EditingCommands.ToggleBullets.Execute(null, Editor);
            return;
        }

        var paragraphs = selectedParagraphs
            .Where(paragraph => !IsParagraphBulleted(paragraph))
            .ToList();

        foreach (var paragraph in paragraphs)
        {
            if (IsParagraphBulleted(paragraph))
            {
                continue;
            }

            Editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
            EditingCommands.ToggleBullets.Execute(null, Editor);
        }
    }

    private void RemoveBulletsFromSelectedParagraphs()
    {
        var paragraphs = GetSelectedParagraphs()
            .Where(IsParagraphBulleted)
            .OrderByDescending(GetListDepth)
            .ToList();

        foreach (var paragraph in paragraphs)
        {
            if (!IsParagraphBulleted(paragraph))
            {
                continue;
            }

            Editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
            EditingCommands.ToggleBullets.Execute(null, Editor);
        }
    }

    private void ConfigureSelectedBulletLists()
    {
        foreach (var list in GetSelectedParagraphs()
                     .Select(GetContainingList)
                     .Where(list => list is not null)
                     .Cast<List>()
                     .Distinct())
        {
            if (!IsUnorderedList(list))
            {
                continue;
            }

            list.MarkerStyle = TextMarkerStyle.Disc;
            list.Margin = new Thickness(14, 0, 0, 0);
            list.MarkerOffset = 12;
        }
    }

    private void ToggleDecoration(TextDecorationLocation location)
    {
        if (!CanMutatePersistedState)
        {
            return;
        }

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

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutatePersistedState)
        {
            return;
        }

        if (Editor.CanUndo)
        {
            Editor.Undo();
        }

        Editor.Focus();
        UpdateFormattingButtons();
        UpdateUndoRedoControls();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutatePersistedState)
        {
            return;
        }

        if (Editor.CanRedo)
        {
            Editor.Redo();
        }

        Editor.Focus();
        UpdateFormattingButtons();
        UpdateUndoRedoControls();
    }

    private void ClearFormattingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutatePersistedState || Editor.Selection.IsEmpty)
        {
            Editor.Focus();
            return;
        }

        Editor.BeginChange();
        try
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, Editor.FontFamily);
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, Editor.FontSize);
            Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, Editor.FontWeight);
            Editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, Editor.FontStyle);
            Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Editor.Foreground);
            Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        }
        finally
        {
            Editor.EndChange();
        }

        Editor.Focus();
        UpdateFormattingButtons();
        UpdateUndoRedoControls();
        ScheduleSave();
    }

    private void PastePlainTextMenuItem_Click(object sender, RoutedEventArgs e) => PastePlainText();

    private void PastePlainText()
    {
        if (Editor.IsReadOnly || !Clipboard.ContainsText(TextDataFormat.UnicodeText))
        {
            return;
        }

        var text = Clipboard.GetText(TextDataFormat.UnicodeText);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Editor.BeginChange();
        try
        {
            Editor.Selection.Text = text;
        }
        finally
        {
            Editor.EndChange();
        }

        Editor.Focus();
        UpdateFormattingButtons();
        UpdateUndoRedoControls();
        ScheduleSave();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => OpenSearchPanel();

    private void CloseSearchButton_Click(object sender, RoutedEventArgs e) => CloseSearchPanel();

    private void SearchPreviousButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSearchResult(-1);
        SearchBox.Focus();
    }

    private void SearchNextButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSearchResult(1);
        SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && SearchPanel.Visibility == Visibility.Visible)
        {
            RefreshSearchResults(selectFirst: true);
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.F3))
        {
            return;
        }

        MoveSearchResult((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1);
        e.Handled = true;
    }

    private void OpenSearchPanel()
    {
        SearchPanel.Visibility = Visibility.Visible;
        RefreshSearchResults(selectFirst: true);
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void CloseSearchPanel()
    {
        SearchPanel.Visibility = Visibility.Collapsed;
        Editor.Focus();
    }

    private void RefreshSearchResults(bool selectFirst)
    {
        _searchMatches.Clear();
        _searchMatches.AddRange(FindSearchMatches(SearchBox.Text));

        if (_searchMatches.Count == 0)
        {
            _searchMatchIndex = -1;
            SearchResultText.Text = "0 / 0";
            return;
        }

        if (selectFirst || _searchMatchIndex < 0 || _searchMatchIndex >= _searchMatches.Count)
        {
            _searchMatchIndex = 0;
        }

        SelectSearchResult(_searchMatchIndex);
    }

    private void MoveSearchResult(int direction)
    {
        if (_searchMatches.Count == 0)
        {
            RefreshSearchResults(selectFirst: true);
        }

        if (_searchMatches.Count == 0)
        {
            return;
        }

        _searchMatchIndex = _searchMatchIndex < 0
            ? direction < 0 ? _searchMatches.Count - 1 : 0
            : (_searchMatchIndex + direction + _searchMatches.Count) % _searchMatches.Count;
        SelectSearchResult(_searchMatchIndex);
    }

    private void SelectSearchResult(int index)
    {
        var match = _searchMatches[index];
        Editor.Selection.Select(match.Start, match.End);
        match.Start.Paragraph?.BringIntoView();
        SearchResultText.Text = $"{index + 1} / {_searchMatches.Count}";
    }

    private List<SearchMatch> FindSearchMatches(string query)
    {
        var matches = new List<SearchMatch>();
        if (string.IsNullOrEmpty(query))
        {
            return matches;
        }

        var text = new StringBuilder();
        var starts = new List<TextPointer?>();
        var ends = new List<TextPointer?>();
        foreach (var paragraph in EnumerateParagraphs(Editor.Document.Blocks))
        {
            AppendSearchableInlines(paragraph.Inlines, text, starts, ends);
            AppendSearchSeparator(text, starts, ends);
        }

        var source = text.ToString();
        var offset = 0;
        while (offset <= source.Length - query.Length)
        {
            var matchIndex = source.IndexOf(query, offset, StringComparison.CurrentCultureIgnoreCase);
            if (matchIndex < 0)
            {
                break;
            }

            var start = starts[matchIndex];
            var end = ends[matchIndex + query.Length - 1];
            if (start is not null && end is not null)
            {
                matches.Add(new SearchMatch(start, end));
            }

            offset = matchIndex + 1;
        }

        return matches;
    }

    private static void AppendSearchableInlines(
        InlineCollection inlines,
        StringBuilder text,
        ICollection<TextPointer?> starts,
        ICollection<TextPointer?> ends)
    {
        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    var runText = run.Text ?? string.Empty;
                    for (var index = 0; index < runText.Length; index++)
                    {
                        text.Append(runText[index]);
                        starts.Add(run.ContentStart.GetPositionAtOffset(index, LogicalDirection.Forward));
                        ends.Add(run.ContentStart.GetPositionAtOffset(index + 1, LogicalDirection.Forward));
                    }
                    break;
                case Span span:
                    AppendSearchableInlines(span.Inlines, text, starts, ends);
                    break;
                default:
                    AppendSearchSeparator(text, starts, ends);
                    break;
            }
        }
    }

    private static void AppendSearchSeparator(
        StringBuilder text,
        ICollection<TextPointer?> starts,
        ICollection<TextPointer?> ends)
    {
        text.Append('\n');
        starts.Add(null);
        ends.Add(null);
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !CanMutatePersistedState || _isUpdatingFormattingControls || FontFamilyCombo.SelectedItem is not FontChoice font)
        {
            return;
        }

        Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(font.FamilyName));
        Editor.Focus();
        UpdateFormattingButtons();
        ScheduleSave();
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !CanMutatePersistedState || _isUpdatingFormattingControls || FontSizeCombo.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(item.Tag as string, out var size))
        {
            return;
        }

        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        Editor.Focus();
        UpdateFormattingButtons();
        ScheduleSave();
    }

    private void FontColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !CanMutatePersistedState || _isUpdatingFormattingControls ||
            FontColorCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string colorText)
        {
            return;
        }

        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorText)!;
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        Editor.Focus();
        UpdateFormattingButtons();
        ScheduleSave();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutatePersistedState || _isSwitchingPage || sender is not ToggleButton button || button.Tag is not string themeId)
        {
            return;
        }

        ApplyTheme(themeId);
        CurrentPage.ThemeId = NoteTheme.Find(themeId).Id;
        ScheduleSave();
    }

    private void ApplyTheme(string? themeId)
    {
        var theme = NoteTheme.Find(themeId);

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
        yield return ThemeLavender;
        yield return ThemePeach;
        yield return ThemeAqua;
        yield return ThemeGray;
        yield return ThemeMocha;
    }

    private NotePageState CurrentPage => _state.GetCurrentPage();

    private void PreviousPageButton_Click(object sender, RoutedEventArgs e) => MoveToAdjacentPage(-1);

    private void NextPageButton_Click(object sender, RoutedEventArgs e) => MoveToAdjacentPage(1);

    private void AddPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutatePersistedState || _state.Pages.Count >= AppState.MaximumPageCount)
        {
            return;
        }

        CaptureCurrentPage();
        var insertionIndex = _state.GetCurrentPageIndex() + 1;
        var addedPage = new NotePageState
        {
            ThemeId = CurrentPage.ThemeId
        };
        _state.Pages.Insert(insertionIndex, addedPage);
        _state.CurrentPageId = addedPage.Id;
        RestoreCurrentPageAfterSwitch();
        ScheduleSave();
    }

    private void DeletePageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutatePersistedState || _state.Pages.Count <= 1)
        {
            return;
        }

        var confirmed = AppRuntimeOptions.IsTestMode ||
            MessageBox.Show(
                "現在のページを削除します。本文と書式は元に戻せません。\n\n削除してよろしいですか？",
                "罫彩 - ページを削除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        CaptureCurrentPage();
        var removedIndex = _state.GetCurrentPageIndex();
        _state.Pages.RemoveAt(removedIndex);
        var nextIndex = Math.Min(removedIndex, _state.Pages.Count - 1);
        _state.CurrentPageId = _state.Pages[nextIndex].Id;
        RestoreCurrentPageAfterSwitch();
        ScheduleSave();
    }

    private void MoveToAdjacentPage(int offset)
    {
        if (!CanMutatePersistedState)
        {
            return;
        }

        var destinationIndex = _state.GetCurrentPageIndex() + offset;
        if (destinationIndex < 0 || destinationIndex >= _state.Pages.Count)
        {
            return;
        }

        CaptureCurrentPage();
        _state.CurrentPageId = _state.Pages[destinationIndex].Id;
        RestoreCurrentPageAfterSwitch();
        ScheduleSave();
    }

    private void RestoreCurrentPageAfterSwitch()
    {
        _isSwitchingPage = true;
        try
        {
            ApplyTheme(CurrentPage.ThemeId);
            LoadEditorContent(CurrentPage);
            ClearUndoHistory();
        }
        finally
        {
            _isSwitchingPage = false;
        }

        UpdatePlaceholder();
        UpdateFormattingButtons();
        UpdateUndoRedoControls();
        UpdatePageNavigationControls();
        UpdateMutationAvailability();
        if (SearchPanel.Visibility == Visibility.Visible)
        {
            RefreshSearchResults(selectFirst: true);
        }

        if (_documentRestoreFailed)
        {
            ShowStatus("本文を復元できません", sticky: true);
        }

        Editor.Focus();
    }

    private void ClearUndoHistory()
    {
        Editor.UndoLimit = 0;
        Editor.UndoLimit = -1;
    }

    private void UpdatePageNavigationControls()
    {
        _state.NormalizePages();
        var currentIndex = _state.GetCurrentPageIndex();
        var pageCount = _state.Pages.Count;
        var canNavigate = CanMutatePersistedState;
        var canModifyPages = CanMutatePersistedState;

        PreviousPageButton.IsEnabled = canNavigate && currentIndex > 0;
        NextPageButton.IsEnabled = canNavigate && currentIndex < pageCount - 1;
        AddPageButton.IsEnabled = canModifyPages && pageCount < AppState.MaximumPageCount;
        DeletePageButton.IsEnabled = canModifyPages && pageCount > 1;
        PageIndicatorText.Text = $"{currentIndex + 1} / {pageCount}";
    }

    private void UpdateMutationAvailability()
    {
        var canMutate = CanMutatePersistedState;
        Editor.IsReadOnly = !canMutate;
        foreach (var control in new Control[]
                 {
                     BoldButton, ItalicButton, UnderlineButton, StrikeButton, CenterAlignButton, BulletButton,
                     ClearFormattingButton, FontFamilyCombo, FontSizeCombo, FontColorCombo, TopmostButton
                 })
        {
            control.IsEnabled = canMutate;
        }

        foreach (var themeButton in GetThemeButtons())
        {
            themeButton.IsEnabled = canMutate;
        }

        PastePlainTextMenuItem.IsEnabled = canMutate;
        UpdateUndoRedoControls();
        UpdatePageNavigationControls();
    }

    private async void AutoStartCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isUpdatingAutoStartControl || AppRuntimeOptions.IsTestMode)
        {
            return;
        }

        var enabled = AutoStartCheck.IsChecked == true;
        if (enabled)
        {
            var confirmationText = _runtimeProfile.StartupBackend == StartupBackend.PackagedStartupTask
                ? "Windowsサインイン時の自動起動を有効にします。\n\n" +
                  "Microsoft Store版のWindowsスタートアップ設定へ罫彩を登録します。\n\n" +
                  "登録してよろしいですか？"
                : "Windowsサインイン時の自動起動を有効にします。\n\n" +
                  "現在のWindowsユーザーの自動起動設定へ、ローカルにコピーした署名済みの罫彩本体を登録します。" +
                  "Google Drive上の保存データを読み書きできる状態になるまで最大10分待機します。\n\n" +
                  "登録してよろしいですか？";
            var confirmed = MessageBox.Show(
                confirmationText,
                "罫彩 - 自動起動の確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmed != MessageBoxResult.Yes)
            {
                SetAutoStartCheckState(false);
                return;
            }
        }

        try
        {
            AutoStartCheck.IsEnabled = false;
            await _startupController.SetEnabledAsync(enabled);
            SetAutoStartCheckState(await _startupController.IsEnabledAsync());
            ShowStatus(enabled ? "自動起動を登録しました" : "自動起動を解除しました");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or
            SecurityException or COMException or System.Text.Json.JsonException)
        {
            SetAutoStartCheckState(!enabled);
            MessageBox.Show(
                $"自動起動の設定を変更できませんでした。\n通常の起動時には登録処理を行いません。\n\n{ex.Message}",
                "罫彩",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            AutoStartCheck.IsEnabled = true;
        }
    }

    private void SetAutoStartCheckState(bool enabled)
    {
        _isUpdatingAutoStartControl = true;
        try
        {
            AutoStartCheck.IsChecked = enabled;
        }
        finally
        {
            _isUpdatingAutoStartControl = false;
        }
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
            MessageBox.Show($"保存場所を開けませんでした。\n\n{ex.Message}", "罫彩", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholder();
        if (!_isLoading && !_isSwitchingPage)
        {
            UpdateFormattingButtons();
            UpdateUndoRedoControls();
            if (SearchPanel.Visibility == Visibility.Visible)
            {
                RefreshSearchResults(selectFirst: true);
            }

            ScheduleSave();
        }
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoading && !_isSwitchingPage)
        {
            UpdateFormattingButtons();
            UpdateUndoRedoControls();
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
        _isUpdatingFormattingControls = true;
        try
        {
            BoldButton.IsChecked = IsSelectionValue(TextElement.FontWeightProperty, FontWeights.Bold);
            ItalicButton.IsChecked = IsSelectionValue(TextElement.FontStyleProperty, FontStyles.Italic);

            var propertyValue = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            var decorations = propertyValue as TextDecorationCollection;
            UnderlineButton.IsChecked = decorations?.Any(item => item.Location == TextDecorationLocation.Underline) == true;
            StrikeButton.IsChecked = decorations?.Any(item => item.Location == TextDecorationLocation.Strikethrough) == true;
            CenterAlignButton.IsChecked = IsSelectionValue(Block.TextAlignmentProperty, TextAlignment.Center);
            BulletButton.IsChecked = GetBulletState() switch
            {
                BulletState.On => true,
                BulletState.Mixed => null,
                _ => false
            };

            UpdateFontFamilySelection();
            UpdateTaggedComboSelection(FontSizeCombo, GetSelectionFontSize());
            UpdateTaggedComboSelection(FontColorCombo, GetSelectionColor());
        }
        finally
        {
            _isUpdatingFormattingControls = false;
        }
    }

    private void UpdateUndoRedoControls()
    {
        var canMutate = CanMutatePersistedState;
        UndoButton.IsEnabled = canMutate && Editor.CanUndo;
        RedoButton.IsEnabled = canMutate && Editor.CanRedo;
        UndoMenuItem.IsEnabled = canMutate && Editor.CanUndo;
        RedoMenuItem.IsEnabled = canMutate && Editor.CanRedo;
    }

    private void UpdateFontFamilySelection()
    {
        var value = Editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty);
        var familyName = value is FontFamily family ? family.Source : null;
        var match = (FontFamilyCombo.ItemsSource as IEnumerable<FontChoice>)?.FirstOrDefault(font =>
            string.Equals(font.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));
        FontFamilyCombo.SelectedItem = match;
        if (match is null)
        {
            FontFamilyCombo.Text = string.Empty;
        }
    }

    private string? GetSelectionFontSize()
    {
        var value = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
        return value is double size ? size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private string? GetSelectionColor()
    {
        var value = Editor.Selection.GetPropertyValue(TextElement.ForegroundProperty);
        return value is SolidColorBrush brush
            ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}"
            : null;
    }

    private static void UpdateTaggedComboSelection(ComboBox comboBox, string? tag)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            item.Tag is string itemTag && string.Equals(itemTag, tag, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSelectionValue(DependencyProperty property, object expected)
    {
        var value = Editor.Selection.GetPropertyValue(property);
        return value != DependencyProperty.UnsetValue && Equals(value, expected);
    }

    private BulletState GetBulletState()
    {
        var hasBulletedParagraph = false;
        var hasOtherParagraph = false;
        TextMarkerStyle? markerStyle = null;
        int? listDepth = null;

        foreach (var paragraph in GetSelectedParagraphs())
        {
            var list = GetContainingList(paragraph);
            if (list is not null && IsUnorderedList(list))
            {
                hasBulletedParagraph = true;
                var paragraphDepth = GetListDepth(paragraph);
                if (markerStyle is null)
                {
                    markerStyle = list.MarkerStyle;
                    listDepth = paragraphDepth;
                }
                else if (markerStyle != list.MarkerStyle || listDepth != paragraphDepth)
                {
                    return BulletState.Mixed;
                }
            }
            else
            {
                hasOtherParagraph = true;
            }

            if (hasBulletedParagraph && hasOtherParagraph)
            {
                return BulletState.Mixed;
            }
        }

        return hasBulletedParagraph ? BulletState.On : BulletState.Off;
    }

    private List<Paragraph> GetSelectedParagraphs()
    {
        var selection = Editor.Selection;
        var paragraphs = new List<Paragraph>();
        foreach (var paragraph in EnumerateParagraphs(Editor.Document.Blocks))
        {
            var isSelected = selection.IsEmpty
                ? paragraph == selection.Start.Paragraph
                : paragraph.ContentStart.CompareTo(selection.End) < 0 &&
                  paragraph.ContentEnd.CompareTo(selection.Start) > 0;
            if (isSelected)
            {
                paragraphs.Add(paragraph);
            }
        }

        AddParagraphIfMissing(paragraphs, selection.Start.Paragraph);
        AddParagraphIfMissing(paragraphs, selection.End.Paragraph);
        return paragraphs;
    }

    private static void AddParagraphIfMissing(ICollection<Paragraph> paragraphs, Paragraph? paragraph)
    {
        if (paragraph is not null && !paragraphs.Contains(paragraph))
        {
            paragraphs.Add(paragraph);
        }
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(BlockCollection blocks)
    {
        foreach (Block block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;
                case Section section:
                    foreach (var paragraph in EnumerateParagraphs(section.Blocks))
                    {
                        yield return paragraph;
                    }
                    break;
                case List list:
                    foreach (ListItem item in list.ListItems)
                    {
                        foreach (var paragraph in EnumerateParagraphs(item.Blocks))
                        {
                            yield return paragraph;
                        }
                    }
                    break;
            }
        }
    }

    private static bool IsParagraphBulleted(Paragraph paragraph) =>
        GetContainingList(paragraph) is { } list && IsUnorderedList(list);

    private static List? GetContainingList(Paragraph paragraph)
    {
        TextElement? element = paragraph;
        while (element?.Parent is TextElement parent)
        {
            if (parent is List list)
            {
                return list;
            }

            element = parent;
        }

        return null;
    }

    private static int GetListDepth(Paragraph paragraph)
    {
        var depth = 0;
        TextElement? element = paragraph;
        while (element?.Parent is TextElement parent)
        {
            if (parent is List)
            {
                depth++;
            }

            element = parent;
        }

        return depth;
    }

    private static bool IsUnorderedList(List list) =>
        list.MarkerStyle >= TextMarkerStyle.Disc && list.MarkerStyle <= TextMarkerStyle.Box;

    private void ApplyDocumentLineLayout()
    {
        Editor.Document.PagePadding = NotePagePadding;
        Editor.Document.LineHeight = NoteLineHeight;
        Editor.Document.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        ApplyBlockLineLayout(Editor.Document.Blocks);
    }

    private static void ApplyBlockLineLayout(BlockCollection blocks)
    {
        foreach (Block block in blocks)
        {
            block.LineHeight = NoteLineHeight;
            block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            block.Margin = new Thickness(0);
            block.Padding = new Thickness(0);

            switch (block)
            {
                case Paragraph paragraph:
                    paragraph.TextIndent = 0;
                    break;
                case Section section:
                    ApplyBlockLineLayout(section.Blocks);
                    break;
                case List list:
                    list.Margin = new Thickness(14, 0, 0, 0);
                    list.MarkerOffset = 12;
                    foreach (ListItem item in list.ListItems)
                    {
                        ApplyBlockLineLayout(item.Blocks);
                    }
                    break;
            }
        }
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

    private void LoadEditorContent(NotePageState page)
    {
        var result = DocumentPersistence.Restore(
            Editor.Document,
            page.RichTextXamlPackageBase64,
            page.RichTextRtfBase64);
        ApplyDocumentLineLayout();
        if (result == DocumentRestoreResult.Failed)
        {
            _documentRestoreFailed = true;
            _savingBlocked = true;
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
            var currentLayoutId = MonitorLayoutService.GetCurrentLayoutId();
            if (!_windowLayoutSession.IsDisplayTransition &&
                !string.Equals(currentLayoutId, _windowLayoutSession.LayoutId, StringComparison.Ordinal))
            {
                BeginDisplayTransition();
            }

            CaptureState();
            _dataService.Save(_state);
            if (!_windowPlacementSavingBlocked && _windowLayoutSession.CanSavePlacement(currentLayoutId))
            {
                _windowProfileService.Save(_windowLayoutSession.LayoutId, CaptureWindowPlacement());
                _windowLayoutSession.MarkPlacementSaved();
            }
            ShowStatus("保存済み");
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or System.Text.Json.JsonException)
        {
            ShowStatus("保存できません", sticky: true);
            if (showErrorDialog)
            {
                MessageBox.Show(
                    $"付箋またはウィンドウ位置を保存できないため、アプリを閉じませんでした。\n保存先: {_dataService.DataDirectory}\n\n{ex.Message}",
                    "罫彩",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
    }

    private void CaptureState()
    {
        CaptureCurrentPage();
        _state.MirrorCurrentPageToLegacyFields();
        _state.AlwaysOnTop = Topmost;
    }

    private void CaptureCurrentPage()
    {
        var document = DocumentPersistence.Capture(Editor.Document);
        var page = CurrentPage;
        page.RichTextXamlPackageBase64 = document.XamlPackageBase64;
        page.RichTextRtfBase64 = document.RtfBase64;
        page.PlainText = document.PlainText;
    }

    private WindowStateData CaptureWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return new WindowStateData
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height
        };
    }

    private WindowProfileLoadResult LoadAndApplyWindowProfile(string layoutId)
    {
        var result = _windowProfileService.LoadOrMigrate(layoutId, _state.Window);
        _windowPlacementSavingBlocked = !result.CanSave;
        var savedPlacement = result.Placement ?? new WindowStateData();
        var bounds = WindowPlacementService.GetRestoredBounds(savedPlacement);
        var previousState = WindowState;

        _isRestoringWindowPlacement = true;
        try
        {
            if (previousState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }

            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            WindowPlacementService.EnsureVisible(this);

            if (previousState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        finally
        {
            _isRestoringWindowPlacement = false;
        }

        return result;
    }

    private void ApplyStableWindowLayout(string layoutId, bool scheduleSave)
    {
        if (_isClosing)
        {
            return;
        }

        _saveTimer.Stop();
        var result = LoadAndApplyWindowProfile(layoutId);
        _windowLayoutSession.ApplyStableLayout(layoutId, result.NeedsSave);
        if (!string.IsNullOrWhiteSpace(result.Warning))
        {
            ShowStatus("位置設定エラー", sticky: true);
        }
        else if (scheduleSave && result.NeedsSave)
        {
            ScheduleSave();
        }
    }

    private void RestoreCurrentWindowLayout()
    {
        _displayStabilizationTimer.Stop();
        ApplyStableWindowLayout(MonitorLayoutService.GetCurrentLayoutId(), scheduleSave: true);
    }

    private void MarkUserPlacementChanged()
    {
        if (_isLoading || _isClosing || _isRestoringWindowPlacement || WindowState != WindowState.Normal)
        {
            return;
        }

        _windowLayoutSession.MarkUserPlacementChanged();
        if (_windowLayoutSession.PlacementDirty)
        {
            ScheduleSave();
        }
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
