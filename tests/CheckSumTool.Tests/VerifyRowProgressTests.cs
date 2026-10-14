using CheckSumTool.Localization;
using CheckSumTool.ViewModels;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>校验行的「单文件进度条」状态机。</summary>
public class VerifyRowProgressTests
{
    private static VerifyResultItem Row() => new()
    {
        FileName = "a.mp4",
        SourceName = "a.crc64",
        Expected = "0",
    };

    [Fact]
    public void FreshRowShowsPendingText()
    {
        var row = Row();

        Assert.True(row.ShowStatusText);
        Assert.False(row.IsVerifying);
        Assert.Equal(0, row.Progress);
    }

    [Fact]
    public void VerifyingRowHidesTextSoProgressBarCanTakeOver()
    {
        var row = Row();
        row.IsVerifying = true;

        Assert.False(row.ShowStatusText);
        Assert.Equal(string.Empty, row.StatusText);
    }

    [Fact]
    public void StatusTextReturnsAfterVerifying()
    {
        var row = Row();
        row.IsVerifying = true;
        row.Status = VerifyStatus.Ok;
        row.IsVerifying = false;

        Assert.True(row.ShowStatusText);
        Assert.False(string.IsNullOrEmpty(row.StatusText));
    }

    [Fact]
    public void FinishProgressClampsToHundredAndClosesBar()
    {
        var row = Row();
        row.BytesTotal = 1000;
        row.BytesDone = 400;
        row.IsVerifying = true;
        row.Progress = 40;

        row.FinishProgress();

        Assert.Equal(100, row.Progress);
        Assert.False(row.IsVerifying);
        Assert.True(row.ShowStatusText);
    }

    [Fact]
    public void EmptyFileDoesNotShowPercentage()
    {
        var row = Row();
        row.BytesTotal = 0;
        row.IsVerifying = true;

        row.FinishProgress();

        Assert.Equal(0, row.Progress);
    }

    [Fact]
    public void ProgressRangeIsRespected()
    {
        var row = Row();

        row.Progress = -5;
        Assert.Equal(-5, row.Progress);

        row.Progress = 150;
        Assert.Equal(150, row.Progress);
    }

    [Fact]
    public void StatusTextForOkIsLocalized()
    {
        var loc = Localizer.Instance;
        var original = loc.Language;
        var row = Row();
        row.Status = VerifyStatus.Ok;

        try
        {
            loc.Language = Language.SimplifiedChinese;
            Assert.Equal("通过", row.StatusText);

            loc.Language = Language.English;
            Assert.Equal("Passed", row.StatusText);
        }
        finally
        {
            loc.Language = original;
        }
    }
}
