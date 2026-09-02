using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace YtecStickyNote;

public partial class App : WpfApplication
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private MainWindow? _mainWindow;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Icon? _trayDrawingIcon;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"予期しないエラーが発生しました。\n\n{args.Exception.Message}",
                "罫彩",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        if (AppRuntimeOptions.ShouldWaitForStartupData)
        {
            var ready = await Services.StartupDataAvailability.WaitUntilReadyAsync(
                AppRuntimeOptions.StartupDataRoot!,
                AppRuntimeOptions.StartupWaitTimeout);
            if (!ready)
            {
                Shutdown();
                return;
            }
        }

        var mutexName = AppRuntimeOptions.IsTestMode
            ? $"Local\\YTEC-Sticky-Note-TestMode-{Environment.ProcessId}"
            : "Local\\YTEC-Sticky-Note-SingleInstance";
        _singleInstanceMutex = new Mutex(true, mutexName, out var isFirstInstance);
        _ownsSingleInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "罫彩はすでに起動しています。\nタスクトレイのアイコンから表示できます。",
                "罫彩",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        SessionEnding += App_SessionEnding;
        _mainWindow.Show();
        try
        {
            InitializeTrayIcon();
        }
        catch (Exception ex)
        {
            _mainWindow.EnableTaskbarFallback();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MessageBox.Show(
                $"タスクトレイを準備できなかったため、タスクバーへ表示します。\n\n{ex.Message}",
                "罫彩",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void InitializeTrayIcon()
    {
        _trayDrawingIcon = LoadTrayIcon();

        var showItem = new Forms.ToolStripMenuItem("罫彩を表示");
        showItem.Click += (_, _) => Dispatcher.Invoke(ShowMainWindow);

        var hideItem = new Forms.ToolStripMenuItem("罫彩を隠す");
        hideItem.Click += (_, _) => Dispatcher.Invoke(() => _mainWindow?.HideToTray());

        var exitItem = new Forms.ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => Dispatcher.Invoke(ExitApplication);

        _trayMenu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        _trayMenu.Items.Add(showItem);
        _trayMenu.Items.Add(hideItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);
        _trayMenu.Opening += (_, _) =>
        {
            var visible = _mainWindow?.IsVisible == true;
            showItem.Enabled = !visible;
            hideItem.Enabled = visible;
        };

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayDrawingIcon,
            Text = "罫彩",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.MouseDoubleClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(ShowMainWindow);
            }
        };
    }

    private static Icon LoadTrayIcon()
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "YTEC-Sticky-Note.exe");
        return Icon.ExtractAssociatedIcon(executablePath) ?? (Icon)SystemIcons.Application.Clone();
    }

    private void ShowMainWindow() => _mainWindow?.RestoreFromTray();

    private void ExitApplication()
    {
        if (_mainWindow is not null && !_mainWindow.TryExitApplication())
        {
            return;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
        }
        Shutdown();
    }

    private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (_mainWindow is not null && !_mainWindow.TryExitApplication())
        {
            e.Cancel = true;
            return;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SessionEnding -= App_SessionEnding;
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _trayMenu?.Dispose();
        _trayDrawingIcon?.Dispose();

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
