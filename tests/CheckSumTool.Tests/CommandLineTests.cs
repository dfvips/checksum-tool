using CheckSumTool.Services;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>命令行解析：双击校验文件（文件关联）或显式 <c>--verify</c> 都能进入校验。</summary>
public class CommandLineTests
{
    private static string Temp(string name)
        => Path.Combine(Path.GetTempPath(), "crc64tool-cli", name);

    [Theory]
    [InlineData("a.crc64")]
    [InlineData("a.md5")]
    public void FileAssociationStyleArgIsAccepted(string name)
    {
        string path = Temp(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "6967259512934720053 *a.mp4\n");

        var files = CommandLine.ExtractChecksumFiles([path]);

        Assert.Equal([path], files);
    }

    [Fact]
    public void ExplicitVerifyFlagIsAccepted()
    {
        string path = Temp("b.md5");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "900150983cd24fb0d6963f7d28e17f72 *b.bin\n");

        var files = CommandLine.ExtractChecksumFiles(["--verify", path]);

        Assert.Equal([path], files);
    }

    [Fact]
    public void OptionsAreIgnored()
    {
        string path = Temp("c.crc64");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "6967259512934720053 *c.mp4\n");

        var files = CommandLine.ExtractChecksumFiles(["--version", path, "--help"]);

        Assert.Equal([path], files);
    }

    [Fact]
    public void NonChecksumFilesAreRejected()
    {
        string path = Temp("d.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a checksum file\n");

        Assert.Empty(CommandLine.ExtractChecksumFiles([path]));
    }

    [Fact]
    public void MissingFilesAreRejected()
    {
        Assert.Empty(CommandLine.ExtractChecksumFiles([Temp("missing.crc64")]));
    }

    [Fact]
    public void MultipleFilesAreDeduplicated()
    {
        string path = Temp("e.crc64");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "6967259512934720053 *e.mp4\n");

        var files = CommandLine.ExtractChecksumFiles([path, "--verify", path]);

        Assert.Single(files);
    }

    [Fact]
    public void EmptyArgsYieldNothing()
    {
        Assert.Empty(CommandLine.ExtractChecksumFiles(Array.Empty<string>()));
    }
}
