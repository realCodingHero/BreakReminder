using System.Diagnostics;
using Microsoft.Win32;

namespace BreakReminder.Services;

/// <summary>
/// 通过 Windows 注册表管理开机自启。
/// 在 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 下
/// 创建或删除 "BreakReminder" 键值。
/// </summary>
public sealed class AutoStartService
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "BreakReminder";

    /// <summary>
    /// 设置或取消开机自启。
    /// </summary>
    /// <param name="enable">true = 添加自启；false = 移除自启</param>
    public void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
            if (key is null)
            {
                Debug.WriteLine("[AutoStartService] Unable to open Run registry key.");
                return;
            }

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    Debug.WriteLine("[AutoStartService] Cannot determine executable path.");
                    return;
                }

                key.SetValue(AppRegistryName, $"\"{exePath}\"");
                Debug.WriteLine($"[AutoStartService] Auto-start enabled: {exePath}");
            }
            else
            {
                key.DeleteValue(AppRegistryName, throwOnMissingValue: false);
                Debug.WriteLine("[AutoStartService] Auto-start disabled.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoStartService] Error setting auto-start: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查当前是否已设置开机自启。
    /// </summary>
    public bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
            var value = key?.GetValue(AppRegistryName);
            return value is not null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoStartService] Error checking auto-start: {ex.Message}");
            return false;
        }
    }
}
