using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

            // macOS Finder 通过应用激活事件传文件，而不是命令行参数。
            // 订阅发生在窗口显示前，也能接住首次启动时的 openFiles 事件。
            var pendingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool windowOpened = false;

            void StartPendingVerification()
            {
                if (!windowOpened || vm.Verify.IsRunning || pendingFiles.Count == 0)
                {
                    return;
                }

                var files = pendingFiles.ToArray();
                pendingFiles.Clear();
                vm.SelectedTabIndex = 0;
                vm.Verify.AddFiles(files);
                if (vm.Verify.StartCommand.CanExecute(null))
                {
                    vm.Verify.StartCommand.Execute(null);
                }
            }

            void QueueFiles(IEnumerable<string> paths)
            {
                foreach (string file in CommandLine.ExtractChecksumFiles(paths))
                {
                    pendingFiles.Add(file);
                }

                if (pendingFiles.Count > 0)
                {
                    Dispatcher.UIThread.Post(StartPendingVerification);
                }
            }

            // 已运行时再次从 Finder「打开方式」打开，也进入校验；运行中的任务结束后接续处理。
            if (OperatingSystem.IsMacOS()
                && TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activation)
            {
                activation.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs fileEvent)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            QueueFiles(fileEvent.Files.Select(file => file.Path.LocalPath));
                            window.Activate();
                        });
                    }
                };
            }

            vm.Verify.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.Verify.IsRunning) && !vm.Verify.IsRunning)
                {
                    Dispatcher.UIThread.Post(StartPendingVerification);
                }
            };

            window.Opened += (_, _) =>
            {
                windowOpened = true;
                StartPendingVerification();
            };

            // Windows 文件关联与显式 --verify 仍走命令行。
            QueueFiles(Environment.GetCommandLineArgs().Skip(1));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
