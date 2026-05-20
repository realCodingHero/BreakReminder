using System.Windows.Input;
using BreakReminder.Helpers;
using BreakReminder.Models;

namespace BreakReminder.ViewModels;

/// <summary>
/// 设置窗口的 ViewModel，将 AppSettings 的属性暴露为可绑定属性，
/// 并提供保存/取消命令。
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _workingCopy;

    // ── 计时设置 ──────────────────────────────────────────────
    private int _workDurationMinutes;
    private int _breakDurationMinutes;
    private int _idleThresholdMinutes;
    private int _snoozeDurationMinutes;

    // ── 提醒方式 ──────────────────────────────────────────────
    private bool _enableToastNotification;
    private bool _enablePopupWindow;
    private bool _enableSound;

    // ── 监控项目 ──────────────────────────────────────────────
    private bool _monitorKeyboard;
    private bool _monitorMouse;
    private bool _monitorMediaPlayback;

    // ── 其他 ──────────────────────────────────────────────────
    private bool _autoStart;

    /// <summary>
    /// 当用户点击"保存"时触发，携带修改后的设置副本。
    /// </summary>
    public event Action<AppSettings>? SettingsSaved;

    /// <summary>
    /// 当用户点击"取消"时触发，请求关闭窗口。
    /// </summary>
    public event Action? CloseRequested;

    public SettingsViewModel(AppSettings currentSettings)
    {
        // 使用深拷贝避免直接修改原始设置
        _workingCopy = currentSettings.Clone();

        // 初始化绑定字段
        _workDurationMinutes = _workingCopy.WorkDurationMinutes;
        _breakDurationMinutes = _workingCopy.BreakDurationMinutes;
        _idleThresholdMinutes = _workingCopy.IdleThresholdMinutes;
        _snoozeDurationMinutes = _workingCopy.SnoozeDurationMinutes;

        _enableToastNotification = _workingCopy.EnableToastNotification;
        _enablePopupWindow = _workingCopy.EnablePopupWindow;
        _enableSound = _workingCopy.EnableSound;

        _monitorKeyboard = _workingCopy.MonitorKeyboard;
        _monitorMouse = _workingCopy.MonitorMouse;
        _monitorMediaPlayback = _workingCopy.MonitorMediaPlayback;

        _autoStart = _workingCopy.AutoStart;

        SaveCommand = new RelayCommand(ExecuteSave);
        CancelCommand = new RelayCommand(ExecuteCancel);
    }

    // ── 计时设置属性 ──────────────────────────────────────────

    public int WorkDurationMinutes
    {
        get => _workDurationMinutes;
        set => SetProperty(ref _workDurationMinutes, value);
    }

    public int BreakDurationMinutes
    {
        get => _breakDurationMinutes;
        set => SetProperty(ref _breakDurationMinutes, value);
    }

    public int IdleThresholdMinutes
    {
        get => _idleThresholdMinutes;
        set => SetProperty(ref _idleThresholdMinutes, value);
    }

    public int SnoozeDurationMinutes
    {
        get => _snoozeDurationMinutes;
        set => SetProperty(ref _snoozeDurationMinutes, value);
    }

    // ── 提醒方式属性 ──────────────────────────────────────────

    public bool EnableToastNotification
    {
        get => _enableToastNotification;
        set => SetProperty(ref _enableToastNotification, value);
    }

    public bool EnablePopupWindow
    {
        get => _enablePopupWindow;
        set => SetProperty(ref _enablePopupWindow, value);
    }

    public bool EnableSound
    {
        get => _enableSound;
        set => SetProperty(ref _enableSound, value);
    }

    // ── 监控项目属性 ──────────────────────────────────────────

    public bool MonitorKeyboard
    {
        get => _monitorKeyboard;
        set => SetProperty(ref _monitorKeyboard, value);
    }

    public bool MonitorMouse
    {
        get => _monitorMouse;
        set => SetProperty(ref _monitorMouse, value);
    }

    public bool MonitorMediaPlayback
    {
        get => _monitorMediaPlayback;
        set => SetProperty(ref _monitorMediaPlayback, value);
    }

    // ── 其他属性 ──────────────────────────────────────────────

    public bool AutoStart
    {
        get => _autoStart;
        set => SetProperty(ref _autoStart, value);
    }

    // ── 命令 ──────────────────────────────────────────────────

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>
    /// 将当前 UI 值写回工作副本并通过事件传出。
    /// </summary>
    private void ExecuteSave()
    {
        _workingCopy.WorkDurationMinutes = WorkDurationMinutes;
        _workingCopy.BreakDurationMinutes = BreakDurationMinutes;
        _workingCopy.IdleThresholdMinutes = IdleThresholdMinutes;
        _workingCopy.SnoozeDurationMinutes = SnoozeDurationMinutes;

        _workingCopy.EnableToastNotification = EnableToastNotification;
        _workingCopy.EnablePopupWindow = EnablePopupWindow;
        _workingCopy.EnableSound = EnableSound;

        _workingCopy.MonitorKeyboard = MonitorKeyboard;
        _workingCopy.MonitorMouse = MonitorMouse;
        _workingCopy.MonitorMediaPlayback = MonitorMediaPlayback;

        _workingCopy.AutoStart = AutoStart;

        SettingsSaved?.Invoke(_workingCopy);
    }

    private void ExecuteCancel()
    {
        CloseRequested?.Invoke();
    }
}
