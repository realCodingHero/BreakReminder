using System.Windows.Input;
using System.Windows.Threading;
using BreakReminder.Helpers;

namespace BreakReminder.ViewModels;

/// <summary>
/// 休息提醒弹窗的 ViewModel。
/// 显示已工作时间、建议休息时长，并运行倒计时。
/// </summary>
public class ReminderViewModel : ViewModelBase
{
    private readonly DispatcherTimer _countdownTimer;
    private string _workedTimeText = string.Empty;
    private string _breakSuggestionText = string.Empty;
    private string _countdownText = string.Empty;
    private int _countdownSeconds;

    /// <summary>
    /// 用户点击"知道了"时触发。
    /// </summary>
    public event Action? Dismissed;

    /// <summary>
    /// 用户点击"推迟"时触发。
    /// </summary>
    public event Action? Snoozed;

    /// <param name="workedSeconds">已连续工作的秒数。</param>
    /// <param name="breakDurationMinutes">建议休息的分钟数。</param>
    public ReminderViewModel(int workedSeconds, int breakDurationMinutes)
    {
        int workedMinutes = workedSeconds / 60;
        WorkedTimeText = $"已连续工作 {workedMinutes} 分钟";
        BreakSuggestionText = $"建议休息 {breakDurationMinutes} 分钟";

        CountdownSeconds = breakDurationMinutes * 60;
        UpdateCountdownText();

        DismissCommand = new RelayCommand(ExecuteDismiss);
        SnoozeCommand = new RelayCommand(ExecuteSnooze);

        // 每秒递减倒计时
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    // ── 属性 ──────────────────────────────────────────────────

    /// <summary>
    /// 格式化后的已工作时间文本，例如"已连续工作 45 分钟"。
    /// </summary>
    public string WorkedTimeText
    {
        get => _workedTimeText;
        private set => SetProperty(ref _workedTimeText, value);
    }

    /// <summary>
    /// 休息建议文本，例如"建议休息 5 分钟"。
    /// </summary>
    public string BreakSuggestionText
    {
        get => _breakSuggestionText;
        private set => SetProperty(ref _breakSuggestionText, value);
    }

    /// <summary>
    /// 倒计时显示文本，格式 mm:ss。
    /// </summary>
    public string CountdownText
    {
        get => _countdownText;
        private set => SetProperty(ref _countdownText, value);
    }

    /// <summary>
    /// 剩余倒计时秒数。
    /// </summary>
    public int CountdownSeconds
    {
        get => _countdownSeconds;
        private set => SetProperty(ref _countdownSeconds, value);
    }

    // ── 命令 ──────────────────────────────────────────────────

    public ICommand DismissCommand { get; }
    public ICommand SnoozeCommand { get; }

    // ── 私有方法 ──────────────────────────────────────────────

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        if (CountdownSeconds > 0)
        {
            CountdownSeconds--;
            UpdateCountdownText();
        }
        else
        {
            _countdownTimer.Stop();
        }
    }

    private void UpdateCountdownText()
    {
        int minutes = CountdownSeconds / 60;
        int seconds = CountdownSeconds % 60;
        CountdownText = $"{minutes:D2}:{seconds:D2}";
    }

    private void ExecuteDismiss()
    {
        _countdownTimer.Stop();
        Dismissed?.Invoke();
    }

    private void ExecuteSnooze()
    {
        _countdownTimer.Stop();
        Snoozed?.Invoke();
    }
}
