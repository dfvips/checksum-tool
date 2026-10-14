using CheckSumTool.Services;
using CheckSumTool.ViewModels;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>
/// 从列表移除文件后：弹轻提示反馈，同时**刷新底部状态栏**——
/// 列表还有文件就显示当前汇总，空了则回到默认文案。
/// </summary>
public class RemoveFileToastTests
{
    private static string P(string name) => Path.Combine(Path.GetTempPath(), "crc64tool-remove", name);

    [Fact]
    public void GeneratePanelShowsToastWithoutTouchingStatusBar()
    {
        var statusKeys = new List<string>();
        var toasts = new List<string>();
        var vm = new GenerateViewModel(
            new AppSettings(),
            (message, _) => toasts.Add(message));

        vm.AddFiles([P("a.mp4"), P("b.mp4")]);

        vm.RemoveFileCommand.Execute(vm.Files[0]);

        Assert.Equal("StatusFilesLoaded", vm.StatusKey);
        Assert.Single(toasts);
        Assert.Contains("a.mp4", toasts[0]);
        Assert.Single(vm.Files);
    }

    [Fact]
    public void VerifyPanelShowsToastWithoutTouchingStatusBar()
    {
        var statusKeys = new List<string>();
        var toasts = new List<string>();
        var vm = new VerifyViewModel(
            new AppSettings(),
            (message, _) => toasts.Add(message));

        vm.AddFiles([P("a.crc64"), P("b.crc64")]);

        vm.RemoveSourceCommand.Execute(vm.Sources[0]);

        Assert.Equal("StatusFilesLoaded", vm.StatusKey);
        Assert.Single(toasts);
        Assert.Contains("a.crc64", toasts[0]);
        Assert.Single(vm.Sources);
    }

    [Fact]
    public void MergePanelShowsToastWithoutTouchingStatusBar()
    {
        var statusKeys = new List<string>();
        var toasts = new List<string>();
        var vm = new MergeViewModel(
            new AppSettings(),
            (message, _) => toasts.Add(message));

        vm.AddFiles([P("a.crc64"), P("b.crc64")]);

        vm.RemoveFileCommand.Execute(vm.Files[0]);

        Assert.Equal("StatusFilesLoaded", vm.StatusKey);
        Assert.Single(toasts);
        Assert.Contains("a.crc64", toasts[0]);
        Assert.Single(vm.Files);
    }

    [Fact]
    public void RemovingLastFileResetsStatusToDefault()
    {
        var statusKeys = new List<string>();
        var toasts = new List<string>();
        var vm = new GenerateViewModel(
            new AppSettings(),
            (message, _) => toasts.Add(message));

        vm.AddFiles([P("only.mp4")]);
        vm.RemoveFileCommand.Execute(vm.Files[0]);

        Assert.Empty(vm.Files);
        Assert.Equal("StatusReady", vm.StatusKey);
        Assert.Single(toasts);
        Assert.Contains("only.mp4", toasts[0]);
    }

    [Fact]
    public void ClearingResetsStatusToDefault()
    {
        var statusKeys = new List<string>();
        var vm = new GenerateViewModel(
            new AppSettings(),
            (_, _) => { });

        vm.AddFiles([P("a.mp4"), P("b.mp4")]);
        vm.ClearCommand.Execute(null);

        Assert.Empty(vm.Files);
        Assert.Equal("StatusReady", vm.StatusKey);
    }

    [Fact]
    public void RemovalIsIgnoredWhileRunning()
    {
        var toasts = new List<string>();
        var vm = new GenerateViewModel(
            new AppSettings(),
            (message, _) => toasts.Add(message));

        vm.AddFiles([P("a.mp4")]);
        vm.IsRunning = true;

        vm.RemoveFileCommand.Execute(vm.Files[0]);

        Assert.Single(vm.Files);
        Assert.Empty(toasts);
    }
}
