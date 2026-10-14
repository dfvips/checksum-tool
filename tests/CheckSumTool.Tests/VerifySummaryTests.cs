using CheckSumTool.Localization;
using CheckSumTool.Services;
using CheckSumTool.ViewModels;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>校验面板的统计摘要：计数变化必须触发摘要刷新，否则扫描结束仍显示 0。</summary>
public class VerifySummaryTests
{
    // 固定为简体中文，避免被其它测试的语言切换污染。
    public VerifySummaryTests()
    {
        Localizer.Instance.Language = Language.SimplifiedChinese;
    }

    private static VerifyViewModel Create() => new(new AppSettings(), (_, _) => { });

    [Fact]
    public void ResultSummaryRefreshesWhenCountersChange()
    {
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.PassCount = 4;
        vm.FailCount = 1;
        vm.MissingCount = 1;
        vm.TotalCount = 6;

        Assert.Contains(nameof(VerifyViewModel.ResultSummary), changed);
    }

    [Fact]
    public void ResultSummaryReportsActualCounts()
    {
        var vm = Create();
        vm.PassCount = 4;
        vm.FailCount = 1;
        vm.MissingCount = 1;
        vm.TotalCount = 6;

        string summary = vm.ResultSummary;

        Assert.Contains("4", summary);
        Assert.Contains("1", summary);
        Assert.Contains("6", summary);
    }

    [Fact]
    public void ResultSummaryMentionsFileCount()
    {
        var vm = Create();
        vm.TotalCount = 6;

        Assert.Contains("个文件", vm.ResultSummary);
    }

    [Fact]
    public void ResultSummaryFollowsLanguage()
    {
        var localizer = Localizer.Instance;
        var vm = Create();

        try
        {
            localizer.Language = Language.English;
            Assert.Contains("file(s)", vm.ResultSummary);

            localizer.Language = Language.SimplifiedChinese;
            Assert.Contains("个文件", vm.ResultSummary);
        }
        finally
        {
            localizer.Language = Language.SimplifiedChinese;
        }
    }

    /// <summary>
    /// 摘要文案必须是「裸文本」——若自身带「已加载」前缀，套进
    /// <c>StatusFilesLoaded</c>（「已加载 {0}」）后会出现两个「已加载」。
    /// </summary>
    [Theory]
    [InlineData("LoadedSummary")]
    [InlineData("FilesSummary")]
    [InlineData("MergeFilesSummary")]
    public void SummariesAreBareWithoutLoadedPrefix(string key)
    {
        var loc = Localizer.Instance;
        var original = loc.Language;

        try
        {
            foreach (Language language in new[] { Language.SimplifiedChinese, Language.TraditionalChinese, Language.English })
            {
                loc.Language = language;
                string summary = loc.Format(key, 1, 2);

                Assert.DoesNotContain("已加载", summary);
                Assert.DoesNotContain("已載入", summary);
                Assert.DoesNotContain("loaded", summary, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            loc.Language = original;
        }
    }

    [Fact]
    public void StatusMessageDoesNotRepeatLoadedPrefix()
    {
        var loc = Localizer.Instance;
        var original = loc.Language;

        try
        {
            loc.Language = Language.SimplifiedChinese;

            string status = loc.Format("StatusFilesLoaded", loc.Format("LoadedSummary", 1, 2));

            Assert.Equal(1, CountOccurrences(status, "已加载"));
            Assert.Contains("1 个校验文件", status);
            Assert.Contains("2 条记录", status);
        }
        finally
        {
            loc.Language = original;
        }
    }

    private static int CountOccurrences(string text, string token)
    {
        int count = 0;
        int index = 0;

        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    [Fact]
    public void ResultItemTooltipCarriesBothHashes()
    {
        var item = new VerifyResultItem
        {
            FileName = "a.mp4",
            SourceName = "a.crc64",
            Expected = "60b0ad9ce66f6235",
        };

        item.Actual = "60b0ad9ce66f6235";

        Assert.Contains("a.mp4", item.Tooltip);
        Assert.Contains("60b0ad9ce66f6235", item.Tooltip);
    }
}
