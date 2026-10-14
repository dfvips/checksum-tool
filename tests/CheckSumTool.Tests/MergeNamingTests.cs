using CheckSumTool.Core;
using CheckSumTool.Services;
using CheckSumTool.ViewModels;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>
/// 合并面板的输出文件名：默认留空，导出时才按「第一个文件名 - 最后一个文件名」生成。
/// </summary>
public class MergeNamingTests
{
    private static MergeViewModel Create() => new(new AppSettings(), (_, _) => { });

    private static string P(string name) => Path.Combine(Path.GetTempPath(), "crc64tool-merge", name);

    [Fact]
    public void NameIsBlankByDefault()
    {
        var vm = Create();
        Assert.Equal(string.Empty, vm.OutputBaseName);
    }

    [Fact]
    public void SingleFileUsesItsOwnName()
    {
        var vm = Create();
        vm.AddFiles([P("古相思曲 第01集.crc64")]);

        var paths = vm.ResolveOutputPaths();

        Assert.Equal([Path.Combine(Path.GetTempPath(), "crc64tool-merge", "古相思曲 第01集.crc64")], paths);
    }

    [Fact]
    public void MultipleFilesUseFirstAndLastName()
    {
        var vm = Create();
        vm.AddFiles([P("a.crc64"), P("b.crc64"), P("c.crc64")]);

        var paths = vm.ResolveOutputPaths();

        Assert.Equal([Path.Combine(Path.GetTempPath(), "crc64tool-merge", "a - c.crc64")], paths);
    }

    [Fact]
    public void ExplicitNameWins()
    {
        var vm = Create();
        vm.AddFiles([P("a.crc64"), P("c.crc64")]);

        vm.OutputBaseName = "我的合集";

        var paths = vm.ResolveOutputPaths();

        Assert.Equal([Path.Combine(Path.GetTempPath(), "crc64tool-merge", "我的合集.crc64")], paths);
    }

    [Fact]
    public void NoFilesProducesNoPaths()
    {
        var vm = Create();
        Assert.Empty(vm.ResolveOutputPaths());
    }

    [Fact]
    public void LongDefaultNameIsTruncated()
    {
        var vm = Create();
        vm.AddFiles([P(new string('x', 200) + ".crc64"), P(new string('y', 200) + ".crc64")]);

        var paths = vm.ResolveOutputPaths();

        string fileName = Path.GetFileName(paths[0]);
        Assert.True(fileName.Length <= 120 + ".crc64".Length, $"文件名过长：{fileName.Length}");
    }

    // ------------------------------------------------------------ 跨文件夹：按文件夹分别合并

    [Fact]
    public void EachFolderIsMergedIntoItsOwnFile()
    {
        var vm = Create();
        vm.AddFiles([
            Path.Combine(Path.GetTempPath(), "crc64tool-merge", "A", "a1.crc64"),
            Path.Combine(Path.GetTempPath(), "crc64tool-merge", "A", "a2.crc64"),
            Path.Combine(Path.GetTempPath(), "crc64tool-merge", "B", "b1.crc64"),
        ]);

        // 每个文件夹一份：A 收 a1/a2，B 收 b1
        Assert.Equal(
            [
                Path.Combine(Path.GetTempPath(), "crc64tool-merge", "A", "a1 - a2.crc64"),
                Path.Combine(Path.GetTempPath(), "crc64tool-merge", "B", "b1.crc64"),
            ],
            vm.ResolveOutputPaths());
    }

    [Fact]
    public void ManualNameAppliesToEveryFolder()
    {
        var vm = Create();
        vm.AddFiles([
            Path.Combine(Path.GetTempPath(), "crc64tool-merge", "A", "a1.crc64"),
            Path.Combine(Path.GetTempPath(), "crc64tool-merge", "B", "b1.crc64"),
        ]);
        vm.OutputBaseName = "我的合集";

        Assert.Equal(
            [
                Path.Combine(Path.GetTempPath(), "crc64tool-merge", "A", "我的合集.crc64"),
                Path.Combine(Path.GetTempPath(), "crc64tool-merge", "B", "我的合集.crc64"),
            ],
            vm.ResolveOutputPaths());
    }

    [Fact]
    public void ExplicitPathStillProducesOneFile()
    {
        var vm = Create();
        vm.AddFiles([
            Path.Combine(Path.GetTempPath(), "crc64tool-merge", "A", "a1.crc64"),
            Path.Combine(Path.GetTempPath(), "crc64tool-merge", "B", "b1.crc64"),
        ]);
        vm.OutputBaseName = Path.Combine(Path.GetTempPath(), "crc64tool-merge", "out", "全部");

        Assert.Equal(
            [Path.Combine(Path.GetTempPath(), "crc64tool-merge", "out", "全部.crc64")],
            vm.ResolveOutputPaths());
    }

    [Fact]
    public void MergesEachFolderSeparatelyWithoutMixingRecords()
    {
        string root = Path.Combine(Path.GetTempPath(), "crc64tool-mergeperfolder", Guid.NewGuid().ToString("N"));
        string dirA = Path.Combine(root, "A");
        string dirB = Path.Combine(root, "B");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        try
        {
            File.WriteAllText(Path.Combine(dirA, "one.crc64"), "1 *a1.bin\n");
            File.WriteAllText(Path.Combine(dirA, "two.crc64"), "2 *a2.bin\n");
            File.WriteAllText(Path.Combine(dirB, "three.crc64"), "3 *b1.bin\n");

            var vm = new MergeViewModel(new AppSettings { MergeDeduplicate = false }, (_, _) => { });
            vm.AddFiles([Path.Combine(dirA, "one.crc64"), Path.Combine(dirA, "two.crc64"), Path.Combine(dirB, "three.crc64")]);
            vm.MergeCommand.Execute(null);

            // A：两个校验文件合成一份，只含 A 的记录
            var mergedA = ChecksumFile.Parse(Path.Combine(dirA, "one - two.crc64"));
            Assert.Equal(["a1.bin", "a2.bin"], mergedA.Entries.Select(e => e.FileName));

            // B：单独一份，只含 B 的记录（不把 A 的记录带过来）
            var mergedB = ChecksumFile.Parse(Path.Combine(dirB, "three.crc64"));
            Assert.Equal(["b1.bin"], mergedB.Entries.Select(e => e.FileName));

            Assert.Equal("StatusMergedMany", vm.StatusKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
