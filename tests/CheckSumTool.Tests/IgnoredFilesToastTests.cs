using CheckSumTool.Localization;
using CheckSumTool.Services;
using CheckSumTool.ViewModels;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>
/// 生成面板拖入 .crc64 / .md5 时的反馈：走警告 Toast（不进状态栏），
/// 且文案只说明「已忽略」即可，不再附带操作指引。
/// </summary>
public class IgnoredFilesToastTests
{
    private static string P(string name) => Path.Combine(Path.GetTempPath(), "crc64tool-ignored", name);

    [Fact]
    public void DroppingChecksumFilesShowsWarningToast()
    {
        var statusKeys = new List<string>();
        var warnings = new List<string>();
        var vm = new GenerateViewModel(
            new AppSettings(),
            (message, isWarning) =>
            {
                if (isWarning)
                {
                    warnings.Add(message);
                }
            });

        vm.AddFiles([P("a.crc64"), P("b.md5")]);

        Assert.Empty(vm.Files);
        Assert.Single(warnings);
        Assert.DoesNotContain("StatusIgnoredChecksums", statusKeys);
    }

    [Fact]
    public void IgnoredToastTextIsShort()
    {
        var loc = Localizer.Instance;
        var original = loc.Language;

        try
        {
            loc.Language = Language.SimplifiedChinese;
            Assert.Equal("已忽略校验文件", loc["ToastIgnoredChecksums"]);

            loc.Language = Language.English;
            Assert.Equal("Checksum files ignored", loc["ToastIgnoredChecksums"]);
        }
        finally
        {
            loc.Language = original;
        }
    }

    [Fact]
    public void NormalFilesStillGoToStatusBar()
    {
        var statusKeys = new List<string>();
        var warnings = new List<string>();
        var vm = new GenerateViewModel(
            new AppSettings(),
            (message, isWarning) =>
            {
                if (isWarning)
                {
                    warnings.Add(message);
                }
            });

        vm.AddFiles([P("a.mp4")]);

        Assert.Single(vm.Files);
        Assert.Equal("StatusFilesLoaded", vm.StatusKey);
        Assert.Empty(warnings);
    }

    [Fact]
    public void MixedDropKeepsFilesAndStillWarns()
    {
        var warnings = new List<string>();
        var vm = new GenerateViewModel(
            new AppSettings(),
            (message, isWarning) =>
            {
                if (isWarning)
                {
                    warnings.Add(message);
                }
            });

        vm.AddFiles([P("a.mp4"), P("b.crc64")]);

        Assert.Single(vm.Files);
        Assert.Single(warnings);
    }
}
