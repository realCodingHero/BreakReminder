using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Drawing;
using BreakReminder.Models;
using BreakReminder.Services;
using BreakReminder.Views;
using H.NotifyIcon;

namespace BreakReminder;

/// <summary>
/// 应用程序入口 - 管理系统托盘生命周期和各服务的协调
/// </summary>
public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "BreakReminder_debug.log");

    private TaskbarIcon? _trayIcon;
    private InputMonitorService? _inputMonitor;
    private MediaPlaybackService? _mediaPlayback;
    private ActivityTracker? _activityTracker;
    private NotificationService? _notificationService;
    private SettingsService? _settingsService;
    private AutoStartService? _autoStartService;
    private AppSettings _settings = new();

    private MainWindow? _mainWindow;
    private CompactWindow? _compactWindow;
    private bool _isPaused;

    // 保持菜单项引用以便后续更新
    private MenuItem? _statusMenuItem;
    private MenuItem? _pauseMenuItem;

    private static void Log(string msg)
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            File.AppendAllText(LogFile, line + Environment.NewLine);
        }
        catch { /* ignore log errors */ }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常捕获
        DispatcherUnhandledException += (_, args) =>
        {
            Log($"UI EXCEPTION: {args.Exception.GetType().Name}: {args.Exception.Message}\n{args.Exception.StackTrace}");
            args.Handled = true; // 防止闪退，记录后继续
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log($"DOMAIN EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log($"TASK EXCEPTION: {args.Exception.GetType().Name}: {args.Exception.Message}\n{args.Exception.InnerException?.StackTrace}");
            args.SetObserved();
        };

        Log("=== OnStartup BEGIN ===");

        try
        {
            // 加载设置
            _settingsService = new SettingsService();
            _settings = _settingsService.Load();
            Log("Settings loaded.");

            // 初始化服务
            _inputMonitor = new InputMonitorService();
            _mediaPlayback = new MediaPlaybackService();
            _notificationService = new NotificationService(_settings);
            _autoStartService = new AutoStartService();
            Log("Services created.");

            // 初始化活动追踪器
            _activityTracker = new ActivityTracker(_inputMonitor, _mediaPlayback, _settings);
            _activityTracker.BreakTimeReached += OnBreakTimeReached;
            _activityTracker.StatusChanged += OnStatusChanged;
            Log("ActivityTracker created.");

            // 初始化通知服务事件
            _notificationService.Dismissed += OnReminderDismissed;
            _notificationService.Snoozed += OnReminderSnoozed;

            // 在代码中创建系统托盘图标
            Log("Creating tray icon...");
            CreateTrayIcon();
            Log("Tray icon created.");

            _notificationService.SetTaskbarIcon(_trayIcon);

            // 初始化媒体播放检测
            try
            {
                await _mediaPlayback.InitializeAsync();
                Log("MediaPlayback initialized.");
            }
            catch (Exception ex)
            {
                Log($"MediaPlayback init failed: {ex.GetType().Name}: {ex.Message}");
            }

            // 启动监控
            _inputMonitor.Start();
            _activityTracker.Start();
            Log("Monitoring started.");

            // 同步自启动状态
            _autoStartService.SetAutoStart(_settings.AutoStart);

            UpdateTrayTooltip();

            // 恢复上次窗口模式和位置
            RestoreWindowState();

            Log("=== OnStartup COMPLETE ===");
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show($"启动失败: {ex.Message}\n\n{ex.StackTrace}\n\n日志: {LogFile}",
                "BreakReminder 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("OnExit called.");
        SaveWindowState();
        _activityTracker?.Stop();
        _inputMonitor?.Dispose();
        _mediaPlayback?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    // --- 创建托盘图标 ---

    private void CreateTrayIcon()
    {
        // 构建右键菜单
        _statusMenuItem = new MenuItem { Header = "⏱ 启动中...", IsEnabled = false };

        var settingsItem = new MenuItem { Header = "⚙ 设置" };
        settingsItem.Click += (_, _) => OpenMainWindow();

        var forceBreakItem = new MenuItem { Header = "🔄 立即休息" };
        forceBreakItem.Click += (_, _) =>
        {
            if (_activityTracker != null)
                _notificationService?.ShowBreakReminder(_activityTracker.WorkedSeconds);
        };

        _pauseMenuItem = new MenuItem { Header = "⏸ 暂停" };
        _pauseMenuItem.Click += (_, _) => TogglePause();

        var resetItem = new MenuItem { Header = "🔄 重置计时" };
        resetItem.Click += (_, _) =>
        {
            _activityTracker?.Reset();
            UpdateTrayTooltip();
        };

        var exitItem = new MenuItem { Header = "❌ 退出" };
        exitItem.Click += (_, _) => Shutdown();

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(_statusMenuItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(forceBreakItem);
        contextMenu.Items.Add(_pauseMenuItem);
        contextMenu.Items.Add(resetItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);
        Log("ContextMenu built.");

        // 创建 TaskbarIcon
        _trayIcon = new TaskbarIcon();
        Log($"TaskbarIcon instance created. Type={_trayIcon.GetType().FullName}");

        // 使用 WPF 的 IconSource (BitmapImage) 而不是 System.Drawing.Icon
        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
            _trayIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(iconUri);
            Log("IconSource set via BitmapImage.");
        }
        catch (Exception ex)
        {
            Log($"IconSource failed: {ex.GetType().Name}: {ex.Message}");
            // 后备：使用 System.Drawing 系统图标
            _trayIcon.Icon = SystemIcons.Information;
            Log("Fallback: System icon assigned.");
        }

        _trayIcon.ToolTipText = "番茄闹钟 - 休息提醒器";
        _trayIcon.ContextMenu = contextMenu;
        Log("Tooltip and ContextMenu set.");

        // 左键双击打开设置
        _trayIcon.TrayMouseDoubleClick += (_, _) => OpenMainWindow();

        // 确保可见
        _trayIcon.Visibility = Visibility.Visible;
        Log($"Visibility set to: {_trayIcon.Visibility}");

        // 强制创建 Win32 窗口句柄
        try
        {
            _trayIcon.ForceCreate(false);
            Log("ForceCreate(false) succeeded.");
        }
        catch (Exception ex)
        {
            Log($"ForceCreate error: {ex.GetType().Name}: {ex.Message}");
        }

        Log($"Final state: IconSource={_trayIcon.IconSource != null}, Icon={_trayIcon.Icon != null}, Visibility={_trayIcon.Visibility}");
    }

    // --- 事件处理 ---

    private void OnBreakTimeReached()
    {
        Dispatcher.Invoke(() =>
        {
            if (_activityTracker != null)
                _notificationService?.ShowBreakReminder(_activityTracker.WorkedSeconds);
        });
    }

    private void OnReminderDismissed()
    {
        _activityTracker?.Reset();
        UpdateTrayTooltip();
    }

    private void OnReminderSnoozed()
    {
        _activityTracker?.Snooze();
        UpdateTrayTooltip();
    }

    private void OnStatusChanged()
    {
        Dispatcher.Invoke(UpdateTrayTooltip);
    }

    private void TogglePause()
    {
        if (_activityTracker == null) return;

        if (_isPaused)
        {
            _activityTracker.Resume();
            _isPaused = false;
        }
        else
        {
            _activityTracker.Pause();
            _isPaused = true;
        }

        if (_pauseMenuItem != null)
            _pauseMenuItem.Header = _isPaused ? "▶ 继续" : "⏸ 暂停";

        _mainWindow?.UpdatePauseState(_isPaused);

        UpdateTrayTooltip();
    }

    // --- 辅助方法 ---

    private void OpenMainWindow(double? left = null, double? top = null)
    {
        if (_mainWindow != null)
        {
            _mainWindow.Activate();
            return;
        }

        if (_activityTracker == null) return;

        _mainWindow = new MainWindow(_activityTracker, _settings, _isPaused);
        if (left.HasValue && top.HasValue)
        {
            _mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            _mainWindow.Left = left.Value;
            _mainWindow.Top = top.Value;
        }
        _mainWindow.ResetConfirmed += () =>
        {
            _activityTracker?.Reset();
            UpdateTrayTooltip();
        };
        _mainWindow.PauseResumeRequested += TogglePause;
        _mainWindow.SettingsSaved += OnSettingsSaved;
        _mainWindow.CompactModeRequested += EnterCompactMode;
        _mainWindow.Closed += (_, _) => _mainWindow = null;
        _mainWindow.Show();
    }

    private void EnterCompactMode()
    {
        double left = _mainWindow?.Left ?? 100;
        double top = _mainWindow?.Top ?? 100;

        _mainWindow?.Close();
        _mainWindow = null;

        if (_activityTracker == null) return;

        _compactWindow = new CompactWindow(_activityTracker, _settings, left, top);
        _compactWindow.ExpandRequested += ExitCompactMode;
        _compactWindow.Closed += (_, _) => _compactWindow = null;
        _compactWindow.Show();

        // 保存窗口状态
        _settings.IsCompactMode = true;
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settingsService?.Save(_settings);
    }

    private void ExitCompactMode()
    {
        double left = _compactWindow?.Left ?? 100;
        double top = _compactWindow?.Top ?? 100;

        _compactWindow?.Close();
        _compactWindow = null;

        OpenMainWindow(left, top);

        // 保存窗口状态
        _settings.IsCompactMode = false;
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settingsService?.Save(_settings);
    }

    private void RestoreWindowState()
    {
        double? left = double.IsNaN(_settings.WindowLeft) ? null : _settings.WindowLeft;
        double? top = double.IsNaN(_settings.WindowTop) ? null : _settings.WindowTop;

        if (_settings.IsCompactMode)
        {
            if (_activityTracker == null) return;
            _compactWindow = new CompactWindow(_activityTracker, _settings, left, top);
            _compactWindow.ExpandRequested += ExitCompactMode;
            _compactWindow.Closed += (_, _) => _compactWindow = null;
            _compactWindow.Show();
        }
        else
        {
            OpenMainWindow(left, top);
        }
    }

    private void SaveWindowState()
    {
        if (_compactWindow != null)
        {
            _settings.IsCompactMode = true;
            _settings.WindowLeft = _compactWindow.Left;
            _settings.WindowTop = _compactWindow.Top;
        }
        else if (_mainWindow != null)
        {
            _settings.IsCompactMode = false;
            _settings.WindowLeft = _mainWindow.Left;
            _settings.WindowTop = _mainWindow.Top;
        }
        _settingsService?.Save(_settings);
    }

    private void OnSettingsSaved(AppSettings newSettings)
    {
        _settings = newSettings;
        _settingsService?.Save(_settings);

        _activityTracker?.UpdateSettings(_settings);
        _notificationService?.UpdateSettings(_settings);
        _autoStartService?.SetAutoStart(_settings.AutoStart);
        _mainWindow?.UpdateSettings(_settings);
        _compactWindow?.UpdateSettings(_settings);

        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon == null || _activityTracker == null) return;

        var worked = TimeSpan.FromSeconds(_activityTracker.WorkedSeconds);
        var target = TimeSpan.FromMinutes(_settings.WorkDurationMinutes);
        var remaining = target - worked;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        string status;
        if (_isPaused)
            status = "⏸ 已暂停";
        else if (_activityTracker.IsIdle)
            status = "😴 空闲中";
        else if (_activityTracker.IsActive)
            status = "💻 工作中";
        else
            status = "⏱ 监控中";

        _trayIcon.ToolTipText = $"番茄闹钟 {status}\n已工作: {worked:mm\\:ss} / {target:mm\\:ss}\n距离休息: {remaining:mm\\:ss}";

        if (_statusMenuItem != null)
        {
            string statusText = _isPaused ? "已暂停" : (_activityTracker.IsIdle ? "空闲中" : "工作中");
            _statusMenuItem.Header = $"⏱ {statusText} | {worked:mm\\:ss} / {target:mm\\:ss}";
        }
    }
}
