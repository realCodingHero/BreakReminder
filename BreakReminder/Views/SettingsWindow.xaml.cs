using System.Windows;
using BreakReminder.Models;
using BreakReminder.ViewModels;

namespace BreakReminder.Views;

/// <summary>
/// 设置窗口的代码后置。
/// 创建 SettingsViewModel 并将其事件转发给外部调用者。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    /// <summary>
    /// 当用户确认保存时触发，携带更新后的 AppSettings。
    /// </summary>
    public event Action<AppSettings>? SettingsSaved;

    /// <param name="currentSettings">当前应用设置（ViewModel 会创建深拷贝进行编辑）。</param>
    public SettingsWindow(AppSettings currentSettings)
    {
        InitializeComponent();

        _viewModel = new SettingsViewModel(currentSettings);
        DataContext = _viewModel;

        _viewModel.SettingsSaved += OnViewModelSettingsSaved;
        _viewModel.CloseRequested += OnViewModelCloseRequested;
    }

    private void OnViewModelSettingsSaved(AppSettings updatedSettings)
    {
        SettingsSaved?.Invoke(updatedSettings);
        Close();
    }

    private void OnViewModelCloseRequested()
    {
        Close();
    }
}
