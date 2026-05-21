using System.Windows;

namespace BreakReminder.Services;

/// <summary>
/// 多语言切换服务。通过替换 Application.Resources.MergedDictionaries 中的
/// ResourceDictionary 实现运行时语言切换。
/// </summary>
public static class LocalizationService
{
    /// <summary>支持的语言列表 (Code, FlagLabel, NativeName)</summary>
    public static readonly (string Code, string FlagLabel, string Name)[] SupportedLanguages =
    {
        ("zh-CN", "CN", "简体中文"),
        ("en",    "EN", "English"),
        ("ja",    "JP", "日本語"),
        ("zh-TW", "TW", "繁體中文"),
    };

    /// <summary>各语言按钮的代表色 (与国旗主色相关)</summary>
    public static readonly string[] FlagColors = { "#DE2910", "#3C3B6E", "#BC002D", "#004B96" };

    private static ResourceDictionary? _currentDict;

    public static string CurrentLanguage { get; private set; } = "zh-CN";

    /// <summary>切换界面语言</summary>
    public static void SwitchLanguage(string langCode)
    {
        var uri = new Uri($"pack://application:,,,/Resources/Lang.{langCode}.xaml", UriKind.Absolute);
        ResourceDictionary newDict;
        try
        {
            newDict = new ResourceDictionary { Source = uri };
        }
        catch
        {
            uri = new Uri("pack://application:,,,/Resources/Lang.zh-CN.xaml", UriKind.Absolute);
            newDict = new ResourceDictionary { Source = uri };
            langCode = "zh-CN";
        }

        var mergedDicts = Application.Current.Resources.MergedDictionaries;

        if (_currentDict != null)
            mergedDicts.Remove(_currentDict);

        mergedDicts.Add(newDict);
        _currentDict = newDict;
        CurrentLanguage = langCode;
    }

    /// <summary>按 key 获取当前语言的字符串</summary>
    public static string Get(string key)
    {
        if (Application.Current.TryFindResource(key) is string s)
            return s;
        return key;
    }

    /// <summary>格式化获取 (用于含 {0} 占位符的字符串)</summary>
    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }
}
