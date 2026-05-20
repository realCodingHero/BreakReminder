using System.Windows;
using System.Windows.Controls;
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
    private TaskbarIcon? _trayIcon;
    private InputMonitorService? _inputMonitor;
    private MediaPlaybackService? _mediaPlayback;
    private ActivityTracker? _activityTracker;
    private NotificationService? _notificationService;
    private SettingsService? _settingsService;
    private AutoStartService? _autoStartService;
    private AppSettings _settings = new();

    private SettingsWindow? _settingsWindow;
    private bool _isPaused;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 加载设置
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();

        // 初始化服务
        _inputMonitor = new InputMonitorService();
        _mediaPlayback = new MediaPlaybackService();
        _notificationService = new NotificationService(_settings);
        _autoStartService = new AutoStartService();

        // 初始化活动追踪器
        _activityTracker = new ActivityTracker(_inputMonitor, _mediaPlayback, _settings);
        _activityTracker.BreakTimeReached += OnBreakTimeReached;
        _activityTracker.StatusChanged += OnStatusChanged;

        // 初始化通知服务事件
        _notificationService.Dismissed += OnReminderDismissed;
        _notificationService.Snoozed += OnReminderSnoozed;

        // 初始化系统托盘
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _notificationService.SetTaskbarIcon(_trayIcon);

        // 初始化媒体播放检测
        try
        {
            await _mediaPlayback.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"媒体播放检测初始化失败: {ex.Message}");
        }

        // 启动监控
        _inputMonitor.Start();
        _activityTracker.Start();

        // 同步自启动状态
        _autoStartService.SetAutoStart(_settings.AutoStart);

        UpdateTrayTooltip();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 清理资源
        _activityTracker?.Stop();
        _inputMonitor?.Dispose();
        _mediaPlayback?.Dispose();
        _trayIcon?.Dispose();

        base.OnExit(e);
    }

    // --- 事件处理 ---

    private void OnBreakTimeReached()
    {
        Dispatcher.Invoke(() =>
        {
            if (_activityTracker != null)
            {
                _notificationService?.ShowBreakReminder(_activityTracker.WorkedSeconds);
            }
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

    // --- 托盘菜单事件 ---

    private void OnTrayLeftMouseDown(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void OnForceBreakClick(object sender, RoutedEventArgs e)
    {
        if (_activityTracker != null)
        {
            _notificationService?.ShowBreakReminder(_activityTracker.WorkedSeconds);
        }
    }

    private void OnPauseResumeClick(object sender, RoutedEventArgs e)
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

        UpdatePauseMenuText();
        UpdateTrayTooltip();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _activityTracker?.Reset();
        UpdateTrayTooltip();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Shutdown();
    }

    // --- 辅助方法 ---

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.SettingsSaved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OnSettingsSaved(AppSettings newSettings)
    {
        _settings = newSettings;
        _settingsService?.Save(_settings);

        // 更新各服务的设置
        _activityTracker?.UpdateSettings(_settings);
        _notificationService?.UpdateSettings(_settings);
        _autoStartService?.SetAutoStart(_settings.AutoStart);

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

        // 更新菜单项状态文本
        UpdateStatusMenuItem();
    }

    private void UpdateStatusMenuItem()
    {
        if (_trayIcon?.ContextMenu == null || _activityTracker == null) return;

        var worked = TimeSpan.FromSeconds(_activityTracker.WorkedSeconds);
        var target = TimeSpan.FromMinutes(_settings.WorkDurationMinutes);

        foreach (var item in _trayIcon.ContextMenu.Items)
        {
            if (item is MenuItem menuItem && menuItem.Name == "StatusMenuItem")
            {
                string status = _isPaused ? "已暂停" : (_activityTracker.IsIdle ? "空闲中" : "工作中");
                menuItem.Header = $"⏱ {status} | {worked:mm\\:ss} / {target:mm\\:ss}";
                break;
            }
        }
    }

    private void UpdatePauseMenuText()
    {
        if (_trayIcon?.ContextMenu == null) return;

        foreach (var item in _trayIcon.ContextMenu.Items)
        {
            if (item is MenuItem menuItem && menuItem.Name == "PauseMenuItem")
            {
                menuItem.Header = _isPaused ? "▶ 继续" : "⏸ 暂停";
                break;
            }
        }
    }
}
