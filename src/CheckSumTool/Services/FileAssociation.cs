using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CheckSumTool.Services;

/// <summary>
/// Windows 文件关联：把 <c>.crc64</c> / <c>.md5</c> 关联到本程序。
/// 只写 <c>HKCU\Software\Classes</c>，无需管理员权限；
/// 注册后双击校验文件即可打开本程序并直接开始校验。
/// </summary>
public static class FileAssociation
{
    private const string Crc64ProgId = "CheckSumTool.crc64";
    private const string Md5ProgId = "CheckSumTool.md5";

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    /// <summary>
    /// 确保扩展名已关联到当前可执行文件。已关联时直接返回，不产生任何写入。
    /// </summary>
    public static void EnsureRegistered()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            string? exe = Environment.ProcessPath;

            // 单文件发布时 ProcessPath 指向真实 exe；开发环境下是 dotnet 宿主，跳过
            if (string.IsNullOrEmpty(exe)
                || !File.Exists(exe)
                || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool changed = false;
            changed |= Register(".crc64", Crc64ProgId, "CRC64 校验文件", exe);
            changed |= Register(".md5", Md5ProgId, "MD5 校验文件", exe);

            if (changed)
            {
                NotifyShell();
            }
        }
        catch
        {
            // 关联失败不影响程序本身的功能
        }
    }

    /// <summary>写入关联项，返回是否发生了变更。</summary>
    [SupportedOSPlatform("windows")]
    private static bool Register(string extension, string progId, string description, string exe)
    {
        string command = $"\"{exe}\" --verify \"%1\"";
        bool changed = false;

        using (var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}"))
        {
            if (extKey.GetValue(null) as string != progId)
            {
                extKey.SetValue(null, progId);
                changed = true;
            }
        }

        using (var progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
        {
            progKey.SetValue(null, description);

            using var iconKey = progKey.CreateSubKey("DefaultIcon");
            iconKey.SetValue(null, $"\"{exe}\",0");

            using var openKey = progKey.CreateSubKey(@"shell\open");
            openKey.SetValue(null, "打开(&O)");

            using var commandKey = progKey.CreateSubKey(@"shell\open\command");
            if (commandKey.GetValue(null) as string != command)
            {
                commandKey.SetValue(null, command);
                changed = true;
            }
        }

        // 让「打开方式」列表里也能看到本程序
        using (var supported = Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\Applications\{Path.GetFileName(exe)}\SupportedTypes"))
        {
            supported.SetValue(extension, string.Empty);
        }

        return changed;
    }

    /// <summary>通知资源管理器刷新图标与关联，否则需要注销或重启才生效。</summary>
    private static void NotifyShell()
        => SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
