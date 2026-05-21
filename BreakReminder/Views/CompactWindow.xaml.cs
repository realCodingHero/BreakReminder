using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
    private bool _isEditing;
    private int _editMinutes;
    private bool _isExpanded;

    // --- Win32 鼠标穿透 ---
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const double BTN_WIDTH = 30.0;
    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(280));
    private static readonly CubicEase Ease = new() { EasingMode = EasingMode.EaseInOut };

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private IntPtr _hwnd;
    private DispatcherTimer? _hoverCheckTimer;
    private bool _isClickThrough;

    public event Action? ExpandRequested;

    public CompactWindow(ActivityTracker tracker, AppSettings settings,
                         double? startLeft = null, double? startTop = null)
    {
        _tracker = tracker;
        _settings = settings;

        InitializeComponent();

        // SizeToContent 生效后再设位置
        Loaded += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Left = startLeft ?? (SystemParameters.WorkArea.Right - ActualWidth - 20);
            Top = startTop ?? (SystemParameters.WorkArea.Top + 20);
        };

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
        if (_isEditing) return;

        var worked = TimeSpan.FromSeconds(_tracker.WorkedSeconds);
        var target = TimeSpan.FromMinutes(_settings.WorkDurationMinutes);
        var remaining = target - worked;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        CountdownText.Text = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";

        CountdownText.Foreground = remaining.TotalMinutes <= 1 && remaining > TimeSpan.Zero
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    }

    // --- 灵动岛展开/收起 ---

    /// <param name="lockOnly">true 时只展开锁定按钮（锁定模式用）</param>
    private void ExpandIsland(bool lockOnly = false)
    {
        if (_isExpanded) return;
        _isExpanded = true;

        _anchorCenterX = Left + ActualWidth / 2.0;
        SizeChanged += OnAnimatingSizeChanged;

        // 锁定按钮始终展开
        var widthAnim = new DoubleAnimation(0, BTN_WIDTH, AnimDuration) { EasingFunction = Ease };
        widthAnim.Completed += (_, _) => SizeChanged -= OnAnimatingSizeChanged;
        AnimateWidth(LockButton, 0, BTN_WIDTH, widthAnim);
        AnimateOpacity(LockButton, 0, 1);
        LockButton.IsHitTestVisible = true;

        if (!lockOnly)
        {
            AnimateWidth(ExpandButton, 0, BTN_WIDTH);
            AnimateOpacity(ExpandButton, 0, 1);
            ExpandButton.IsHitTestVisible = true;
        }
    }

    private void CollapseIsland()
    {
        if (!_isExpanded) return;
        _isExpanded = false;

        _anchorCenterX = Left + ActualWidth / 2.0;
        SizeChanged += OnAnimatingSizeChanged;

        var widthAnim = new DoubleAnimation(BTN_WIDTH, 0, AnimDuration) { EasingFunction = Ease };
        widthAnim.Completed += (_, _) => SizeChanged -= OnAnimatingSizeChanged;
        AnimateWidth(LockButton, BTN_WIDTH, 0, widthAnim);
        AnimateOpacity(LockButton, 1, 0);
        LockButton.IsHitTestVisible = false;

        // ExpandButton 可能没展开过（锁定模式），安全收起
        AnimateWidth(ExpandButton, ExpandButton.ActualWidth, 0);
        AnimateOpacity(ExpandButton, ExpandButton.Opacity, 0);
        ExpandButton.IsHitTestVisible = false;
    }

    private double _anchorCenterX;

    private void OnAnimatingSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Left = _anchorCenterX - ActualWidth / 2.0;
    }

    private void AnimateWidth(FrameworkElement el, double from, double to, DoubleAnimation? customAnim = null)
    {
        var anim = customAnim ?? new DoubleAnimation(from, to, AnimDuration) { EasingFunction = Ease };
        el.BeginAnimation(WidthProperty, anim);
        if (el is System.Windows.Controls.Button btn)
        {
            var clipAnim = new RectAnimation(
                new Rect(0, 0, from, 26),
                new Rect(0, 0, to, 26),
                AnimDuration) { EasingFunction = Ease };
            if (btn.Clip is RectangleGeometry rg)
                rg.BeginAnimation(RectangleGeometry.RectProperty, clipAnim);
        }
    }

    private void AnimateOpacity(UIElement el, double from, double to)
    {
        var anim = new DoubleAnimation(from, to, AnimDuration) { EasingFunction = Ease };
        el.BeginAnimation(OpacityProperty, anim);
    }

    // --- Hover ---

    private void OnMouseEnterRoot(object sender, MouseEventArgs e)
    {
        if (!_isLocked)
            ExpandIsland();
    }

    private void OnMouseLeaveRoot(object sender, MouseEventArgs e)
    {
        if (!_isLocked && !_isEditing)
            CollapseIsland();
    }

    // --- 拖拽 / 点击编辑 ---

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isLocked || _isEditing || e.ChangedButton != MouseButton.Left) return;

        if (e.OriginalSource is FrameworkElement fe &&
            (fe.Name == "LockButton" || fe.Name == "ExpandButton"))
            return;

        bool clickedCountdown = e.OriginalSource == CountdownText ||
                                (e.OriginalSource is FrameworkElement src &&
                                 src.Name == "CountdownText");

        double prevLeft = Left, prevTop = Top;
        DragMove();

        bool moved = Math.Abs(Left - prevLeft) > 2 || Math.Abs(Top - prevTop) > 2;
        if (!moved && clickedCountdown)
            EnterEditMode();
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e) { }
    private void OnRootMouseUp(object sender, MouseButtonEventArgs e) { }

    // --- 时间编辑 ---

    private void EnterEditMode()
    {
        if (_isLocked || _isEditing) return;

        var worked = TimeSpan.FromSeconds(_tracker.WorkedSeconds);
        var target = TimeSpan.FromMinutes(_settings.WorkDurationMinutes);
        var remaining = target - worked;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        _editMinutes = ((int)Math.Ceiling(remaining.TotalMinutes / 5.0)) * 5;
        _editMinutes = Math.Max(0, Math.Min(_editMinutes, _settings.WorkDurationMinutes));
        UpdateEditDisplay();

        _isEditing = true;
        CountdownText.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;
    }

    private void OnEditUp(object sender, RoutedEventArgs e)
    {
        _editMinutes = Math.Min(_editMinutes + 5, _settings.WorkDurationMinutes);
        UpdateEditDisplay();
    }

    private void OnEditDown(object sender, RoutedEventArgs e)
    {
        _editMinutes = Math.Max(_editMinutes - 5, 0);
        UpdateEditDisplay();
    }

    private void OnEditWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0)
            _editMinutes = Math.Min(_editMinutes + 5, _settings.WorkDurationMinutes);
        else
            _editMinutes = Math.Max(_editMinutes - 5, 0);
        UpdateEditDisplay();
        e.Handled = true;
    }

    private void OnEditCancel(object sender, RoutedEventArgs e) => ExitEditMode(apply: false);

    private void ExitEditMode(bool apply)
    {
        if (!_isEditing) return;
        _isEditing = false;
        EditPanel.Visibility = Visibility.Collapsed;
        CountdownText.Visibility = Visibility.Visible;

        if (apply)
            _tracker.SetRemainingMinutes(_editMinutes);

        RefreshCountdown();
    }

    private void UpdateEditDisplay()
    {
        EditMinutesText.Text = _editMinutes.ToString();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_isEditing && e.Key == Key.Escape)
        {
            ExitEditMode(apply: false);
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_isEditing)
        {
            var hit = VisualTreeHelper.HitTest(EditPanel, e.GetPosition(EditPanel));
            if (hit == null)
            {
                ExitEditMode(apply: true);
                e.Handled = true;
                return;
            }
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    // --- 鼠标穿透 ---

    private void SetClickThrough(bool enable)
    {
        if (_hwnd == IntPtr.Zero) return;
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        if (enable)
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
        else
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
        _isClickThrough = enable;
    }

    private void OnHoverCheckTick(object? sender, EventArgs e)
    {
        if (!_isLocked || _hwnd == IntPtr.Zero) return;

        GetCursorPos(out POINT pt);

        // 检测鼠标是否在整个窗口区域（含少量外边距）
        var winPos = PointToScreen(new Point(0, 0));
        double pad = 6;
        bool overWindow = pt.X >= winPos.X - pad && pt.X <= winPos.X + ActualWidth + pad &&
                          pt.Y >= winPos.Y - pad && pt.Y <= winPos.Y + ActualHeight + pad;

        if (overWindow && _isClickThrough)
        {
            // 鼠标进入 → 取消穿透，展开灵动岛（仅锁定按钮）
            SetClickThrough(false);
            ExpandIsland(lockOnly: true);
        }
        else if (!overWindow && !_isClickThrough && _isLocked)
        {
            // 鼠标离开 → 收起灵动岛，恢复穿透
            CollapseIsland();
            SetClickThrough(true);
        }
    }

    // --- 锁定/解锁 ---

    private void OnLockToggle(object sender, RoutedEventArgs e)
    {
        _isLocked = !_isLocked;
        LockButton.Content = _isLocked ? "🔒" : "🔓";
        LockButton.ToolTip = _isLocked ? "解锁位置" : "锁定位置";

        RootBorder.Background = new SolidColorBrush(
            _isLocked ? Color.FromArgb(0x99, 0x1E, 0x1E, 0x2E)
                      : Color.FromArgb(0xCC, 0x1E, 0x1E, 0x2E));

        if (_isLocked)
        {
            if (_isEditing) ExitEditMode(apply: false);
            CollapseIsland();
            SetClickThrough(true);
            _hoverCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _hoverCheckTimer.Tick += OnHoverCheckTick;
            _hoverCheckTimer.Start();
        }
        else
        {
            _hoverCheckTimer?.Stop();
            _hoverCheckTimer = null;
            SetClickThrough(false);
            // 解锁后保持展开状态，展示全部按钮
            if (_isExpanded)
            {
                // 已经展开了（锁按钮可见），补充展开返回按钮
                AnimateWidth(ExpandButton, 0, BTN_WIDTH);
                AnimateOpacity(ExpandButton, 0, 1);
                ExpandButton.IsHitTestVisible = true;
            }
            else
            {
                ExpandIsland();
            }
        }
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
        _hoverCheckTimer?.Stop();
        base.OnClosed(e);
    }
}
