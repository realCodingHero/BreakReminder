using System.Diagnostics;
using BreakReminder.Helpers;

namespace BreakReminder.Services;

/// <summary>
/// 全局键盘和鼠标钩子监控服务。
/// 通过 Win32 低层钩子（WH_KEYBOARD_LL / WH_MOUSE_LL）检测用户输入活动，
/// 更新 LastInputTime 并触发 InputDetected 事件。
/// </summary>
public sealed class InputMonitorService : IDisposable
{
    // --- 钩子句柄 ---
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;

    // --- 保持委托引用以防止 GC 回收 ---
    private readonly NativeMethods.LowLevelHookProc _keyboardProc;
    private readonly NativeMethods.LowLevelHookProc _mouseProc;

    private bool _disposed;

    /// <summary>最近一次检测到用户输入的时间（UTC）</summary>
    public DateTime LastInputTime { get; private set; } = DateTime.UtcNow;

    /// <summary>是否正在监控</summary>
    public bool IsMonitoring { get; private set; }

    /// <summary>当检测到任何用户输入时触发</summary>
    public event Action? InputDetected;

    public InputMonitorService()
    {
        // 在构造函数中创建委托引用，确保在整个生命周期内不被 GC 回收
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    /// <summary>
    /// 安装全局键盘和鼠标钩子，开始监控用户输入。
    /// </summary>
    public void Start()
    {
        if (IsMonitoring) return;

        _keyboardHookId = SetHook(NativeMethods.WH_KEYBOARD_LL, _keyboardProc);
        _mouseHookId = SetHook(NativeMethods.WH_MOUSE_LL, _mouseProc);

        LastInputTime = DateTime.UtcNow;
        IsMonitoring = true;

        Debug.WriteLine("[InputMonitorService] Hooks installed – monitoring started.");
    }

    /// <summary>
    /// 卸载所有钩子，停止监控。
    /// </summary>
    public void Stop()
    {
        if (!IsMonitoring) return;

        UnhookSafe(ref _keyboardHookId);
        UnhookSafe(ref _mouseHookId);

        IsMonitoring = false;

        Debug.WriteLine("[InputMonitorService] Hooks removed – monitoring stopped.");
    }

    // ======================================================================
    //  Private helpers
    // ======================================================================

    /// <summary>使用当前进程模块安装低层钩子</summary>
    private static IntPtr SetHook(int hookType, NativeMethods.LowLevelHookProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule
            ?? throw new InvalidOperationException("Cannot obtain the main module of the current process.");

        return NativeMethods.SetWindowsHookEx(
            hookType,
            proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);
    }

    /// <summary>安全卸载钩子并重置句柄</summary>
    private static void UnhookSafe(ref IntPtr hookId)
    {
        if (hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(hookId);
            hookId = IntPtr.Zero;
        }
    }

    /// <summary>
    /// 键盘钩子回调 – 仅更新时间戳并触发事件，不做耗时操作。
    /// </summary>
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            OnInputDetected();
        }
        return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// 鼠标钩子回调 – 仅更新时间戳并触发事件，不做耗时操作。
    /// </summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            OnInputDetected();
        }
        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    /// <summary>更新最后活动时间并触发事件</summary>
    private void OnInputDetected()
    {
        LastInputTime = DateTime.UtcNow;
        InputDetected?.Invoke();
    }

    // ======================================================================
    //  IDisposable
    // ======================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
    }
}
