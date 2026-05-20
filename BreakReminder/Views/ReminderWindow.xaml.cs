using System.Windows;
using BreakReminder.ViewModels;

namespace BreakReminder.Views;

/// <summary>
/// 休息提醒弹窗的代码后置。
/// 全屏覆盖所有显示器，显示倒计时和操作按钮。
/// </summary>
public partial class ReminderWindow : Window
{
    private readonly Action _onDismiss;
    private readonly Action _onSnooze;

    /// <param name="workedSeconds">已连续工作的秒数。</param>
    /// <param name="breakDurationMinutes">建议休息的分钟数。</param>
    /// <param name="onDismiss">用户点击"知道了"后的回调。</param>
    /// <param name="onSnooze">用户点击"推迟"后的回调。</param>
    public ReminderWindow(int workedSeconds, int breakDurationMinutes, Action onDismiss, Action onSnooze)
    {
        InitializeComponent();

        _onDismiss = onDismiss;
        _onSnooze = onSnooze;

        // 覆盖所有虚拟屏幕
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var viewModel = new ReminderViewModel(workedSeconds, breakDurationMinutes);
        DataContext = viewModel;

        viewModel.Dismissed += OnDismissed;
        viewModel.Snoozed += OnSnoozed;
    }

    private void OnDismissed()
    {
        _onDismiss();
        Close();
    }

    private void OnSnoozed()
    {
        _onSnooze();
        Close();
    }
}
