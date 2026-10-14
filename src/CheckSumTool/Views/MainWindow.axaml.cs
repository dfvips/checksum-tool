using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CheckSumTool.ViewModels;

namespace CheckSumTool.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 用隧道阶段捕获，确保子控件（按钮、滚动区等）先处理事件也不会漏掉
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            Width = Math.Max(MinWidth, vm.Settings.WindowWidth);
            Height = Math.Max(MinHeight, vm.Settings.WindowHeight);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PersistSettings(Width, Height);
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// 点击输入框以外的位置时释放焦点。macOS 原生行为是点击按钮不夺走焦点，
    /// 会让输入框一直停留在「获得焦点」的样式上。
    /// </summary>
    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control source)
        {
            return;
        }

        // 点在输入框内部时保留焦点，其余一律释放
        if (source is TextBox || source.FindAncestorOfType<TextBox>() is not null)
        {
            return;
        }

        FocusManager?.ClearFocus();
    }

    private void OnThemeToggle(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CycleTheme();
        }
    }

    private async void OnSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var window = new SettingsWindow { DataContext = vm };
        await window.ShowDialog(this);
    }
}
