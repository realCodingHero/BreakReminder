using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
    private string _selectedLanguage;

    public event Action<AppSettings>? SettingsSaved;
    public event Action? ResetConfirmed;
    public event Action? PauseResumeRequested;
    public event Action? CompactModeRequested;

    public MainWindow(ActivityTracker tracker, AppSettings settings, bool isPaused)
    {
        _currentSettings = settings;
        _tracker = tracker;
        _selectedLanguage = settings.Language;

        InitializeComponent();

        _viewModel = new MainViewModel(tracker, settings);
        _viewModel.IsPaused = isPaused;
        DataContext = _viewModel;

        _viewModel.ResetRequested += OnResetRequested;
        _viewModel.PauseResumeRequested += () => PauseResumeRequested?.Invoke();

        BuildLanguageSelector();
    }

    // --- 语言选择器 ---

    private void BuildLanguageSelector()
    {
        LangGrid.Children.Clear();
        for (int i = 0; i < LocalizationService.SupportedLanguages.Length; i++)
        {
            var (code, flagLabel, name) = LocalizationService.SupportedLanguages[i];
            var color = LocalizationService.FlagColors[i];
            bool isSelected = code == _selectedLanguage;

            var btn = new Button
            {
                Tag = code,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(2),
                Padding = new Thickness(4, 6, 4, 6),
                BorderThickness = new Thickness(2),
                BorderBrush = isSelected
                    ? (Brush)FindResource("AccentBrush")
                    : new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                Background = isSelected
                    ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x6B, 0x6B))
                    : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            };

            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            // 国旗色块 + 缩写
            var flagBorder = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString(color)!,
                CornerRadius = new CornerRadius(3),
                Width = 28, Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
                Child = new TextBlock
                {
                    Text = flagLabel,
                    FontSize = 9, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }
            };
            sp.Children.Add(flagBorder);

            // 语言名称
            sp.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 10,
                Foreground = isSelected
                    ? (Brush)FindResource("AccentBrush")
                    : (Brush)FindResource("FgSecondary"),
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            btn.Content = sp;
            btn.Click += OnLanguageSelected;
            LangGrid.Children.Add(btn);
        }
    }

    private void OnLanguageSelected(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string code)
        {
            _selectedLanguage = code;
            LocalizationService.SwitchLanguage(code);
            BuildLanguageSelector(); // 刷新高亮状态
        }
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

    // --- 倒计时编辑 ---

    private bool _isEditing;
    private int _editMinutes;

    private void OnCountdownClick(object sender, MouseButtonEventArgs e)
    {
        if (_isEditing) return;

        // 读取当前剩余分钟（向上取整到 5 的倍数）
        var worked = TimeSpan.FromSeconds(_tracker.WorkedSeconds);
        var target = TimeSpan.FromMinutes(_currentSettings.WorkDurationMinutes);
        var remaining = target - worked;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        _editMinutes = ((int)Math.Ceiling(remaining.TotalMinutes / 5.0)) * 5;
        _editMinutes = Math.Max(0, Math.Min(_editMinutes, _currentSettings.WorkDurationMinutes));
        UpdateEditDisplay();

        _isEditing = true;
        CountdownDisplay.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnEditUp(object sender, RoutedEventArgs e)
    {
        _editMinutes = Math.Min(_editMinutes + 5, _currentSettings.WorkDurationMinutes);
        UpdateEditDisplay();
    }

    private void OnEditDown(object sender, RoutedEventArgs e)
    {
        _editMinutes = Math.Max(_editMinutes - 5, 0);
        UpdateEditDisplay();
    }

    private void OnEditMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0)
            _editMinutes = Math.Min(_editMinutes + 5, _currentSettings.WorkDurationMinutes);
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
        CountdownDisplay.Visibility = Visibility.Visible;

        if (apply)
            _tracker.SetRemainingMinutes(_editMinutes);
    }

    private void UpdateEditDisplay()
    {
        EditMinutesText.Text = _editMinutes.ToString();
    }

    // ESC 取消编辑，点击编辑区域外确认
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
            // 检查点击是否在编辑面板外
            var hitResult = VisualTreeHelper.HitTest(EditPanel, e.GetPosition(EditPanel));
            if (hitResult == null)
            {
                ExitEditMode(apply: true);
                e.Handled = true;
                return;
            }
        }
        base.OnPreviewMouseLeftButtonDown(e);
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

        _selectedLanguage = _currentSettings.Language;
        BuildLanguageSelector();
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

        s.Language = _selectedLanguage;

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
