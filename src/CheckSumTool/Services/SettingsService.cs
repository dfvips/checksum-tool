using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CheckSumTool.Core;
using CheckSumTool.Localization;

namespace CheckSumTool.Services;

/// <summary>界面主题模式。</summary>
public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// 持久化的用户偏好设置。实现变更通知，界面与各面板可直接订阅。
/// </summary>
public sealed partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private ThemeMode _theme = ThemeMode.System;

    [ObservableProperty]
    private Language _language = Language.SimplifiedChinese;

    /// <summary>是否跟随系统语言（启动与切换语言时自动按系统区域选择）。</summary>
    [ObservableProperty]
    private bool _followSystemLanguage = true;

    /// <summary>同时处理的文件数（1~5）。</summary>
    [ObservableProperty]
    private int _parallelism = 3;

    /// <summary>CRC-32 输出时使用的扩展名，生成页由 `.crc32` / `.sfv` 格式复选项单独选择。</summary>
    [ObservableProperty]
    private string _crc32Extension = ChecksumFile.Crc32Extension;

    [ObservableProperty]
    private bool _mergeDeduplicate = true;

    [ObservableProperty]
    private bool _mergeSort = true;

    [ObservableProperty]
    private double _windowWidth = 1100;

    [ObservableProperty]
    private double _windowHeight = 800;

    /// <summary>允许的并发数上限。</summary>
    public const int MaxParallelism = 8;

    partial void OnParallelismChanged(int value)
    {
        int clamped = Math.Clamp(value, 1, MaxParallelism);
        if (clamped != value)
        {
            Parallelism = clamped;
        }
    }
}

/// <summary>读写 <c>%APPDATA%/CheckSumTool/settings.json</c>（各平台等价目录）。</summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string? _cachedDirectory;

    /// <summary>可用环境变量 <c>CHECKSUMTOOL_SETTINGS_DIR</c> 重定向设置目录（供预览/测试使用）。</summary>
    public static string SettingsDirectory
    {
        get
        {
            if (_cachedDirectory is not null)
            {
                return _cachedDirectory;
            }

            string? overridden = Environment.GetEnvironmentVariable("CHECKSUMTOOL_SETTINGS_DIR");
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                _cachedDirectory = overridden;
                return _cachedDirectory;
            }

            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config");
            }

            _cachedDirectory = Path.Combine(root, "CheckSumTool");
            return _cachedDirectory;
        }
    }

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded is not null)
                {
                    loaded.Parallelism = Math.Clamp(loaded.Parallelism, 1, AppSettings.MaxParallelism);
                    return loaded;
                }
            }
        }
        catch
        {
            // 配置损坏时静默回退到默认值。
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // 写入失败不影响主流程。
        }
    }
}
