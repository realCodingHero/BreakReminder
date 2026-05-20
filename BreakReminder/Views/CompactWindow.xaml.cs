using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BreakReminder.Models;
using BreakReminder.Services;

namespace BreakReminder.Views;

public partial class CompactWindow : Window
{
    private readonly ActivityTracker _tracker;
    private AppSettings _settings;
    private readonly DispatcherTimer _refreshTimer;

    private bool _isLocked;

    /// <summary>用户点击"返回主窗口"时触发</summary>
    public event Action? ExpandRequested;

    public CompactWindow(ActivityTracker tracker, AppSettings settings,
                         double? startLeft = null, double? startTop = null)
    {
        _tracker = tracker;
        _settings = settings;

        InitializeComponent();

        // 初始位置：传入坐标 或 屏幕右上角
        Left = startLeft ?? (SystemParameters.WorkArea.Right - Width - 20);
        Top = startTop ?? (SystemParameters.WorkArea.Top + 20);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += (_, _) => RefreshCountdown();
        _refreshTimer.Start();

        RefreshCountdown();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        RefreshCountdown();
    }

    private void RefreshCountdown()
    {
        var worked = TimeSpan.FromSeconds(_tracker.WorkedSeconds);
        var target = TimeSpan.FromMinutes(_settings.WorkDurationMinutes);
        var remaining = target - worked;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        CountdownText.Text = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";

        // 快到时间时变亮红
        CountdownText.Foreground = remaining.TotalMinutes <= 1 && remaining > TimeSpan.Zero
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    }

    // --- Hover 显示/隐藏控制按钮 ---

    private void OnMouseEnterRoot(object sender, MouseEventArgs e)
    {
        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150));
        LockButton.IsHitTestVisible = true;
        LockButton.BeginAnimation(OpacityProperty, fadeIn);
        ExpandButton.IsHitTestVisible = true;
        ExpandButton.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void OnMouseLeaveRoot(object sender, MouseEventArgs e)
    {
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        LockButton.IsHitTestVisible = false;
        LockButton.BeginAnimation(OpacityProperty, fadeOut);
        ExpandButton.IsHitTestVisible = false;
        ExpandButton.BeginAnimation(OpacityProperty, fadeOut);
    }

    // --- 拖拽移动 (使用 DragMove，不闪烁) ---

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isLocked || e.ChangedButton != MouseButton.Left) return;

        // 只有在非按钮区域才拖拽
        if (e.OriginalSource is FrameworkElement fe &&
            (fe.Name == "LockButton" || fe.Name == "ExpandButton"))
            return;

        DragMove();
    }

    // MouseMove 和 MouseUp 不再需要
    private void OnRootMouseMove(object sender, MouseEventArgs e) { }
    private void OnRootMouseUp(object sender, MouseButtonEventArgs e) { }

    // --- 锁定/解锁 ---

    private void OnLockToggle(object sender, RoutedEventArgs e)
    {
        _isLocked = !_isLocked;
        LockButton.Content = _isLocked ? "🔒" : "🔓";
        LockButton.ToolTip = _isLocked ? "解锁位置" : "锁定位置";

        // 锁定时隐藏返回按钮
        ExpandButton.Visibility = _isLocked ? Visibility.Collapsed : Visibility.Visible;

        // 锁定时更透明
        RootBorder.Background = new SolidColorBrush(
            _isLocked ? Color.FromArgb(0x99, 0x1E, 0x1E, 0x2E)
                      : Color.FromArgb(0xCC, 0x1E, 0x1E, 0x2E));
    }

    // --- 返回主窗口 ---

    private void OnExpandClick(object sender, RoutedEventArgs e)
    {
        if (_isLocked) return;
        ExpandRequested?.Invoke();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        base.OnClosed(e);
    }
}
