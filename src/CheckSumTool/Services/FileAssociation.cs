using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using CheckSumTool.Core;
using CheckSumTool.Localization;

namespace CheckSumTool.Services;

/// <summary>
/// Windows 文件关联：把全部校验文件扩展名（<c>.crc64</c> / <c>.md5</c> / <c>.sha1</c> /
/// <c>.sha256</c> / <c>.sha384</c> / <c>.sha512</c> / <c>.sha3-256</c> / <c>.sha3-384</c> /
/// <c>.sha3-512</c> / <c>.crc32</c> / <c>.sfv</c>）关联到本程序。只写 <c>HKCU\Software\Classes</c>，
/// 无需管理员权限；注册后双击校验文件即可打开本程序并直接开始校验。
/// 应用条目写入 <c>FriendlyAppName</c>，资源管理器「打开方式」里显示的是界面同款程序名，
/// 切换界面语言后会同步刷新。
/// </summary>
public static class FileAssociation
{
    private static readonly (string Extension, string ProgId, string Description)[] KnownTypes =
    [
        (ChecksumFile.Crc64Extension,    "CheckSumTool.crc64",    "CRC64 校验文件"),
        (ChecksumFile.Md5Extension,      "CheckSumTool.md5",      "MD5 校验文件"),
        (ChecksumFile.Sha1Extension,     "CheckSumTool.sha1",     "SHA-1 校验文件"),
        (ChecksumFile.Sha256Extension,   "CheckSumTool.sha256",   "SHA-256 校验文件"),
        (ChecksumFile.Sha384Extension,   "CheckSumTool.sha384",   "SHA-384 校验文件"),
        (ChecksumFile.Sha512Extension,   "CheckSumTool.sha512",   "SHA-512 校验文件"),
        (ChecksumFile.Sha3_256Extension, "CheckSumTool.sha3-256", "SHA3-256 校验文件"),
        (ChecksumFile.Sha3_384Extension, "CheckSumTool.sha3-384", "SHA3-384 校验文件"),
        (ChecksumFile.Sha3_512Extension, "CheckSumTool.sha3-512", "SHA3-512 校验文件"),
        (ChecksumFile.Crc32Extension,    "CheckSumTool.crc32",    "CRC-32 校验文件"),
        (ChecksumFile.Crc32AltExtension, "CheckSumTool.sfv",      "SFV 校验文件（CRC-32）"),
    ];

    private const string OpenVerbLabel = "打开(&O)";

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

            // 「打开方式」里显示的程序名跟随界面语言（简体中文为「文件校验工具」）
            string appName = Localizer.Instance["AppTitle"];
            bool changed = false;

            foreach (var (extension, progId, description) in KnownTypes)
            {
                changed |= Register(extension, progId, description, exe);
            }

            changed |= RegisterApplication(exe, appName);

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

    /// <summary>写入单个扩展名的关联项，返回是否发生了变更。</summary>
    [SupportedOSPlatform("windows")]
    private static bool Register(string extension, string progId, string description, string exe)
    {
        string command = $"\"{exe}\" --verify \"%1\"";
        string icon = $"\"{exe}\",0";
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
            if (progKey.GetValue(null) as string != description)
            {
                progKey.SetValue(null, description);
                changed = true;
            }

            using (var iconKey = progKey.CreateSubKey("DefaultIcon"))
            {
                if (iconKey.GetValue(null) as string != icon)
                {
                    iconKey.SetValue(null, icon);
                    changed = true;
                }
            }

            using (var openKey = progKey.CreateSubKey(@"shell\open"))
            {
                if (openKey.GetValue(null) as string != OpenVerbLabel)
                {
                    openKey.SetValue(null, OpenVerbLabel);
                    changed = true;
                }
            }

            using (var commandKey = progKey.CreateSubKey(@"shell\open\command"))
            {
                if (commandKey.GetValue(null) as string != command)
                {
                    commandKey.SetValue(null, command);
                    changed = true;
                }
            }
        }

        // 让「打开方式」列表里也能看到本程序
        using (var supported = Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\Applications\{Path.GetFileName(exe)}\SupportedTypes"))
        {
            if (supported.GetValue(extension) is null)
            {
                supported.SetValue(extension, string.Empty);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// 写入应用条目：<c>FriendlyAppName</c> 让「打开方式」与安装程序列表显示界面同款程序名，
    /// 否则只会看到 <c>CheckSumTool.exe</c>。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool RegisterApplication(string exe, string appName)
    {
        bool changed = false;

        using (var appKey = Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\Applications\{Path.GetFileName(exe)}"))
        {
            if (appKey.GetValue("FriendlyAppName") as string != appName)
            {
                appKey.SetValue("FriendlyAppName", appName);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>通知资源管理器刷新图标与关联，否则需要注销或重启才生效。</summary>
    private static void NotifyShell()
        => SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
