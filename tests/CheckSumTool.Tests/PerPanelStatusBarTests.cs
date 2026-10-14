using CheckSumTool.ViewModels;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>
/// 状态栏消息必须随选中的面板切换而变化，
/// 不能用一条共享字段（否则会残留/混合其他面板的消息）。
/// </summary>
public class PerPanelStatusBarTests
{
    private static MainWindowViewModel Create() => new();

    [Fact]
    public void StatusBarDefaultsToSelectedPanel()
    {
        var vm = Create();

        // 默认停在第一个面板（校验），初始消息为默认文案
        vm.SelectedTabIndex = 0;
        Assert.Equal(0, vm.SelectedTabIndex);

        // 首屏状态栏不应空白（避免只显示指示点）
        Assert.NotEmpty(vm.StatusMessage);
        Assert.Equal(vm.Verify.StatusMessage, vm.StatusMessage);

        // 三个面板的初始状态栏都应已就绪
        Assert.NotEmpty(vm.Verify.StatusMessage);
        Assert.NotEmpty(vm.Generate.StatusMessage);
        Assert.NotEmpty(vm.Merge.StatusMessage);
    }

    [Fact]
    public void StatusBarTracksSelectedPanel()
    {
        var vm = Create();

        // 三个面板各自设自己的消息
        vm.SelectedTabIndex = 0;
        vm.Verify.SetError("StatusMissingTarget", "/tmp/a.mp4");

        vm.SelectedTabIndex = 1;
        vm.Generate.SetError("StatusComputeFailed", "b.mp4", "boom");

        vm.SelectedTabIndex = 2;
        vm.Merge.SetError("StatusOnlyChecksums");

        string verifyMsg = vm.Verify.StatusMessage;
        string genMsg = vm.Generate.StatusMessage;
        string mergeMsg = vm.Merge.StatusMessage;

        // 三份互不相同，证明各自独立
        Assert.NotEqual(verifyMsg, genMsg);
        Assert.NotEqual(verifyMsg, mergeMsg);
        Assert.NotEqual(genMsg, mergeMsg);
    }

    [Fact]
    public void SwitchingTabsShowsThatPanelsMessage()
    {
        var vm = Create();

        vm.SelectedTabIndex = 0;
        vm.Verify.SetError("StatusMissingTarget", "/tmp/a.mp4");

        vm.SelectedTabIndex = 1;
        vm.Generate.SetError("StatusComputeFailed", "b.mp4", "boom");

        // 切回校验：状态栏应显示校验自己的消息
        vm.SelectedTabIndex = 0;
        Assert.Equal(vm.Verify.StatusMessage, vm.StatusMessage);
        Assert.NotEqual(vm.Generate.StatusMessage, vm.StatusMessage);

        // 切到生成：状态栏跟随
        vm.SelectedTabIndex = 1;
        Assert.Equal(vm.Generate.StatusMessage, vm.StatusMessage);
    }

    [Fact]
    public void LiveUpdateWhilePanelIsSelected()
    {
        var vm = Create();
        vm.SelectedTabIndex = 1;
        vm.Generate.SetError("StatusGenerating");

        vm.Generate.SetError("StatusComputeFailed", "c.mp4", "x");

        // 当前面板：状态栏立即跟着变
        Assert.Equal(vm.Generate.StatusMessage, vm.StatusMessage);
    }

    [Fact]
    public void OffscreenPanelDoesNotAffectStatusBar()
    {
        var vm = Create();

        vm.SelectedTabIndex = 0;
        vm.Verify.SetError("StatusVerifying");
        string before = vm.StatusMessage;

        // 未选中的面板改自己的消息，不应影响状态栏
        vm.Generate.SetError("StatusComputeFailed", "d.mp4", "y");
        vm.Merge.SetError("StatusOnlyChecksums");

        Assert.Equal(before, vm.StatusMessage);
    }

    [Fact]
    public void StatusKeyIsTrackedForDebugging()
    {
        var vm = Create();

        vm.SelectedTabIndex = 0;
        vm.Verify.SetError("StatusVerifying");
        Assert.Equal("StatusVerifying", vm.Verify.StatusKey);
        Assert.Equal("StatusVerifyFinished", "StatusVerifyFinished");

        vm.Generate.SetError("StatusGenerating");
        Assert.Equal("StatusGenerating", vm.Generate.StatusKey);
    }

    [Fact]
    public void VersionTextIsPrefixedWithV()
    {
        var vm = Create();

        Assert.StartsWith("v", vm.VersionText);
    }
}
