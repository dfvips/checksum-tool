using CheckSumTool.Services;
using CheckSumTool.ViewModels;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>
/// 合并面板混入不同算法时的警告提示：每次追加都要提示，
/// 不能因为「之前提示过」或「清空过列表」就沉默。
/// </summary>
public class MergeConflictToastTests
{
    private static MergeViewModel Create(List<string> warnings)
        => new(
            new AppSettings(),
            (message, isWarning) =>
            {
                if (isWarning)
                {
                    warnings.Add(message);
                }
            });

    private static string P(string name) => Path.Combine(Path.GetTempPath(), "crc64tool-merge", name);

    [Fact]
    public void WarningFiresWhenKindsMix()
    {
        var warnings = new List<string>();
        var vm = Create(warnings);

        vm.AddFiles([P("a.crc64")]);
        Assert.Empty(warnings);

        vm.AddFiles([P("b.md5")]);
        Assert.Single(warnings);
    }

    [Fact]
    public void WarningFiresAgainAfterClear()
    {
        var warnings = new List<string>();
        var vm = Create(warnings);

        vm.AddFiles([P("a.crc64"), P("b.md5")]);
        Assert.Single(warnings);

        vm.ClearCommand.Execute(null);
        vm.AddFiles([P("a.crc64")]);
        vm.AddFiles([P("b.md5")]);

        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void WarningFiresOnAppendWhileAlreadyConflicting()
    {
        var warnings = new List<string>();
        var vm = Create(warnings);

        vm.AddFiles([P("a.crc64"), P("b.md5")]);
        Assert.Single(warnings);

        vm.AddFiles([P("c.crc64")]);

        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void NoWarningWhenAllSameKind()
    {
        var warnings = new List<string>();
        var vm = Create(warnings);

        vm.AddFiles([P("a.crc64"), P("b.crc64"), P("c.md5")]);
        Assert.Single(warnings);

        vm.ClearCommand.Execute(null);
        vm.AddFiles([P("a.md5"), P("b.md5")]);

        Assert.Single(warnings);
    }

    [Fact]
    public void NoWarningWhenNothingAdded()
    {
        var warnings = new List<string>();
        var vm = Create(warnings);

        vm.AddFiles([P("a.crc64"), P("b.md5")]);
        int before = warnings.Count;

        // 重复拖入同一批文件（不会新增）不应重复提示
        vm.AddFiles([P("a.crc64"), P("b.md5")]);

        Assert.Equal(before, warnings.Count);
    }

    [Fact]
    public void NoWarningForRejectedFiles()
    {
        var warnings = new List<string>();
        var vm = Create(warnings);

        vm.AddFiles([P("a.mp4"), P("b.txt")]);

        Assert.Empty(warnings);
        Assert.Empty(vm.Files);
    }
}
