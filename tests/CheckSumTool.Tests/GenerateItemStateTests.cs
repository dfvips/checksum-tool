using CheckSumTool.Localization;
using CheckSumTool.ViewModels;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>
/// 生成行的状态机：完成后状态必须从「等待中」变为「已完成」，
/// 否则列表会一直显示等待中（曾出现的 bug）。
/// </summary>
public class GenerateItemStateTests
{
    // Localizer.Instance 是全局单例，这里固定为简体中文以避免被其它测试的
    // 语言切换污染（测试禁用了并行，但顺序不固定）。
    public GenerateItemStateTests()
    {
        Localizer.Instance.Language = Language.SimplifiedChinese;
    }

    private static SourceFileItem Item() => new()
    {
        Path = Path.Combine("/tmp", "a.mp4"),
        Size = 1000,
    };

    [Fact]
    public void StartsAsPending()
    {
        var item = Item();

        Assert.Equal(GenerateItemState.Pending, item.State);
        Assert.True(item.ShowStatusText);
        Assert.False(item.IsVerifying);
        Assert.Equal("等待中", item.StatusText);
    }

    [Fact]
    public void RunningShowsProgressBarAndHidesText()
    {
        var item = Item();
        item.State = GenerateItemState.Running;

        Assert.True(item.IsVerifying);
        Assert.False(item.ShowStatusText);
    }

    [Fact]
    public void DoneShowsCompletedText()
    {
        var item = Item();
        item.BytesTotal = 1000;
        item.BytesDone = 400;
        item.State = GenerateItemState.Running;

        item.FinishProgress(GenerateItemState.Done);

        Assert.False(item.IsVerifying);
        Assert.True(item.ShowStatusText);
        Assert.Equal("已完成", item.StatusText);
        Assert.Equal(100, item.Progress);
    }

    [Fact]
    public void FailedShowsFailedText()
    {
        var item = Item();
        item.FinishProgress(GenerateItemState.Failed);

        Assert.Equal("失败", item.StatusText);
        Assert.True(item.ShowStatusText);
        Assert.False(item.IsVerifying);
    }

    [Fact]
    public void CancelledShowsCancelledText()
    {
        var item = Item();
        item.State = GenerateItemState.Running;
        item.FinishProgress(GenerateItemState.Cancelled);

        Assert.Equal("已取消", item.StatusText);
    }

    [Fact]
    public void StateChangesNotifyDerivedProperties()
    {
        var item = Item();
        var changed = new List<string>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        item.State = GenerateItemState.Done;

        Assert.Contains(nameof(item.IsVerifying), changed);
        Assert.Contains(nameof(item.ShowStatusText), changed);
        Assert.Contains(nameof(item.StatusText), changed);
        Assert.Contains(nameof(item.StatusBrush), changed);
    }
}
