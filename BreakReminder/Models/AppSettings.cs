namespace BreakReminder.Models;

/// <summary>
/// 应用程序设置数据模型
/// </summary>
public class AppSettings
{
    /// <summary>工作时长（分钟），达到后触发休息提醒</summary>
    public int WorkDurationMinutes { get; set; } = 45;

    /// <summary>建议休息时长（分钟），显示在提醒窗口中</summary>
    public int BreakDurationMinutes { get; set; } = 5;

    /// <summary>空闲阈值（分钟），无活动超过此时间则暂停工作计时</summary>
    public int IdleThresholdMinutes { get; set; } = 5;

    /// <summary>推迟时间（分钟）</summary>
    public int SnoozeDurationMinutes { get; set; } = 5;

    /// <summary>启用系统 Toast 通知</summary>
    public bool EnableToastNotification { get; set; } = true;

    /// <summary>启用弹窗提醒</summary>
    public bool EnablePopupWindow { get; set; } = true;

    /// <summary>启用声音提醒</summary>
    public bool EnableSound { get; set; } = true;

    /// <summary>开机自启（默认关闭）</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>监控键盘输入</summary>
    public bool MonitorKeyboard { get; set; } = true;

    /// <summary>监控鼠标输入</summary>
    public bool MonitorMouse { get; set; } = true;

    /// <summary>监控媒体播放（浏览器视频等）</summary>
    public bool MonitorMediaPlayback { get; set; } = true;

    // --- 窗口状态（自动持久化，不在设置界面显示） ---

    /// <summary>是否处于小窗模式</summary>
    public bool IsCompactMode { get; set; } = false;

    /// <summary>窗口 X 坐标</summary>
    public double WindowLeft { get; set; } = double.NaN;

    /// <summary>窗口 Y 坐标</summary>
    public double WindowTop { get; set; } = double.NaN;

    /// <summary>界面语言 (zh-CN, en, ja, zh-TW)</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>创建深拷贝</summary>
    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }
}
