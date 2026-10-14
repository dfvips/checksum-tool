using CheckSumTool.Helpers;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>
/// 「选择文件夹」会把**目录路径**传进来，必须先展开成文件，
/// 否则整个目录会被当成一个文件加入列表（表现为"显示了文件夹但开始就结束"）。
/// </summary>
public class ExpandDirectoriesTests : IDisposable
{
    private readonly string _root;

    public ExpandDirectoriesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "crc64tool-expand-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private string Write(string relative)
    {
        string path = Path.Combine(_root, relative);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void PlainFilesPassThrough()
    {
        string a = Write("a.txt");

        Assert.Equal([a], FilePaths.ExpandDirectories([a]));
    }

    [Fact]
    public void DirectoryExpandsToItsFiles()
    {
        Write("mix/b.bin");
        Write("mix/a.bin");
        Write("mix/c.bin");

        var result = FilePaths.ExpandDirectories([Path.Combine(_root, "mix")]);

        // 按文件名字典序，保证列表顺序稳定
        Assert.Equal(
            [
                Path.Combine(_root, "mix", "a.bin"),
                Path.Combine(_root, "mix", "b.bin"),
                Path.Combine(_root, "mix", "c.bin"),
            ],
            result);
    }

    [Fact]
    public void ExpansionIsNotRecursive()
    {
        Write("mix/a.bin");
        Write("mix/sub/deep.bin");

        var result = FilePaths.ExpandDirectories([Path.Combine(_root, "mix")]);

        Assert.Single(result);
        Assert.EndsWith("a.bin", result[0]);
    }

    [Fact]
    public void DirectoriesSucceedingFilesKeepsOrder()
    {
        string a = Write("a.bin");
        Write("mix/a.bin");
        Write("mix/b.bin");
        string z = Write("z.bin");

        Assert.Equal(
            [
                a,
                Path.Combine(_root, "mix", "a.bin"),
                Path.Combine(_root, "mix", "b.bin"),
                z,
            ],
            FilePaths.ExpandDirectories([a, Path.Combine(_root, "mix"), z]));
    }

    [Fact]
    public void EmptyAndNullPathsAreSkipped()
    {
        Assert.Empty(FilePaths.ExpandDirectories([string.Empty, null!]));
    }

    [Fact]
    public void EmptyDirectoryYieldsNothing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        Assert.Empty(FilePaths.ExpandDirectories([Path.Combine(_root, "empty")]));
    }

    [Fact]
    public void MissingPathIsKeptAsIs()
    {
        string missing = Path.Combine(_root, "nope.bin");

        Assert.Equal([missing], FilePaths.ExpandDirectories([missing]));
    }
}

/// <summary>
/// 导入目录时静默丢掉 .DS_Store 等系统杂物，且不触发任何提示。
/// 这里专门盯住「过滤条件写反」这类回归：曾把 <c>Where(IsJunk)</c> 写成
/// 取反缺失，导致目录里只剩 .DS_Store、正常文件全被丢弃。
/// </summary>
public class DsStoreFilterTests : IDisposable
{
    private readonly string _root;

    public DsStoreFilterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "crc64tool-dsstore-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private string Make(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void DsStoreIsDroppedButRealFilesSurvive()
    {
        string dir = Path.Combine(_root, "mix");
        Directory.CreateDirectory(dir);
        Make("mix/.DS_Store");
        string a = Make("mix/a.mp4");
        Make("mix/Thumbs.db");
        string b = Make("mix/b.mp4");

        Assert.Equal([a, b], FilePaths.ExpandDirectories([dir]));
    }

    [Fact]
    public void DsStoreDroppedFromExplicitListToo()
    {
        string ds = Make(".DS_Store");
        string a = Make("a.mp4");

        Assert.Equal([a], FilePaths.ExpandDirectories([a, ds]));
    }

    [Fact]
    public void FolderWithOnlyDsStoreYieldsNothing()
    {
        string dir = Path.Combine(_root, "only");
        Directory.CreateDirectory(dir);
        Make("only/.DS_Store");

        Assert.Empty(FilePaths.ExpandDirectories([dir]));
    }

    [Fact]
    public void IsIgnorableMatchesJunkNames()
    {
        Assert.True(FilePaths.IsIgnorable("/x/.DS_Store"));
        Assert.True(FilePaths.IsIgnorable("/x/thumbs.db"));
        Assert.False(FilePaths.IsIgnorable("/x/a.mp4"));
        Assert.False(FilePaths.IsIgnorable(""));
    }
}

/// <summary>
/// 读取文件夹时忽略以点开头的隐藏文件（.DS_Store、.gitignore、.hidden.mp4 等）。
/// 仅作用于目录展开；用户显式拖入的单个隐藏文件仍保留。
/// </summary>
public class HiddenFileFilterTests : IDisposable
{
    private readonly string _root;

    public HiddenFileFilterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "crc64tool-hidden-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private string Make(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void DotHiddenFilesAreDroppedFromDirectoryExpansion()
    {
        string dir = Path.Combine(_root, "mix");
        Directory.CreateDirectory(dir);
        Make("mix/.hidden.mp4");
        Make("mix/.gitignore");
        string a = Make("mix/a.mp4");
        string b = Make("mix/b.mp4");

        Assert.Equal([a, b], FilePaths.ExpandDirectories([dir]));
    }

    [Fact]
    public void FolderWithOnlyHiddenFilesYieldsNothing()
    {
        string dir = Path.Combine(_root, "only");
        Directory.CreateDirectory(dir);
        Make("only/.hidden.mp4");
        Make("only/.DS_Store");

        Assert.Empty(FilePaths.ExpandDirectories([dir]));
    }

    [Fact]
    public void ExplicitlyDraggedHiddenFileIsKept()
    {
        string hidden = Make(".hidden.mp4");

        Assert.Equal([hidden], FilePaths.ExpandDirectories([hidden]));
    }
}
