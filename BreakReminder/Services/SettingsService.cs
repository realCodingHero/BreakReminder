using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BreakReminder.Models;

namespace BreakReminder.Services;

/// <summary>
/// 将 <see cref="AppSettings"/> 序列化到 JSON 文件进行持久化。
/// 文件路径：%APPDATA%\BreakReminder\settings.json
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BreakReminder");

    private static readonly string SettingsFilePath =
        Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 从磁盘加载设置。如果文件不存在或解析失败，返回默认设置。
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                Debug.WriteLine("[SettingsService] Settings file not found, returning defaults.");
                return new AppSettings();
            }

            string json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (settings is null)
            {
                Debug.WriteLine("[SettingsService] Deserialization returned null, returning defaults.");
                return new AppSettings();
            }

            Debug.WriteLine("[SettingsService] Settings loaded successfully.");
            return settings;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] Failed to load settings: {ex.Message}");
            return new AppSettings();
        }
    }

    /// <summary>
    /// 将设置保存到磁盘。自动创建目录。
    /// </summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);

            Debug.WriteLine("[SettingsService] Settings saved successfully.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] Failed to save settings: {ex.Message}");
        }
    }
}
