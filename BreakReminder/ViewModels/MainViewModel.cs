using System.Windows.Threading;
using BreakReminder.Helpers;
using BreakReminder.Services;
using BreakReminder.Models;

namespace BreakReminder.ViewModels;

/// <summary>
/// 主窗口 ViewModel — 展示倒计时和工作状态
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly ActivityTracker _tracker;
    private AppSettings _settings;
    private readonly DispatcherTimer _refreshTimer;

    private string _workedTimeText = "00:00";
    private string _remainingTimeText = "00:00";
    private string _statusText = "监控中";
    private string _statusEmoji = "⏱";
    private double _progress;
    private bool _isPaused;

    public string WorkedTimeText
    {
        get => _workedTimeText;
        private set => SetProperty(ref _workedTimeText, value);
    }

    public string RemainingTimeText
    {
        get => _remainingTimeText;
        private set => SetProperty(ref _remainingTimeText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusEmoji
    {
        get => _statusEmoji;
        private set => SetProperty(ref _statusEmoji, value);
    }

    /// <summary>0.0 ~ 1.0 工作进度</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(PauseButtonIcon));
                OnPropertyChanged(nameof(PauseButtonLabel));
            }
        }
    }

    public string PauseButtonText => IsPaused ? LocalizationService.Get("ResumeFull") : LocalizationService.Get("PauseFull");
    public string PauseButtonIcon => IsPaused ? "▶" : "⏸";
    public string PauseButtonLabel => IsPaused ? LocalizationService.Get("Resume") : LocalizationService.Get("Pause");

    public RelayCommand ResetCommand { get; }
    public RelayCommand PauseResumeCommand { get; }

    public event Action? ResetRequested;
    public event Action? PauseResumeRequested;

    public MainViewModel(ActivityTracker tracker, AppSettings settings)
    {
        _tracker = tracker;
        _settings = settings;

        ResetCommand = new RelayCommand(OnReset);
        PauseResumeCommand = new RelayCommand(OnPauseResume);

        // 每秒刷新 UI
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        Refresh();
    }

    public void Refresh()
    {
        var worked = TimeSpan.FromSeconds(_tracker.WorkedSeconds);
        var target = TimeSpan.FromMinutes(_settings.WorkDurationMinutes);
        var remaining = target - worked;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        WorkedTimeText = $"{(int)worked.TotalMinutes:D2}:{worked.Seconds:D2}";
        RemainingTimeText = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";

        double totalSec = _settings.WorkDurationMinutes * 60.0;
        Progress = totalSec > 0 ? Math.Min(_tracker.WorkedSeconds / totalSec, 1.0) : 0;

        if (IsPaused)
        {
            StatusText = LocalizationService.Get("StatusPaused");
            StatusEmoji = "⏸";
        }
        else if (_tracker.IsWaitingForActivity)
        {
            StatusText = LocalizationService.Get("StatusResetWait");
            StatusEmoji = "☕";
        }
        else if (_tracker.IsIdle)
        {
            StatusText = LocalizationService.Get("StatusIdle");
            StatusEmoji = "😴";
        }
        else if (_tracker.IsActive)
        {
            StatusText = LocalizationService.Get("StatusWorking");
            StatusEmoji = "💻";
        }
        else
        {
            StatusText = LocalizationService.Get("StatusMonitoring");
            StatusEmoji = "⏱";
        }
    }

    private void OnReset() => ResetRequested?.Invoke();
    private void OnPauseResume() => PauseResumeRequested?.Invoke();

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        Refresh();
    }

    public void Stop() => _refreshTimer.Stop();
}
