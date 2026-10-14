using Avalonia.Controls;
using Avalonia.Interactivity;
using CheckSumTool.ViewModels;

namespace CheckSumTool.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 打开期间把轻提示的目标切到本窗口：设置窗口是模态对话框，
    /// 主窗口被挡在后面，提示落在主窗口上等于看不见。
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSettingsOpen = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSettingsOpen = false;
        }

        base.OnClosed(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
