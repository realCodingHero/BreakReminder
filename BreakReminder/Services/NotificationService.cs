using System.Diagnostics;
using System.Media;
using System.Windows;
using BreakReminder.Models;
using H.NotifyIcon;

namespace BreakReminder.Services;

/// <summary>
/// 统一通知服务：根据设置选择声音、系统托盘气泡、弹窗等方式提醒用户休息。
/// </summary>
public sealed class NotificationService
{
    private AppSettings _settings;
    private TaskbarIcon? _taskbarIcon;

    /// <summary>用户点击"关闭/确认"时触发</summary>
    public event Action? Dismissed;

    /// <summary>用户点击"稍后提醒"时触发</summary>
    public event Action? Snoozed;

    public NotificationService(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// 注入系统托盘图标引用，用于显示气泡通知。
    /// 应在 MainWindow 初始化后调用。
    /// </summary>
    public void SetTaskbarIcon(TaskbarIcon? taskbarIcon)
    {
        _taskbarIcon = taskbarIcon;
    }

    /// <summary>
    /// 根据当前设置显示休息提醒。
    /// </summary>
    /// <param name="workedSeconds">已连续工作秒数</param>
    public void ShowBreakReminder(int workedSeconds)
    {
        // 1) 声音提醒
        if (_settings.EnableSound)
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationService] Sound playback failed: {ex.Message}");
            }
        }

        // 2) 系统托盘气泡通知
        if (_settings.EnableToastNotification && _taskbarIcon is not null)
        {
            try
            {
                int workedMinutes = workedSeconds / 60;
                _taskbarIcon.ShowNotification(
                    "休息提醒 – BreakReminder",
                    $"您已连续工作 {workedMinutes} 分钟，建议休息 {_settings.BreakDurationMinutes} 分钟。",
                    H.NotifyIcon.Core.NotificationIcon.Info);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationService] Toast notification failed: {ex.Message}");
            }
        }

        // 3) 弹窗提醒
        if (_settings.EnablePopupWindow)
        {
            ShowPopupOnUIThread(workedSeconds);
        }
    }

    /// <summary>运行时更新设置</summary>
    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    // ======================================================================
    //  Private helpers
    // ======================================================================

    /// <summary>
    /// 确保弹窗在 UI 线程上创建和显示。
    /// </summary>
    private void ShowPopupOnUIThread(int workedSeconds)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        if (dispatcher.CheckAccess())
        {
            CreateAndShowReminderWindow(workedSeconds);
        }
        else
        {
            dispatcher.BeginInvoke(() => CreateAndShowReminderWindow(workedSeconds));
        }
    }

    /// <summary>
    /// 创建 ReminderWindow 并连接回调。
    /// </summary>
    private void CreateAndShowReminderWindow(int workedSeconds)
    {
        try
        {
            // ReminderWindow 位于 BreakReminder.Views 命名空间
            var window = new Views.ReminderWindow(
                workedSeconds,
                _settings.BreakDurationMinutes,
                onDismiss: () => Dismissed?.Invoke(),
                onSnooze: () => Snoozed?.Invoke());

            window.Show();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NotificationService] Failed to show ReminderWindow: {ex.Message}");
        }
    }
}
