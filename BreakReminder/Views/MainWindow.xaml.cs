using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using BreakReminder.ViewModels;
using BreakReminder.Models;
using BreakReminder.Services;

namespace BreakReminder.Views;

/// <summary>
/// 将 Progress (0~1) × 容器宽度 → 像素宽度
/// </summary>
public class ProgressToWidthConverter : IMultiValueConverter
{
    public static readonly ProgressToWidthConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2
            && values[0] is double progress
            && values[1] is double containerWidth)
        {
            return Math.Max(0, progress * containerWidth);
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ActivityTracker _tracker;
    private AppSettings _currentSettings;

    public event Action<AppSettings>? SettingsSaved;
    public event Action? ResetConfirmed;
    public event Action? PauseResumeRequested;
    public event Action? CompactModeRequested;

    public MainWindow(ActivityTracker tracker, AppSettings settings, bool isPaused)
    {
        _currentSettings = settings;
        _tracker = tracker;

        InitializeComponent();

        _viewModel = new MainViewModel(tracker, settings);
        _viewModel.IsPaused = isPaused;
        DataContext = _viewModel;

        _viewModel.ResetRequested += OnResetRequested;
        _viewModel.PauseResumeRequested += () => PauseResumeRequested?.Invoke();
    }

    // --- 重置确认 ---

    private void OnResetRequested()
    {
        var result = MessageBox.Show(
            "确认要重置计时吗？\n当前的工作时间将被清零。",
            "重置确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            ResetConfirmed?.Invoke();
    }

    // --- 透明模式 ---

    private void OnCompactClick(object sender, RoutedEventArgs e)
    {
        CompactModeRequested?.Invoke();
    }

    // --- 自定义标题栏 ---

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // 点击非交互控件区域时可拖拽窗口
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close(); // 隐藏到托盘
    }

    // --- 抽屉操作 ---

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        LoadSettingsToDrawer();
        Backdrop.Visibility = Visibility.Visible;
        DrawerPanel.Visibility = Visibility.Visible;
        var sb = (Storyboard)FindResource("OpenDrawer");
        sb.Begin(this);
    }

    private void OnDrawerClose(object sender, RoutedEventArgs e) => CloseDrawer();

    private void OnBackdropClick(object sender, MouseButtonEventArgs e) => CloseDrawer();

    private void CloseDrawer()
    {
        var sb = (Storyboard)FindResource("CloseDrawer");
        sb.Completed += OnCloseDrawerCompleted;
        sb.Begin(this);
    }

    private void OnCloseDrawerCompleted(object? sender, EventArgs e)
    {
        Backdrop.Visibility = Visibility.Collapsed;
        DrawerPanel.Visibility = Visibility.Collapsed;
    }

    private void OnDrawerSave(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromDrawer();
        CloseDrawer();
    }

    // --- 设置数据搬运 ---

    private void LoadSettingsToDrawer()
    {
        SliderWork.Value   = _currentSettings.WorkDurationMinutes;
        SliderBreak.Value  = _currentSettings.BreakDurationMinutes;
        SliderIdle.Value   = _currentSettings.IdleThresholdMinutes;
        SliderSnooze.Value = _currentSettings.SnoozeDurationMinutes;

        ChkToast.IsChecked    = _currentSettings.EnableToastNotification;
        ChkPopup.IsChecked    = _currentSettings.EnablePopupWindow;
        ChkSound.IsChecked    = _currentSettings.EnableSound;

        ChkKeyboard.IsChecked  = _currentSettings.MonitorKeyboard;
        ChkMouse.IsChecked     = _currentSettings.MonitorMouse;
        ChkMedia.IsChecked     = _currentSettings.MonitorMediaPlayback;

        ChkAutoStart.IsChecked = _currentSettings.AutoStart;
    }

    private void SaveSettingsFromDrawer()
    {
        var s = _currentSettings.Clone();

        s.WorkDurationMinutes   = (int)SliderWork.Value;
        s.BreakDurationMinutes  = (int)SliderBreak.Value;
        s.IdleThresholdMinutes  = (int)SliderIdle.Value;
        s.SnoozeDurationMinutes = (int)SliderSnooze.Value;

        s.EnableToastNotification = ChkToast.IsChecked == true;
        s.EnablePopupWindow       = ChkPopup.IsChecked == true;
        s.EnableSound             = ChkSound.IsChecked == true;

        s.MonitorKeyboard      = ChkKeyboard.IsChecked == true;
        s.MonitorMouse         = ChkMouse.IsChecked == true;
        s.MonitorMediaPlayback = ChkMedia.IsChecked == true;

        s.AutoStart = ChkAutoStart.IsChecked == true;

        _currentSettings = s;
        SettingsSaved?.Invoke(s);
    }

    // --- 外部同步 ---

    public void UpdatePauseState(bool isPaused) => _viewModel.IsPaused = isPaused;

    public void UpdateSettings(AppSettings settings)
    {
        _currentSettings = settings;
        _viewModel.UpdateSettings(settings);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Stop();
        base.OnClosed(e);
    }
}
