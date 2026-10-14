using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CheckSumTool.Services;
using CheckSumTool.ViewModels;
using CheckSumTool.Views;

namespace CheckSumTool;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            vm.ApplyTheme(vm.ThemeMode);

            // 语言已按设置初始化，此时把全部校验文件扩展名关联到本程序（写 HKCU，无需管理员），
            // 「打开方式」里显示的程序名与界面同款
            FileAssociation.EnsureRegistered();

            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // 由文件关联或 --verify 传入的校验文件：直接进入校验面板并开始
            var files = CommandLine.ExtractChecksumFiles(Environment.GetCommandLineArgs().Skip(1));
            if (files.Count > 0)
            {
                vm.SelectedTabIndex = 0;
                vm.Verify.AddFiles(files);

                // 等窗口显示后再启动，保证进度与状态更新可见
                window.Opened += (_, _) => vm.Verify.StartCommand.Execute(null);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
