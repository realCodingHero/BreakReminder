using System.Diagnostics;
using System.Windows.Threading;
using BreakReminder.Models;

namespace BreakReminder.Services;

/// <summary>
/// 核心活动追踪器。聚合 <see cref="InputMonitorService"/> 和
/// <see cref="MediaPlaybackService"/> 的状态，按秒累计工作时间，
/// 到达工作时长后触发休息提醒。
/// </summary>
public sealed class ActivityTracker
{
    private readonly InputMonitorService _inputMonitor;
    private readonly MediaPlaybackService _mediaPlayback;
    private readonly DispatcherTimer _timer;

    private AppSettings _settings;
    private int _targetSeconds;          // WorkDurationMinutes * 60 + snooze additions
    private int _snoozedExtraSeconds;    // 累计追加的贪睡秒数
    private bool _breakEventFired;       // 防止重复触发

    // ======================================================================
    //  Public properties
    // ======================================================================

    /// <summary>已累计工作秒数</summary>
    public int WorkedSeconds { get; private set; }

    /// <summary>用户当前是否处于活动状态</summary>
    public bool IsActive { get; private set; }

    /// <summary>用户是否空闲（超过 IdleThresholdMinutes 无活动）</summary>
    public bool IsIdle { get; private set; }

    /// <summary>追踪是否已被手动暂停</summary>
    public bool IsPaused { get; private set; }

    /// <summary>自动重置后等待用户归来</summary>
    public bool IsWaitingForActivity { get; private set; }

    // ======================================================================
    //  Events
    // ======================================================================

    /// <summary>工作时间达到目标时触发</summary>
    public event Action? BreakTimeReached;

    /// <summary>状态发生变化时触发（活动/空闲/暂停/秒数更新）</summary>
    public event Action? StatusChanged;

    // ======================================================================
    //  Constructor
    // ======================================================================

    public ActivityTracker(
        InputMonitorService inputMonitor,
        MediaPlaybackService mediaPlayback,
        AppSettings settings)
    {
        _inputMonitor = inputMonitor ?? throw new ArgumentNullException(nameof(inputMonitor));
        _mediaPlayback = mediaPlayback ?? throw new ArgumentNullException(nameof(mediaPlayback));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _targetSeconds = _settings.WorkDurationMinutes * 60;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTick;
    }

    // ======================================================================
    //  Public methods
    // ======================================================================

    /// <summary>开始追踪</summary>
    public void Start()
    {
        IsPaused = false;
        _timer.Start();
        Debug.WriteLine("[ActivityTracker] Started.");
        StatusChanged?.Invoke();
    }

    /// <summary>停止追踪</summary>
    public void Stop()
    {
        _timer.Stop();
        Debug.WriteLine("[ActivityTracker] Stopped.");
        StatusChanged?.Invoke();
    }

    /// <summary>重置工作时间和贪睡状态，重新开始计时</summary>
    public void Reset()
    {
        WorkedSeconds = 0;
        _snoozedExtraSeconds = 0;
        _targetSeconds = _settings.WorkDurationMinutes * 60;
        _breakEventFired = false;
        IsActive = false;
        IsIdle = false;
        IsPaused = false;

        Debug.WriteLine("[ActivityTracker] Reset.");
        StatusChanged?.Invoke();
    }

    /// <summary>手动调整本次剩余分钟数</summary>
    public void SetRemainingMinutes(int minutes)
    {
        int remainingSeconds = Math.Max(0, minutes * 60);
        WorkedSeconds = Math.Max(0, _targetSeconds - remainingSeconds);
        _breakEventFired = WorkedSeconds >= _targetSeconds;

        Debug.WriteLine($"[ActivityTracker] Remaining set to {minutes} min (worked={WorkedSeconds}s).");
        StatusChanged?.Invoke();
    }

    /// <summary>贪睡：在当前目标上追加 SnoozeDurationMinutes</summary>
    public void Snooze()
    {
        int snoozeSeconds = _settings.SnoozeDurationMinutes * 60;
        _snoozedExtraSeconds += snoozeSeconds;
        _targetSeconds += snoozeSeconds;
        _breakEventFired = false; // 允许再次触发

        Debug.WriteLine($"[ActivityTracker] Snoozed +{_settings.SnoozeDurationMinutes} min. " +
                        $"New target: {_targetSeconds / 60} min.");
        StatusChanged?.Invoke();
    }

    /// <summary>暂停计时（不重置）</summary>
    public void Pause()
    {
        IsPaused = true;
        Debug.WriteLine("[ActivityTracker] Paused.");
        StatusChanged?.Invoke();
    }

    /// <summary>恢复计时</summary>
    public void Resume()
    {
        IsPaused = false;
        Debug.WriteLine("[ActivityTracker] Resumed.");
        StatusChanged?.Invoke();
    }

    /// <summary>运行时更新设置</summary>
    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // 重新计算目标秒数（保留已有贪睡量）
        _targetSeconds = _settings.WorkDurationMinutes * 60 + _snoozedExtraSeconds;

        Debug.WriteLine($"[ActivityTracker] Settings updated. Target: {_targetSeconds / 60} min.");
        StatusChanged?.Invoke();
    }

    // ======================================================================
    //  Timer tick
    // ======================================================================

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (IsPaused) return;

        double idleThresholdSeconds = _settings.IdleThresholdMinutes * 60.0;
        double secondsSinceLastInput = (DateTime.UtcNow - _inputMonitor.LastInputTime).TotalSeconds;

        bool isInputActive = secondsSinceLastInput < idleThresholdSeconds;
        bool isMediaActive = _settings.MonitorMediaPlayback && _mediaPlayback.IsMediaPlaying;

        bool wasActive = IsActive;
        bool wasIdle = IsIdle;
        int previousSeconds = WorkedSeconds;

        if (isInputActive || isMediaActive)
        {
            if (IsWaitingForActivity)
            {
                // 用户回来了，结束等待，下一个 tick 开始计时
                IsWaitingForActivity = false;
                IsActive = true;
                IsIdle = false;
                Debug.WriteLine("[ActivityTracker] User returned, start counting.");
            }
            else
            {
                // 正常活跃：累计工作时间
                IsActive = true;
                IsIdle = false;
                WorkedSeconds++;
            }
        }
        else if (secondsSinceLastInput >= idleThresholdSeconds)
        {
            // 空闲超过阈值
            IsActive = false;
            IsIdle = true;
            // 不累计工作时间

            // 空闲超过休息时长 → 视为已完成休息，自动重置并等待
            double breakSeconds = _settings.BreakDurationMinutes * 60.0;
            if (WorkedSeconds > 0 && secondsSinceLastInput >= breakSeconds)
            {
                Debug.WriteLine($"[ActivityTracker] Idle {secondsSinceLastInput:F0}s >= break {breakSeconds}s, auto-reset + wait.");
                Reset();
                IsWaitingForActivity = true;
            }
        }

        // 检查是否达到工作时长
        if (!_breakEventFired && WorkedSeconds >= _targetSeconds)
        {
            _breakEventFired = true;
            Debug.WriteLine($"[ActivityTracker] Break time reached after {WorkedSeconds}s.");
            BreakTimeReached?.Invoke();
        }

        // 状态有任何变化时通知订阅者
        if (wasActive != IsActive || wasIdle != IsIdle || previousSeconds != WorkedSeconds)
        {
            StatusChanged?.Invoke();
        }
    }
}
