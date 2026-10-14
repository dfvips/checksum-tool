using System.Reflection;
using Avalonia;

namespace CheckSumTool;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 命令行查询：CI 冒烟测试与脚本化调用使用
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--version":
                case "-v":
                    Console.WriteLine($"CheckSumTool {Version}");
                    return 0;

                case "--help":
                case "-h":
                    Console.WriteLine("CheckSumTool - 文件校验工具");
                    Console.WriteLine();
                    Console.WriteLine("用法：直接运行本程序打开图形界面。");
                    Console.WriteLine();
                    Console.WriteLine("  -v, --version   显示版本号");
                    Console.WriteLine("  -h, --help      显示本帮助");
                    return 0;
            }
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static string Version
        => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "1.0.0";

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
