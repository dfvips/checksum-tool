using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CheckSumTool;
using CheckSumTool.Controls;
using CheckSumTool.Core;
using CheckSumTool.Services;
using CheckSumTool.ViewModels;
using CheckSumTool.Views;

namespace CheckSumTool.Preview;

/// <summary>
/// 用 Avalonia 无头渲染（Skia）把各个面板渲染成 PNG，用于文档截图与界面回归检查。
/// 用法：dotnet run --project tools/CheckSumTool.Preview -- &lt;输出目录&gt;
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string outputDirectory = args.Length > 0 ? args[0] : "docs/screenshots";
        Directory.CreateDirectory(outputDirectory);

        // 让设置读写落在临时目录，避免污染用户真实偏好
        string settingsDirectory = Path.Combine(Path.GetTempPath(), "crc64tool-preview-settings");
        if (Directory.Exists(settingsDirectory))
        {
            Directory.Delete(settingsDirectory, recursive: true);
        }

        Environment.SetEnvironmentVariable("CHECKSUMTOOL_SETTINGS_DIR", settingsDirectory);

        string sampleDirectory = Path.Combine(Path.GetTempPath(), "crc64tool-preview-samples");
        var samples = PrepareSamples(sampleDirectory);

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        if (Environment.GetEnvironmentVariable("CRC64TOOL_BENCH") == "1")
        {
            RunBenchmark(samples);
        }

        Shoot(Path.Combine(outputDirectory, "01-verify-light.png"), ThemeMode.Light, vm =>
        {
            // 刚拖入、尚未开始校验：全部记录以「等待中」列出
            vm.SelectedTabIndex = 0;
            vm.Verify.AddFiles([samples.ChecksumFiles[0]]);
        });

        Shoot(Path.Combine(outputDirectory, "02-verify-dark.png"), ThemeMode.Dark, vm =>
        {
            vm.SelectedTabIndex = 0;
            vm.Verify.AddFiles([samples.ChecksumFiles[0]]);
            RunToCompletion(() => vm.Verify.StartCommand.Execute(null), () => vm.Verify.IsRunning);
        });

        Shoot(Path.Combine(outputDirectory, "03-generate-light.png"), ThemeMode.Light, vm =>
        {
            vm.SelectedTabIndex = 1;
            vm.Generate.AddFiles(samples.PlainFiles);
            RunToCompletion(() => vm.Generate.StartCommand.Execute(null), () => vm.Generate.IsRunning);
        });

        Shoot(Path.Combine(outputDirectory, "04-merge-light.png"), ThemeMode.Light, vm =>
        {
            vm.SelectedTabIndex = 2;
            vm.Merge.AddFiles(samples.ChecksumFiles);
        });

        Shoot(Path.Combine(outputDirectory, "05-settings-light.png"), ThemeMode.Light, _ => { },
            vm => new SettingsWindow());

        Shoot(Path.Combine(outputDirectory, "06-verify-english.png"), ThemeMode.Light, vm =>
        {
            vm.LanguageIndex = 2;
            vm.SelectedTabIndex = 0;
            vm.Verify.AddFiles([samples.ChecksumFiles[0]]);
            RunToCompletion(() => vm.Verify.StartCommand.Execute(null), () => vm.Verify.IsRunning);
        });

        Shoot(Path.Combine(outputDirectory, "07-settings-menu-light.png"), ThemeMode.Light, _ => { },
            vm => new SettingsWindow(), openSettingsMenu: true);

        Shoot(Path.Combine(outputDirectory, "08-textbox-blur.png"), ThemeMode.Light, vm =>
        {
            vm.SelectedTabIndex = 1;
            // 含一个 0 字节文件，用于核对「0 B 不显示」
            vm.Generate.AddFiles([samples.PlainFiles[0], samples.EmptyFile]);
        }, blurAfterShow: true);

        Shoot(Path.Combine(outputDirectory, "09-verify-running.png"), ThemeMode.Light, vm =>
        {
            vm.SelectedTabIndex = 0;
            // 同时加载两个校验文件，行数足够看出「进行中 / 等待中 / 已完成」三种状态
            vm.Verify.AddFiles([samples.ChecksumFiles[0], samples.ChecksumFiles[1]]);
            RunToCompletion(() => vm.Verify.StartCommand.Execute(null), () => vm.Verify.IsRunning);

            // 复原为「进行中」，用于核对进度条右侧的停止按钮
            vm.Verify.IsRunning = true;
            vm.Verify.Progress = 42;

            // 把前几行还原成「校验中」，核对单文件进度条
            double[] percents = [86, 54, 31,  .0];
            for (int i = 0; i < vm.Verify.Results.Count && i < percents.Length; i++)
            {
                var row = vm.Verify.Results[i];
                row.BytesDone = (long)(row.BytesTotal * percents[i]);
                row.BytesTotal = Math.Max(row.BytesTotal, 1);
                row.Progress = (int)percents[i];
                row.IsVerifying = percents[i] > 0;
            }
        });

        Shoot(Path.Combine(outputDirectory, "10-merge-conflict.png"), ThemeMode.Light, vm =>
        {
            // CRC64 与 MD5 混用，应弹出冲突轻提示
            vm.SelectedTabIndex = 2;
            vm.Merge.AddFiles([samples.ChecksumFiles[0], samples.Md5Checksum]);
        });

        Shoot(Path.Combine(outputDirectory, "11-merge-toast.png"), ThemeMode.Light, vm =>
        {
            // 导出成功后弹轻提示；故意用超长文件名验证「限宽 + 末尾省略」
            vm.SelectedTabIndex = 2;
            vm.Merge.AddFiles(samples.ChecksumFiles);
            vm.Merge.OutputBaseName =
                "古相思曲_第01集_这就是第一集 - 古相思曲_第07集_我回来了 - 古相思曲_第08集_南梦之愿";
            vm.Merge.MergeCommand.Execute(null);
        });

        Shoot(Path.Combine(outputDirectory, "12-generate-running.png"), ThemeMode.Light, vm =>
        {
            vm.SelectedTabIndex = 1;
            vm.Generate.AddFiles(samples.PlainFiles);
            RunToCompletion(() => vm.Generate.StartCommand.Execute(null), () => vm.Generate.IsRunning);

            // 复原为「进行中」，核对运行中文件名被锁定为只读
            vm.Generate.IsRunning = true;
            vm.Generate.Progress = 42;

            // 前几行还原为「计算中」，核对单文件进度条与百分比
            double[] percents = [92, 61, 28];
            for (int i = 0; i < vm.Generate.Files.Count && i < percents.Length; i++)
            {
                var f = vm.Generate.Files[i];
                f.BytesTotal = Math.Max(f.Size, 1);
                f.BytesDone = (long)(f.BytesTotal * percents[i]);
                f.Progress = (int)percents[i];
                f.State = GenerateItemState.Running;
            }
        });

        Shoot(Path.Combine(outputDirectory, "13-remove-file.png"), ThemeMode.Light, vm =>
        {
            // 点 × 移除一个文件：弹轻提示，列表实时收缩，状态栏消息保持不变
            vm.SelectedTabIndex = 1;
            vm.Generate.AddFiles(
            [
                samples.PlainFiles[0],
                samples.PlainFiles[1],
                samples.PlainFiles[2],
            ]);

            vm.Generate.RemoveFileCommand.Execute(vm.Generate.Files[1]);
        });

        Shoot(Path.Combine(outputDirectory, "16-folder-import.png"), ThemeMode.Light, vm =>
        {
            // 模拟「选择文件夹」按钮：传进来的是目录路径，必须展开成文件
            vm.SelectedTabIndex = 1;
            vm.Generate.AddFiles([samples.PlainDir]);
        });

        Shoot(Path.Combine(outputDirectory, "14-verify-remove.png"), ThemeMode.Light, vm =>
        {
            // 校验面板移除一个校验文件：状态栏应显示「已加载 N 个校验文件 · 共 M 条记录」
            // （不能出现两个「已加载」）
            vm.SelectedTabIndex = 0;
            vm.Verify.AddFiles([samples.ChecksumFiles[0], samples.Md5Checksum]);
            vm.Verify.RemoveSourceCommand.Execute(vm.Verify.Sources[0]);
        });

        Shoot(Path.Combine(outputDirectory, "15-generate-ignored.png"), ThemeMode.Light, vm =>
        {
            // 生成面板拖入校验文件：应弹警告 Toast，且不进状态栏
            vm.SelectedTabIndex = 1;
            vm.Generate.AddFiles([samples.ChecksumFiles[0], samples.Md5Checksum]);
        });

        Shoot(Path.Combine(outputDirectory, "15-generate-ignored.png"), ThemeMode.Light, vm =>
        {
            // 生成面板拖入校验文件：应弹警告 Toast，且不进状态栏
            vm.SelectedTabIndex = 1;
            vm.Generate.AddFiles([samples.ChecksumFiles[0], samples.Md5Checksum]);
        });

        CheckGapsAtMinimumSize();

        Console.WriteLine($"截图已输出到 {Path.GetFullPath(outputDirectory)}");
        return 0;
    }

    /// <summary>
    /// 最小窗口尺寸下再量一遍间距。窗口越小，各行的最小高度越容易超过可用高度，
    /// 一旦超过，Grid 会把缺口平摊给每一行、子元素溢出单元格，面板就贴到一起（间距消失）。
    /// 这里专门盯住这个尺寸，生成面板还特意带上进度卡片（多一行，最容易压掉间距）。
    /// </summary>
    private static void CheckGapsAtMinimumSize()
    {
        Console.WriteLine("===== 最小窗口尺寸（940x640）下的面板间距 =====");

        var vm = new MainWindowViewModel { ThemeMode = ThemeMode.Light };
        vm.ApplyTheme(ThemeMode.Light);
        vm.Settings.WindowWidth = 940;
        vm.Settings.WindowHeight = 640;
        vm.SelectedTabIndex = 1;
        vm.Generate.AddFiles([Path.Combine(Path.GetTempPath(), "gap-check.mp4")]);
        vm.Generate.IsRunning = true;
        Pump();

        Window window = new MainWindow();
        window.DataContext = vm;
        window.Show();
        Pump();

        ReportPanelGaps(window);

        window.Close();
        Pump();
    }

    // ------------------------------------------------------------------ 渲染

    private static void Shoot(
        string path,
        ThemeMode theme,
        Action<MainWindowViewModel> prepare,
        Func<MainWindowViewModel, Window>? windowFactory = null,
        bool openSettingsMenu = false,
        bool blurAfterShow = false)
    {
        var vm = new MainWindowViewModel { ThemeMode = theme };
        vm.ApplyTheme(theme);

        // 固定窗口尺寸与语言，避免上一次截图写入的设置影响后续渲染
        vm.Settings.WindowWidth = 1120;
        vm.Settings.WindowHeight = 800;
        vm.LanguageIndex = 0;

        prepare(vm);
        Pump();

        Window window = windowFactory?.Invoke(vm) ?? new MainWindow();
        window.DataContext = vm;
        window.Show();
        Pump();

        if (openSettingsMenu)
        {
            OpenFirstSelectMenu(window);
        }

        if (blurAfterShow)
        {
            VerifyTextBoxBlur(window);
        }

        var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine($"渲染失败：{path}");
        }
        else
        {
            frame.Save(path);
            Console.WriteLine($"已生成 {path}");
        }

        ReportPanelGaps(window);

        window.Close();
        Pump();
    }

    /// <summary>
    /// 量出各面板内部「拖拽区 / 列表 / 输出设置 / 进度」之间的上下间距。
    /// Grid 行被压缩时子元素会溢出自己的单元格，面板就贴到一起（间距消失），
    /// 这里直接把间距打出来，小于 10 就报警。
    /// </summary>
    private static void ReportPanelGaps(Window window)
    {
        foreach (var view in window.GetVisualDescendants().OfType<UserControl>())
        {
            string name = view.GetType().Name;
            if (name is not ("GenerateView" or "VerifyView" or "MergeView") || view.Bounds.Height <= 0)
            {
                continue;
            }

            // UserControl 的模板里还隔着一层 ContentPresenter，直接取第一个 Grid 后代即根 Grid
            if (view.GetVisualDescendants().OfType<Grid>().FirstOrDefault() is not { } grid)
            {
                continue;
            }

            var panels = grid.Children
                .OfType<Control>()
                .Where(control => control.IsVisible && control.Bounds.Height > 0)
                .OrderBy(control => control.Bounds.Y)
                .ToList();

            var gaps = new List<string>();
            bool collapsed = false;

            for (int i = 1; i < panels.Count; i++)
            {
                double gap = panels[i].Bounds.Y - (panels[i - 1].Bounds.Y + panels[i - 1].Bounds.Height);
                gaps.Add($"{gap:0.#}");
                collapsed |= gap < 10;
            }

            Console.WriteLine(
                $"    {name} 面板间距：{string.Join(" / ", gaps)}" +
                (collapsed ? "  ← 间距被压掉，检查行的最小高度是否超出可用高度" : string.Empty));
        }
    }

    /// <summary>展开设置页里的第一个下拉，用于核对弹出面板的宽度与间距。</summary>
    private static void OpenFirstSelectMenu(Window window)
    {
        var select = window.GetVisualDescendants().OfType<SelectBox>().FirstOrDefault();
        var button = select?.GetVisualDescendants().OfType<Button>().FirstOrDefault();

        if (button?.Flyout is not Flyout flyout)
        {
            return;
        }

        flyout.ShowAt(button);
        Pump();

        // 打印对齐情况：菜单左边缘应与选择框左边缘一致
        var menu = flyout.Content is Control content
            ? content.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "MenuHost")
            : null;

        if (menu is not null && select is not null)
        {
            var selectOrigin = select.TranslatePoint(new Point(0, 0), window) ?? default;
            var menuOrigin = menu.TranslatePoint(new Point(0, 0), window) ?? default;

            var presenter = menu.GetVisualAncestors().OfType<FlyoutPresenter>().FirstOrDefault();
            string presenterInfo = "presenter=null";

            if (presenter is not null)
            {
                var presenterOrigin = presenter.TranslatePoint(new Point(0, 0), window) ?? default;
                presenterInfo =
                    $"presenter left={presenterOrigin.X:0.#} width={presenter.Bounds.Width:0.#} " +
                    $"padding={presenter.Padding} 内容相对弹出层偏移={menuOrigin.X - presenterOrigin.X:0.#}";
            }

            Console.WriteLine(
                $"    选择框 left={selectOrigin.X:0.#} width={select.Bounds.Width:0.#} | " +
                $"菜单 left={menuOrigin.X:0.#} width={menu.Bounds.Width:0.#} | " +
                $"左边缘偏移={menuOrigin.X - selectOrigin.X:0.#} | {presenterInfo}");
        }
    }

    /// <summary>聚焦输入框后点击空白处，验证焦点样式是否被正确清除。</summary>
    private static void VerifyTextBoxBlur(Window window)
    {
        var textBox = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (textBox is null)
        {
            return;
        }

        textBox.Focus();
        Pump();

        // 无头平台不会派发鼠标输入，这里直接构造指针事件投递给窗口，
        // 验证「点击输入框以外区域 → 释放焦点」的逻辑确实生效。
        var point = new Point(560, 680);
        var args = new PointerPressedEventArgs(
            window,
            new Pointer(1, PointerType.Mouse, true),
            window,
            point,
            0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None);

        window.RaiseEvent(args);
        Pump();

        Console.WriteLine($"    点击空白后输入框已失焦：{!textBox.IsFocused}");
    }


    /// <summary>端到端性能基准：生成 + 校验各跑一轮，定位瓶颈在哪一层。</summary>
    private static void RunBenchmark(Samples samples)
    {
        // 用 4 个 256MB 文件凑到 1GB，避免长时间占用磁盘
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-bench");
        Directory.CreateDirectory(dir);

        var files = new List<string>();
        for (int i = 1; i <= 4; i++)
        {
            string f = Path.Combine(dir, $"bench{i}.bin");
            if (!File.Exists(f) || new FileInfo(f).Length != 256L * 1024 * 1024)
            {
                using var fs = File.Create(f);
                var rnd = new Random(i);
                byte[] chunk = new byte[1024 * 1024];
                for (int j = 0; j < 256; j++)
                {
                    rnd.NextBytes(chunk);
                    fs.Write(chunk);
                }
            }
            files.Add(f);
        }

        double totalGb = files.Sum(f => new FileInfo(f).Length) / 1024.0 / 1024 / 1024;
        Console.WriteLine($"\n===== 基准：{files.Count} 个文件，共 {totalGb:F2} GB =====");

        // ---------- 生成 ----------
        var genVm = new GenerateViewModel(
            new AppSettings(),
            (_, _) => { });
        genVm.AddFiles(files);

        var swGen = Stopwatch.StartNew();
        RunToCompletion(() => genVm.StartCommand.Execute(null), () => genVm.IsRunning);
        swGen.Stop();
        Console.WriteLine($"  生成 CRC64：{swGen.Elapsed.TotalSeconds:F2}s → {totalGb / swGen.Elapsed.TotalSeconds * 1024,7:N0} MB/s");

        // ---------- 校验 ----------
        string checksum = Path.Combine(dir, checksumFileName(files));
        var verVm = new VerifyViewModel(
            new AppSettings(),
            (_, _) => { });
        verVm.AddFiles([checksum]);

        // 预热磁盘缓存，只测计算本身
        RunToCompletion(() => verVm.StartCommand.Execute(null), () => verVm.IsRunning);

        swGen = Stopwatch.StartNew();
        RunToCompletion(() => verVm.StartCommand.Execute(null), () => verVm.IsRunning);
        swGen.Stop();
        Console.WriteLine($"  校验 CRC64：{swGen.Elapsed.TotalSeconds:F2}s → {totalGb / swGen.Elapsed.TotalSeconds * 1024,7:N0} MB/s");

        Directory.Delete(dir, recursive: true);
    }

    private static string checksumFileName(List<string> files)
    {
        string first = Path.GetFileNameWithoutExtension(files[0]);
        string last = Path.GetFileNameWithoutExtension(files[^1]);
        return $"{first} - {last}.crc64";
    }

    /// <summary>驱动无头渲染循环，直到窗口完成一次绘制。</summary>
    private static void Pump()
    {
        for (int i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>执行一个异步命令并等待其结束（同时驱动 UI 线程的消息泵）。</summary>
    private static void RunToCompletion(Action start, Func<bool> isRunning)
    {
        start();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (isRunning() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(5);
        }

        Pump();
    }

    // ------------------------------------------------------------------ 样例数据

    private sealed record Samples(
        List<string> PlainFiles,
        string PlainDir,
        List<string> ChecksumFiles,
        string EmptyFile,
        string Md5Checksum);

    private static Samples PrepareSamples(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);

        // 23 个真实文件 + 1 条指向已删除文件的记录 = 24 条记录
        (string Name, int Size)[] specs = Enumerable.Range(1, 22)
            .Select(i => ($"演示视频 第{i:00}集.mp4", 180_000 + i * 9_000))
            .Append(("素材归档.zip", 640_000))
            .ToArray();

        var random = new Random(20261013);
        var plainFiles = new List<string>();

        foreach (var (name, size) in specs)
        {
            string path = Path.Combine(directory, name);
            var buffer = new byte[size];
            random.NextBytes(buffer);
            File.WriteAllBytes(path, buffer);
            plainFiles.Add(path);
        }

        // 主校验文件：22 条通过 + 1 条不匹配 + 1 条未找到关联文件
        var sb = new StringBuilder();
        for (int i = 0; i < plainFiles.Count; i++)
        {
            string hex = i == 3 ? Flip(Hash(plainFiles[i])) : Hash(plainFiles[i]);
            sb.Append(ChecksumFile.FormatLine(hex, Path.GetFileName(plainFiles[i]), NumberFormat.Decimal))
              .Append('\n');
        }

        sb.Append(ChecksumFile.FormatLine(Flip(Hash(plainFiles[0])), "演示视频 第23集（已删除）.mp4", NumberFormat.Decimal))
          .Append('\n');

        string mainChecksum = Path.Combine(directory, "演示视频合集 1-24.crc64");
        File.WriteAllText(mainChecksum, sb.ToString(), new UTF8Encoding(false));

        // 第二个校验文件：MD5，用于演示合并面板
        var md5Sb = new StringBuilder();
        foreach (string file in plainFiles.Take(3))
        {
            string hex = Hash(file, HashKind.Md5);
            md5Sb.Append(ChecksumFile.FormatLine(hex, Path.GetFileName(file), NumberFormat.Hex)).Append('\n');
        }

        string md5Checksum = Path.Combine(directory, "素材归档.md5");
        File.WriteAllText(md5Checksum, md5Sb.ToString(), new UTF8Encoding(false));

        // 再生成一份 CRC64 供合并使用（与主文件有重复项）
        var secondSb = new StringBuilder();
        foreach (string file in plainFiles.Skip(2))
        {
            secondSb.Append(ChecksumFile.FormatLine(Hash(file), Path.GetFileName(file), NumberFormat.Decimal))
                    .Append('\n');
        }

        string secondChecksum = Path.Combine(directory, "补充校验.crc64");
        File.WriteAllText(secondChecksum, secondSb.ToString(), new UTF8Encoding(false));

        // 0 字节文件：用于核对列表里不显示「0 B」
        string emptyFile = Path.Combine(directory, "空文件.bin");
        File.WriteAllBytes(emptyFile, []);

        string plainDir = Path.GetDirectoryName(plainFiles[0])!;
        return new Samples(
            plainFiles,
            plainDir,
            [mainChecksum, secondChecksum],
            emptyFile,
            md5Checksum);
    }

    private static string Hash(string path, HashKind kind = HashKind.Crc64)
        => HashEngine.ComputeAsync(path, kind, null, CancellationToken.None).GetAwaiter().GetResult().Hex;

    /// <summary>翻转最高位，制造一条不匹配的记录。</summary>
    private static string Flip(string hex)
    {
        char first = hex[0];
        char flipped = first switch
        {
            '0' => '1',
            '1' => '0',
            _ => '0',
        };

        return flipped + hex[1..];
    }
}
