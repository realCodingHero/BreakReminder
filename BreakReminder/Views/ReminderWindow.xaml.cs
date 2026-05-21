using System.Runtime.InteropServices;
using System.Windows;
using BreakReminder.ViewModels;

namespace BreakReminder.Views;

/// <summary>
/// 休息提醒弹窗的代码后置。
/// 覆盖鼠标所在的显示器，显示倒计时和操作按钮。
/// </summary>
public partial class ReminderWindow : Window
{
    private readonly Action _onDismiss;
    private readonly Action _onSnooze;

    // --- Win32 多屏检测 ---
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public ReminderWindow(int workedSeconds, int breakDurationMinutes, Action onDismiss, Action onSnooze)
    {
        InitializeComponent();

        _onDismiss = onDismiss;
        _onSnooze = onSnooze;

        // 获取鼠标所在屏幕
        GetCursorPos(out POINT cursorPt);
        var hMonitor = MonitorFromPoint(cursorPt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref mi);

        var bounds = mi.rcMonitor;

        // 物理像素 → WPF 设备无关像素
        var source = PresentationSource.FromVisual(Application.Current.MainWindow!);
        double scaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double scaleY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

        Left = bounds.Left * scaleX;
        Top = bounds.Top * scaleY;
        Width = (bounds.Right - bounds.Left) * scaleX;
        Height = (bounds.Bottom - bounds.Top) * scaleY;

        var viewModel = new ReminderViewModel(workedSeconds, breakDurationMinutes);
        DataContext = viewModel;

        viewModel.Dismissed += OnDismissed;
        viewModel.Snoozed += OnSnoozed;
    }

    private void OnDismissed()
    {
        _onDismiss();
        Close();
    }

    private void OnSnoozed()
    {
        _onSnooze();
        Close();
    }
}
