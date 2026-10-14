using CheckSumTool.Core;
using CheckSumTool.Localization;
using CheckSumTool.Services;
using CheckSumTool.ViewModels;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>生成面板的默认文件名规则：单文件用自身名，多文件用「第一个 - 最后一个」。</summary>
public class GenerateNamingTests
{
    private static GenerateViewModel Create(out AppSettings settings)
    {
        settings = new AppSettings();
        return new GenerateViewModel(
            settings,
            (_, _) => { });
    }

    private static GenerateViewModel Create() => Create(out _);

    private static string P(string name) => Path.Combine(Path.GetTempPath(), "crc64tool-naming", name);

    [Fact]
    public void EmptyListKeepsNameBlank()
    {
        var vm = Create();
        Assert.Equal(string.Empty, vm.OutputBaseName);
    }

    [Fact]
    public void SingleFileUsesItsOwnNameWithoutExtension()
    {
        var vm = Create();
        vm.AddFiles([P("古相思曲_第01集.mp4")]);

        Assert.Equal("古相思曲_第01集", vm.OutputBaseName);
        Assert.Equal(".crc64", Assert.Single(vm.OutputFormats, f => f.IsSelected).Extension);
    }

    [Fact]
    public void MultipleFilesUseFirstAndLastName()
    {
        var vm = Create();
        vm.AddFiles([P("a.mp4"), P("b.mp4"), P("c.mp4")]);

        Assert.Equal("a - c", vm.OutputBaseName);
    }

    [Fact]
    public void NameFollowsAppendedFiles()
    {
        var vm = Create();
        vm.AddFiles([P("first.mp4")]);
        Assert.Equal("first", vm.OutputBaseName);

        vm.AddFiles([P("last.mkv")]);
        Assert.Equal("first - last", vm.OutputBaseName);
    }

    [Fact]
    public void ManualEditIsNotOverwrittenByLaterFiles()
    {
        var vm = Create();
        vm.AddFiles([P("a.mp4")]);

        vm.OutputBaseName = "我的合集";
        vm.AddFiles([P("b.mp4")]);

        Assert.Equal("我的合集", vm.OutputBaseName);
    }

    [Fact]
    public void ClearRestoresAutoNaming()
    {
        var vm = Create();
        vm.AddFiles([P("a.mp4")]);
        vm.OutputBaseName = "手动";
        vm.ClearCommand.Execute(null);

        Assert.Equal(string.Empty, vm.OutputBaseName);

        vm.AddFiles([P("z.mp4")]);
        Assert.Equal("z", vm.OutputBaseName);
    }

    [Fact]
    public void TypingExtensionSelectsCorrespondingFormat()
    {
        var vm = Create();
        vm.AddFiles([P("a.mp4")]);

        vm.OutputBaseName = "a.md5";

        Assert.Equal("a", vm.OutputBaseName);
        var selected = Assert.Single(vm.OutputFormats, f => f.IsSelected);
        Assert.Equal(HashKind.Md5, selected.Kind);
        Assert.Equal(".md5", selected.Extension);
    }

    [Fact]
    public void OutputFormatListIsAlphabeticalAndDefaultsToCrc64()
    {
        var vm = Create();

        Assert.Equal(
            [
                "CRC-32 (.crc32)", "CRC-32 (.sfv)", "CRC-64 (.crc64)", "MD5 (.md5)",
                "SHA-1 (.sha1)", "SHA-256 (.sha256)", "SHA-384 (.sha384)", "SHA-512 (.sha512)",
                "SHA3-256 (.sha3-256)", "SHA3-384 (.sha3-384)", "SHA3-512 (.sha3-512)",
            ],
            vm.OutputFormats.Select(f => f.Label));

        var selected = Assert.Single(vm.OutputFormats, f => f.IsSelected);
        Assert.Equal(HashKind.Crc64, selected.Kind);
        Assert.Equal(".crc64", selected.Extension);
    }

    [Fact]
    public void CanSelectBothCrc32ExtensionsAlongsideOtherFormats()
    {
        var vm = Create();
        vm.AddFiles([P("a.mp4")]);
        vm.OutputFormats.Single(f => f.Extension == ".crc32").IsSelected = true;
        vm.OutputFormats.Single(f => f.Extension == ".sfv").IsSelected = true;
        vm.OutputFormats.Single(f => f.Extension == ".md5").IsSelected = true;

        Assert.Equal(
            ["a.crc32", "a.sfv", "a.crc64", "a.md5"],
            vm.ResolveOutputPaths().Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(".sfv")]
    [InlineData(".crc32")]
    [InlineData(".SFV")]
    [InlineData(".CRC32")]
    public void TypingCrc32ExtensionSelectsThatFormat(string typed)
    {
        var vm = Create();
        vm.AddFiles([P("a.mp4")]);
        vm.OutputBaseName = "a" + typed;

        Assert.Equal("a", vm.OutputBaseName);
        var selected = Assert.Single(vm.OutputFormats, f => f.IsSelected);
        Assert.Equal(HashKind.Crc32, selected.Kind);
        Assert.Equal(typed.ToLowerInvariant(), selected.Extension);
    }

    [Fact]
    public void OutputPathsFollowSourceDirectoryForEachSelectedFormat()
    {
        var vm = Create();
        vm.AddFiles([P("a.mp4"), P("b.mp4")]);
        vm.OutputFormats.Single(f => f.Extension == ".md5").IsSelected = true;

        var crc64 = vm.ResolveOutputPaths(vm.OutputFormats.Single(f => f.Extension == ".crc64"));
        var md5 = vm.ResolveOutputPaths(vm.OutputFormats.Single(f => f.Extension == ".md5"));
        Assert.Equal([Path.Combine(Path.GetTempPath(), "crc64tool-naming", "a - b.crc64")], crc64);
        Assert.Equal([Path.Combine(Path.GetTempPath(), "crc64tool-naming", "a - b.md5")], md5);
    }

    [Fact]
    public async Task GeneratesAllSelectedFormatFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-multiformat", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string input = Path.Combine(dir, "data.bin");
        await File.WriteAllTextAsync(input, "123456789");
        try
        {
            var settings = new AppSettings { Parallelism = 1 };
            var vm = new GenerateViewModel(settings, (_, _) => { });
            vm.AddFiles([input]);
            vm.OutputFormats.Single(f => f.Extension == ".md5").IsSelected = true;
            vm.OutputFormats.Single(f => f.Extension == ".sfv").IsSelected = true;

            await vm.StartCommand.ExecuteAsync(null);

            // 运行中每行会显示当前算法名（进度条左侧），生成结束后仍保留最后一个算法。
            Assert.False(string.IsNullOrEmpty(vm.Files[0].CurrentFormat));

            foreach (string ext in new[] { ".crc64", ".md5", ".sfv" })
            {
                Assert.True(File.Exists(Path.Combine(dir, "data" + ext)), $"Missing {ext} output");
                var parsed = ChecksumFile.Parse(Path.Combine(dir, "data" + ext));
                Assert.Empty(parsed.Warnings);
                Assert.Single(parsed.Entries);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SfvOutputUsesHashCheckCompatibleLayout()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-sfv", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "short.bin"), "123456789");
        await File.WriteAllTextAsync(Path.Combine(dir, "a-longer-name.bin"), "123456789");
        try
        {
            var settings = new AppSettings { Parallelism = 2 };
            var vm = new GenerateViewModel(settings, (_, _) => { });
            vm.AddFiles([Path.Combine(dir, "short.bin"), Path.Combine(dir, "a-longer-name.bin")]);
            // 只勾 CRC-32 (.sfv)：.sfv 是 HashCheck 的 CRC-32 容器，必须写 SFV 原生格式。
            vm.OutputFormats.Single(f => f.Extension == ".crc64").IsSelected = false;
            vm.OutputFormats.Single(f => f.Extension == ".sfv").IsSelected = true;

            await vm.StartCommand.ExecuteAsync(null);

            string sfvPath = Path.Combine(dir, "short - a-longer-name.sfv");
            // CRC-32("123456789") = 0xCBF43926 → 小写 8 位；文件名居左、按最长名补空格对齐、单空格分隔。
            Assert.Equal(
                "short.bin         cbf43926\n" +
                "a-longer-name.bin cbf43926\n",
                await File.ReadAllTextAsync(sfvPath));

            // 写出的文件本工具也要能原样读回。
            var parsed = ChecksumFile.Parse(sfvPath);
            Assert.Empty(parsed.Warnings);
            Assert.Equal(2, parsed.Entries.Count);
            Assert.All(parsed.Entries, e => Assert.Equal("cbf43926", e.HashHex));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratesPerFormatFilesForMultipleFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-multifile", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "a.bin"), "123456789");
        await File.WriteAllTextAsync(Path.Combine(dir, "b.bin"), "abcdefghi");
        try
        {
            var settings = new AppSettings { Parallelism = 3 };
            var vm = new GenerateViewModel(settings, (_, _) => { });
            vm.AddFiles([Path.Combine(dir, "a.bin"), Path.Combine(dir, "b.bin")]);
            vm.OutputFormats.Single(f => f.Extension == ".md5").IsSelected = true;

            await vm.StartCommand.ExecuteAsync(null);

            // 每个格式各产出一份文件，且都包含全部输入文件的记录。
            foreach (string ext in new[] { ".crc64", ".md5" })
            {
                var parsed = ChecksumFile.Parse(Path.Combine(dir, "a - b" + ext));
                Assert.Empty(parsed.Warnings);
                Assert.Equal(2, parsed.Entries.Count);
            }

            // 每个文件在所有格式跑完后都结算为完成。
            Assert.All(vm.Files, f => Assert.Equal(GenerateItemState.Done, f.State));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }


    [Fact]
    public void LongNamesAreTruncated()
    {
        var vm = Create();
        vm.AddFiles([P(new string('x', 300) + ".mp4")]);

        Assert.Equal(120, vm.OutputBaseName.Length);
    }

    [Fact]
    public void ChecksumFilesAreRejected()
    {
        var vm = Create();
        vm.AddFiles([P("a.crc64"), P("b.md5")]);

        Assert.Empty(vm.Files);
        Assert.Equal(string.Empty, vm.OutputBaseName);
    }

    [Fact]
    public void OutputPathsFollowSourceDirectory()
    {
        var vm = Create();
        vm.AddFiles([P("a.mp4"), P("b.mp4")]);

        var paths = vm.ResolveOutputPaths();

        string expected = Path.Combine(Path.GetTempPath(), "crc64tool-naming", "a - b.crc64");
        Assert.Equal([expected], paths);
    }

    // ------------------------------------------------------------ 跨文件夹：按文件夹分别生成

    private static string Dir(string name) => Path.Combine(Path.GetTempPath(), "crc64tool-naming", name);

    [Fact]
    public void MultipleFoldersLeaveNameBlankAndNameEachFolderByItsOwnFiles()
    {
        var vm = Create();
        vm.AddFiles([Path.Combine(Dir("A"), "a1.mp4"), Path.Combine(Dir("A"), "a2.mp4"), Path.Combine(Dir("B"), "b1.mp4")]);

        // 跨目录时没有「一个」名字可用，输入框留空（每个目录各自命名）
        Assert.Equal(string.Empty, vm.OutputBaseName);

        var format = vm.OutputFormats.Single(f => f.Extension == ".crc64");
        Assert.Equal(
            [Path.Combine(Dir("A"), "a1 - a2.crc64"), Path.Combine(Dir("B"), "b1.crc64")],
            vm.ResolveOutputPaths(format));
    }

    [Fact]
    public void ManualNameAppliesToEveryFolder()
    {
        var vm = Create();
        vm.AddFiles([Path.Combine(Dir("A"), "a1.mp4"), Path.Combine(Dir("B"), "b1.mp4")]);
        vm.OutputBaseName = "合集";

        var format = vm.OutputFormats.Single(f => f.Extension == ".crc64");
        Assert.Equal(
            [Path.Combine(Dir("A"), "合集.crc64"), Path.Combine(Dir("B"), "合集.crc64")],
            vm.ResolveOutputPaths(format));
    }

    [Fact]
    public async Task GeneratesOneFilePerFolderWithPerFolderNames()
    {
        string root = Path.Combine(Path.GetTempPath(), "crc64tool-perfolder", Guid.NewGuid().ToString("N"));
        string dirA = Path.Combine(root, "A");
        string dirB = Path.Combine(root, "B");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        await File.WriteAllTextAsync(Path.Combine(dirA, "a1.bin"), "111111111");
        await File.WriteAllTextAsync(Path.Combine(dirA, "a2.bin"), "222222222");
        await File.WriteAllTextAsync(Path.Combine(dirB, "b1.bin"), "333333333");

        try
        {
            var vm = new GenerateViewModel(new AppSettings { Parallelism = 2 }, (_, _) => { });
            vm.AddFiles([Path.Combine(dirA, "a1.bin"), Path.Combine(dirA, "a2.bin"), Path.Combine(dirB, "b1.bin")]);

            await vm.StartCommand.ExecuteAsync(null);

            // 每个文件夹各一份，只收本文件夹的文件，文件名按本文件夹内的文件命名
            var groupA = ChecksumFile.Parse(Path.Combine(dirA, "a1 - a2.crc64"));
            Assert.Empty(groupA.Warnings);
            Assert.Equal(["a1.bin", "a2.bin"], groupA.Entries.Select(e => e.FileName));

            var groupB = ChecksumFile.Parse(Path.Combine(dirB, "b1.crc64"));
            Assert.Empty(groupB.Warnings);
            Assert.Equal(["b1.bin"], groupB.Entries.Select(e => e.FileName));

            // 不应该在别处冒出把两个文件夹混在一起的输出
            Assert.Single(Directory.GetFiles(dirA, "*.crc64"));
            Assert.Single(Directory.GetFiles(dirB, "*.crc64"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DefaultParallelismIsWithinRange()
    {
        var vm = Create();
        Assert.InRange(vm.Settings.Parallelism, 1, 5);
    }

    [Fact]
    public void ParallelismIsClamped()
    {
        var settings = new AppSettings { Parallelism = 99 };
        Assert.Equal(AppSettings.MaxParallelism, settings.Parallelism);

        settings.Parallelism = 0;
        Assert.Equal(1, settings.Parallelism);
    }
}

/// <summary>多语言文案表完整性。</summary>
public class LocalizationTests
{
    // 遍历全部具体语言（不再硬编码 3 种），确保新增的日语/韩语/法语等也都已补齐。
    private static readonly Language[] AllLanguages = LanguageExtensions.AllConcrete.ToArray();

    [Fact]
    public void EveryKeyHasAllThreeLanguages()
    {
        foreach (string key in Strings.Keys)
        {
            foreach (Language language in AllLanguages)
            {
                string text = Strings.Get(language, key);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{key} 缺少 {language} 文案");
            }
        }
    }

    [Fact]
    public void PlaceholdersMatchAcrossLanguages()
    {
        foreach (string key in Strings.Keys)
        {
            int expected = CountPlaceholders(Strings.Get(Language.SimplifiedChinese, key));

            foreach (Language language in AllLanguages)
            {
                int actual = CountPlaceholders(Strings.Get(language, key));
                Assert.True(expected == actual, $"{key} 在 {language} 中的占位符数量不一致（{expected} vs {actual}）");
            }
        }
    }

    [Fact]
    public void UnknownKeyFallsBackToItself()
    {
        Assert.Equal("NoSuchKey", Strings.Get(Language.English, "NoSuchKey"));
    }

    [Fact]
    public void LocalizerSwitchesLanguage()
    {
        var localizer = Localizer.Instance;
        try
        {
            localizer.Language = Language.English;
            Assert.Equal("Verify", localizer["TabVerify"]);

            localizer.Language = Language.SimplifiedChinese;
            Assert.Equal("校验", localizer["TabVerify"]);
        }
        finally
        {
            localizer.Language = Language.SimplifiedChinese;
        }
    }

    private static int CountPlaceholders(string template)
    {
        int max = -1;

        for (int i = 0; i < 10; i++)
        {
            if (template.Contains($"{{{i}}}", StringComparison.Ordinal))
            {
                max = i;
            }
        }

        return max + 1;
    }
}
