using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private bool _isPaused;
    private bool _isAnimating;
    private int _outsideTickCount;

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
    public event Action? PauseResumeRequested;
    public event Action? ResetRequested;
    public event Action? ExitRequested;

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

        // 统一的鼠标位置检测定时器（替代不可靠的 MouseEnter/Leave）
        _hoverCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _hoverCheckTimer.Tick += OnHoverCheckTick;
        _hoverCheckTimer.Start();

        BuildContextMenu();
        RefreshCountdown();
    }

    // --- 右键菜单 ---

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();

        var pauseItem = new MenuItem { Header = LocalizationService.Get("TrayPause") };
        pauseItem.Click += (_, _) => PauseResumeRequested?.Invoke();

        var resetItem = new MenuItem { Header = LocalizationService.Get("TrayReset") };
        resetItem.Click += (_, _) => ResetRequested?.Invoke();

        var expandItem = new MenuItem { Header = LocalizationService.Get("BackToMain") };
        expandItem.Click += (_, _) => { if (!_isLocked) ExpandRequested?.Invoke(); };

        var exitItem = new MenuItem { Header = LocalizationService.Get("TrayExit") };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(pauseItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(expandItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        // 菜单关闭后恢复穿透状态
        menu.Closed += (_, _) =>
        {
            if (_isLocked && !_isExpanded)
                SetClickThrough(true);
        };

        RootBorder.ContextMenu = menu;
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

        // 同步暂停按钮图标
        PauseButton.Content = _isPaused ? "▶" : "⏸";
        PauseButton.ToolTip = _isPaused
            ? LocalizationService.Get("Resume")
            : LocalizationService.Get("Pause");
    }

    // --- 暂停/重置 ---

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        PauseResumeRequested?.Invoke();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        ResetRequested?.Invoke();
    }

    public void UpdatePauseState(bool paused)
    {
        _isPaused = paused;
        RefreshCountdown();
    }

    // --- 灵动岛展开/收起 ---

    /// <param name="lockOnly">true 时只展开锁定按钮（锁定模式用）</param>
    private void ExpandIsland(bool lockOnly = false)
    {
        if (_isExpanded) return;
        _isExpanded = true;

        CaptureCountdownAnchor();
        _isAnimating = true;
        _outsideTickCount = 0;
        SizeChanged += OnAnimatingSizeChanged;

        // 锁定按钮始终展开
        var widthAnim = new DoubleAnimation(0, BTN_WIDTH, AnimDuration) { EasingFunction = Ease };
        widthAnim.Completed += OnExpandAnimationCompleted;
        AnimateWidth(LockButton, 0, BTN_WIDTH, widthAnim);
        AnimateOpacity(LockButton, 0, 1);
        LockButton.IsHitTestVisible = true;

        if (!lockOnly)
        {
            // 非锁定模式：展示暂停/重置/返回按钮
            AnimateWidth(PauseButton, 0, 24);
            AnimateOpacity(PauseButton, 0, 1);
            PauseButton.IsHitTestVisible = true;

            AnimateWidth(ResetButton, 0, 24);
            AnimateOpacity(ResetButton, 0, 1);
            ResetButton.IsHitTestVisible = true;

            AnimateWidth(ExpandButton, 0, BTN_WIDTH);
            AnimateOpacity(ExpandButton, 0, 1);
            ExpandButton.IsHitTestVisible = true;
        }
    }

    private void CollapseIsland()
    {
        if (!_isExpanded) return;
        _isExpanded = false;

        CaptureCountdownAnchor();
        _isAnimating = true;
        _outsideTickCount = 0;
        SizeChanged += OnAnimatingSizeChanged;

        var widthAnim = new DoubleAnimation(BTN_WIDTH, 0, AnimDuration) { EasingFunction = Ease };
        widthAnim.Completed += OnCollapseAnimationCompleted;
        AnimateWidth(LockButton, BTN_WIDTH, 0, widthAnim);
        AnimateOpacity(LockButton, 1, 0);
        LockButton.IsHitTestVisible = false;

        // 暂停/重置按钮安全收起
        AnimateWidth(PauseButton, PauseButton.ActualWidth, 0);
        AnimateOpacity(PauseButton, PauseButton.Opacity, 0);
        PauseButton.IsHitTestVisible = false;

        AnimateWidth(ResetButton, ResetButton.ActualWidth, 0);
        AnimateOpacity(ResetButton, ResetButton.Opacity, 0);
        ResetButton.IsHitTestVisible = false;

        // ExpandButton 可能没展开过（锁定模式），安全收起
        AnimateWidth(ExpandButton, ExpandButton.ActualWidth, 0);
        AnimateOpacity(ExpandButton, ExpandButton.Opacity, 0);
        ExpandButton.IsHitTestVisible = false;
    }

    private double _anchorCenterX;

    /// <summary>
    /// 记录倒计时文本在屏幕上的中心 X 坐标作为锚点。
    /// 展开/收起时以此为不动点调整窗口 Left。
    /// </summary>
    private void CaptureCountdownAnchor()
    {
        var countdownPos = CountdownText.TranslatePoint(
            new Point(CountdownText.ActualWidth / 2.0, 0), this);
        _anchorCenterX = Left + countdownPos.X;
    }

    private void OnAnimatingSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 重新计算倒计时在窗口中的位置，保持屏幕锚点不变
        var countdownPos = CountdownText.TranslatePoint(
            new Point(CountdownText.ActualWidth / 2.0, 0), this);
        Left = _anchorCenterX - countdownPos.X;
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

    // --- 动画完成回调 ---

    private void OnExpandAnimationCompleted(object? sender, EventArgs e)
    {
        SizeChanged -= OnAnimatingSizeChanged;
        // 动画结束后延迟一帧再解锁 hover 检测，避免立即触发 collapse
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _isAnimating = false;
            _outsideTickCount = 0;
        });
    }

    private void OnCollapseAnimationCompleted(object? sender, EventArgs e)
    {
        SizeChanged -= OnAnimatingSizeChanged;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _isAnimating = false;
            _outsideTickCount = 0;
        });
    }

    // --- 统一 Hover 检测 ---

    private bool IsCursorOverWindow()
    {
        if (_hwnd == IntPtr.Zero) return false;
        GetCursorPos(out POINT pt);
        var winPos = PointToScreen(new Point(0, 0));
        double pad = 14;
        return pt.X >= winPos.X - pad && pt.X <= winPos.X + ActualWidth + pad &&
               pt.Y >= winPos.Y - pad && pt.Y <= winPos.Y + ActualHeight + pad;
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
        if (_hwnd == IntPtr.Zero || _isAnimating) return;

        bool over = IsCursorOverWindow();

        if (over)
        {
            _outsideTickCount = 0;

            if (_isLocked)
            {
                // 锁定模式：取消穿透，展开仅锁定按钮
                if (_isClickThrough)
                {
                    SetClickThrough(false);
                    ExpandIsland(lockOnly: true);
                }
            }
            else
            {
                // 非锁定模式：展开全部按钮
                if (!_isExpanded && !_isEditing)
                    ExpandIsland();
            }
        }
        else
        {
            _outsideTickCount++;
            // 连续 4 次 tick (~320ms) 都在外面才收起
            if (_outsideTickCount >= 4 && _isExpanded && !_isEditing)
            {
                if (_isLocked)
                {
                    CollapseIsland();
                    SetClickThrough(true);
                }
                else
                {
                    CollapseIsland();
                }
                _outsideTickCount = 0;
            }
        }
    }

    // --- 锁定/解锁 ---

    private void OnLockToggle(object sender, RoutedEventArgs e)
    {
        _isLocked = !_isLocked;
        LockButton.Content = _isLocked ? "🔒" : "🔓";
        LockButton.ToolTip = _isLocked
            ? LocalizationService.Get("UnlockTip")
            : LocalizationService.Get("LockTip");

        RootBorder.Background = new SolidColorBrush(
            _isLocked ? Color.FromArgb(0x99, 0x1E, 0x1E, 0x2E)
                      : Color.FromArgb(0xCC, 0x1E, 0x1E, 0x2E));

        if (_isLocked)
        {
            if (_isEditing) ExitEditMode(apply: false);
            // 收起暂停/重置/返回，只保留锁按钮
            AnimateWidth(PauseButton, PauseButton.ActualWidth, 0);
            AnimateOpacity(PauseButton, PauseButton.Opacity, 0);
            PauseButton.IsHitTestVisible = false;

            AnimateWidth(ResetButton, ResetButton.ActualWidth, 0);
            AnimateOpacity(ResetButton, ResetButton.Opacity, 0);
            ResetButton.IsHitTestVisible = false;

            AnimateWidth(ExpandButton, ExpandButton.ActualWidth, 0);
            AnimateOpacity(ExpandButton, ExpandButton.Opacity, 0);
            ExpandButton.IsHitTestVisible = false;

            // 鼠标离开后再穿透（由 hover timer 管理）
        }
        else
        {
            SetClickThrough(false);
            _outsideTickCount = 0;
            // 解锁后展示全部按钮
            if (_isExpanded)
            {
                AnimateWidth(PauseButton, 0, 24);
                AnimateOpacity(PauseButton, 0, 1);
                PauseButton.IsHitTestVisible = true;

                AnimateWidth(ResetButton, 0, 24);
                AnimateOpacity(ResetButton, 0, 1);
                ResetButton.IsHitTestVisible = true;

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
