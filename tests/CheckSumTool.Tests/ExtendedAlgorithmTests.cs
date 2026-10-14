using System.Globalization;
using System.Text;
using CheckSumTool.Core;
using CheckSumTool.Localization;
using CheckSumTool.Services;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>
/// 扩展算法（SHA-1 / SHA-256 / SHA-384 / SHA-512 / SHA3-256 / SHA3-384 / SHA3-512 / CRC-32）与多语言相关的核心测试。
/// 所有哈希向量均来自 NIST / 标准文档公开值。
/// </summary>
public class Crc32CoreTests
{
    /// <summary>CRC-32（IEEE 802.3）标准校验值：字符串 "123456789" = 0xCBF43926。</summary>
    [Fact]
    public void CheckValue_MatchesStandard()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));
    }

    [Fact]
    public void EmptyInput_IsZero()
    {
        Assert.Equal(0u, Crc32.Compute(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void KnownShortValue_Abc()
    {
        // CRC-32("abc") = 0x352441C2
        Assert.Equal(0x352441C2u, Crc32.Compute(Encoding.ASCII.GetBytes("abc")));
    }

    /// <summary>分段调用与一次性调用必须一致。</summary>
    [Fact]
    public void IncrementalMatchesOneShot()
    {
        byte[] data = new byte[100_000];
        new Random(2024).NextBytes(data);

        uint oneShot = Crc32.Compute(data);

        var incremental = new Crc32();
        int offset = 0;
        int chunk = 1;
        while (offset < data.Length)
        {
            int size = Math.Min(chunk, data.Length - offset);
            incremental.Update(data.AsSpan(offset, size));
            offset += size;
            chunk = chunk * 3 % 7919 + 1;
        }

        Assert.Equal(oneShot, incremental.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(1024)]
    public void AllLengthsAreStable(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(i * 31 + 7);
        }

        var incremental = new Crc32();
        incremental.Update(data);
        Assert.Equal(Crc32.Compute(data), incremental.Value);
    }
}

public class Sha3CoreTests
{
    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    [Fact]
    public void Sha3_256_Empty_IsNistVector()
    {
        var sha3 = Sha3.Sha256();
        sha3.Update(ReadOnlySpan<byte>.Empty);
        Assert.Equal(
            "a7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a",
            Hex(sha3.GetHash()));
    }

    [Fact]
    public void Sha3_256_Abc_IsNistVector()
    {
        var sha3 = Sha3.Sha256();
        sha3.Update(Encoding.ASCII.GetBytes("abc"));
        Assert.Equal(
            "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532",
            Hex(sha3.GetHash()));
    }

    [Fact]
    public void Sha3_512_Abc_IsNistVector()
    {
        var sha3 = Sha3.Sha512();
        sha3.Update(Encoding.ASCII.GetBytes("abc"));
        Assert.Equal(
            "b751850b1a57168a5693cd924b6b096e08f621827444f70d884f5d0240d2712e10e116e9192af3c91a7ec57647e3934057340b4cf408d5a56592f8274eec53f0",
            Hex(sha3.GetHash()));
    }

    /// <summary>多块输入（跨多个速率块）的分段处理必须等价于一次性计算。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(135)]
    [InlineData(136)]
    [InlineData(137)]
    [InlineData(1000)]
    [InlineData(2048)]
    public void IncrementalMatchesOneShot_MultiBlock(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)("abcdefghijklmnopqrstuvwxyz"[i % 26]);
        }

        var oneShot = Sha3.Sha256();
        oneShot.Update(data);
        string expected = Hex(oneShot.GetHash());

        var incremental = Sha3.Sha256();
        int pos = 0;
        int chunk = 137; // 故意不与速率（136）对齐
        while (pos < data.Length)
        {
            int take = Math.Min(chunk, data.Length - pos);
            incremental.Update(data.AsSpan(pos, take));
            pos += take;
        }

        Assert.Equal(expected, Hex(incremental.GetHash()));
    }

    [Fact]
    public async Task Sha3_256_ViaHashEngine_MatchesVector()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "abc.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));

        var outcome = await HashEngine.ComputeAsync(path, HashKind.Sha3_256, null, CancellationToken.None);

        Assert.Equal("3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532", outcome.Hex);
    }
}

public class HashKindMappingTests
{
    [Theory]
    [InlineData(HashKind.Crc64, ".crc64")]
    [InlineData(HashKind.Md5, ".md5")]
    [InlineData(HashKind.Sha1, ".sha1")]
    [InlineData(HashKind.Sha256, ".sha256")]
    [InlineData(HashKind.Sha512, ".sha512")]
    [InlineData(HashKind.Sha3_256, ".sha3-256")]
    [InlineData(HashKind.Sha3_512, ".sha3-512")]
    [InlineData(HashKind.Sha384, ".sha384")]
    [InlineData(HashKind.Sha3_384, ".sha3-384")]
    [InlineData(HashKind.Crc32, ".crc32")]
    public void Extension_MapsCorrectly(HashKind kind, string expected)
    {
        Assert.Equal(expected, kind.Extension());
    }

    [Fact]
    public void Crc32_AllExtensions_PrefersCrc32ThenSfv()
    {
        var exts = HashKind.Crc32.AllExtensions();
        Assert.Equal(2, exts.Count);
        Assert.Equal(".crc32", exts[0]);   // 优先 .crc32
        Assert.Contains(".sfv", exts);     // .sfv 仅为兼容保留
        Assert.Contains(".crc32", exts);
    }

    [Theory]
    [InlineData("a.sfv", HashKind.Crc32)]
    [InlineData("a.SFV", HashKind.Crc32)]
    [InlineData("a.crc32", HashKind.Crc32)]
    [InlineData("a.CRC32", HashKind.Crc32)]
    [InlineData("a.sha1", HashKind.Sha1)]
    [InlineData("a.sha256", HashKind.Sha256)]
    [InlineData("a.sha512", HashKind.Sha512)]
    [InlineData("a.sha3-256", HashKind.Sha3_256)]
    [InlineData("a.sha3-512", HashKind.Sha3_512)]
    [InlineData("a.sha384", HashKind.Sha384)]
    [InlineData("a.sha3-384", HashKind.Sha3_384)]
    [InlineData("a.crc64", HashKind.Crc64)]
    [InlineData("a.md5", HashKind.Md5)]
    public void FromPath_MapsExtensionToKind(string path, HashKind expected)
    {
        Assert.Equal(expected, HashKindExtensions.FromPath(path));
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.mp4")]
    [InlineData("a")]
    [InlineData("a.tar.gz")]
    public void FromPath_UnknownExtension_ReturnsNull(string path)
    {
        Assert.Null(HashKindExtensions.FromPath(path));
    }

    [Theory]
    [InlineData(HashKind.Crc64, 16)]
    [InlineData(HashKind.Md5, 32)]
    [InlineData(HashKind.Sha1, 40)]
    [InlineData(HashKind.Sha256, 64)]
    [InlineData(HashKind.Sha3_256, 64)]
    [InlineData(HashKind.Sha384, 96)]
    [InlineData(HashKind.Sha3_384, 96)]
    [InlineData(HashKind.Sha512, 128)]
    [InlineData(HashKind.Sha3_512, 128)]
    [InlineData(HashKind.Crc32, 8)]
    public void HexLength_IsCorrect(HashKind kind, int expected)
    {
        Assert.Equal(expected, kind.HexLength());
    }
}

public class HashEngineAllKindsTests
{
    private static async Task<string> HashOf(HashKind kind, string text)
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "data.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(text));

        var outcome = await HashEngine.ComputeAsync(path, kind, null, CancellationToken.None);
        return outcome.Hex;
    }

    [Fact]
    public async Task Sha1_Abc() =>
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", await HashOf(HashKind.Sha1, "abc"));

    [Fact]
    public async Task Sha256_Abc() =>
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", await HashOf(HashKind.Sha256, "abc"));

    [Fact]
    public async Task Sha512_Abc() =>
        Assert.Equal("ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f", await HashOf(HashKind.Sha512, "abc"));

    [Fact]
    public async Task Sha3_256_Abc() =>
        Assert.Equal("3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532", await HashOf(HashKind.Sha3_256, "abc"));

    [Fact]
    public async Task Sha3_512_Abc() =>
        Assert.Equal("b751850b1a57168a5693cd924b6b096e08f621827444f70d884f5d0240d2712e10e116e9192af3c91a7ec57647e3934057340b4cf408d5a56592f8274eec53f0", await HashOf(HashKind.Sha3_512, "abc"));

    [Fact]
    public async Task Sha384_Abc() =>
        Assert.Equal("cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7", await HashOf(HashKind.Sha384, "abc"));

    [Fact]
    public async Task Sha3_384_Abc() =>
        Assert.Equal("ec01498288516fc926459f58e2c6ad8df9b473cb0fc08c2596da7cf0e49be4b298d88cea927ac7f539f1edf228376d25", await HashOf(HashKind.Sha3_384, "abc"));

    [Fact]
    public async Task Crc32_Standard() =>
        Assert.Equal("cbf43926", await HashOf(HashKind.Crc32, "123456789"));

    [Fact]
    public async Task Crc64_Standard() =>
        Assert.Equal("995dc9bbdf1939fa", await HashOf(HashKind.Crc64, "123456789"));
}

public class ChecksumFileNewKindTests
{
    private static string WriteTemp(string fileName, string content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    [Theory]
    [InlineData("a.sha1", "a9993e364706816aba3e25717850c26c9cd0d89d *abc.txt", HashKind.Sha1, "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("a.sha256", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad *abc.txt", HashKind.Sha256, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("a.sha512", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f *abc.txt", HashKind.Sha512, "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    [InlineData("a.sha3-256", "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532 *abc.txt", HashKind.Sha3_256, "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532")]
    [InlineData("a.sha3-512", "b751850b1a57168a5693cd924b6b096e08f621827444f70d884f5d0240d2712e10e116e9192af3c91a7ec57647e3934057340b4cf408d5a56592f8274eec53f0 *abc.txt", HashKind.Sha3_512, "b751850b1a57168a5693cd924b6b096e08f621827444f70d884f5d0240d2712e10e116e9192af3c91a7ec57647e3934057340b4cf408d5a56592f8274eec53f0")]
    [InlineData("a.sha384", "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7 *abc.txt", HashKind.Sha384, "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7")]
    [InlineData("a.sha3-384", "ec01498288516fc926459f58e2c6ad8df9b473cb0fc08c2596da7cf0e49be4b298d88cea927ac7f539f1edf228376d25 *abc.txt", HashKind.Sha3_384, "ec01498288516fc926459f58e2c6ad8df9b473cb0fc08c2596da7cf0e49be4b298d88cea927ac7f539f1edf228376d25")]
    public void ParsesNewHashKinds(string fileName, string line, HashKind expectedKind, string expectedHex)
    {
        string path = WriteTemp(fileName, line + "\n");
        var parsed = ChecksumFile.Parse(path);

        Assert.Empty(parsed.Warnings);
        Assert.Equal(expectedKind, parsed.Kind);
        var entry = Assert.Single(parsed.Entries);
        Assert.Equal("abc.txt", entry.FileName);
        Assert.Equal(expectedHex, entry.HashHex);
    }

    [Fact]
    public void ParsesCrc32_SfvExtension_Hex()
    {
        string path = WriteTemp("a.sfv", "cbf43926 *data.bin\n");
        var parsed = ChecksumFile.Parse(path);

        Assert.Empty(parsed.Warnings);
        Assert.Equal(HashKind.Crc32, parsed.Kind);
        var entry = Assert.Single(parsed.Entries);
        Assert.Equal("cbf43926", entry.HashHex);
    }

    [Fact]
    public void ParsesCrc32_Crc32Extension_Decimal()
    {
        string path = WriteTemp("a.crc32", "3421780262 *data.bin\n");
        var parsed = ChecksumFile.Parse(path);

        Assert.Empty(parsed.Warnings);
        Assert.Equal(HashKind.Crc32, parsed.Kind);
        var entry = Assert.Single(parsed.Entries);
        // 十进制 3421780262 == 0xCBF43926
        Assert.Equal("cbf43926", entry.HashHex);
    }

    [Theory]
    [InlineData("cbf43926", "cbf43926")]
    [InlineData("0xCBF43926", "cbf43926")]
    [InlineData("3421780262", "cbf43926")]
    public void NormalizesCrc32Tokens(string token, string expectedHex)
    {
        Assert.True(ChecksumFile.TryNormalizeHash(token, HashKind.Crc32, out string hex));
        Assert.Equal(expectedHex, hex);
    }

    [Fact]
    public void Crc32RejectsWrongLengthHex()
    {
        // 超过 8 位的十六进制不是合法的 CRC-32 值
        Assert.False(ChecksumFile.TryNormalizeHash("cbf439261", HashKind.Crc32, out _));
        // 非十六进制字符
        Assert.False(ChecksumFile.TryNormalizeHash("zzzzzzzz", HashKind.Crc32, out _));
    }

    [Fact]
    public void DetectsAllChecksumFileExtensions()
    {
        Assert.True(ChecksumFile.IsChecksumFile("a.sha1"));
        Assert.True(ChecksumFile.IsChecksumFile("a.sha256"));
        Assert.True(ChecksumFile.IsChecksumFile("a.sha512"));
        Assert.True(ChecksumFile.IsChecksumFile("a.sha3-256"));
        Assert.True(ChecksumFile.IsChecksumFile("a.sha3-512"));
        Assert.True(ChecksumFile.IsChecksumFile("a.sfv"));
        Assert.True(ChecksumFile.IsChecksumFile("a.crc32"));
        Assert.True(ChecksumFile.IsChecksumFile("a.crc64"));
        Assert.True(ChecksumFile.IsChecksumFile("a.md5"));
        Assert.False(ChecksumFile.IsChecksumFile("a.txt"));
    }

    [Fact]
    public void Crc32WrittenAsDecimalInFile()
    {
        // CRC-32 写入 .crc32 校验文件时采用十进制（与界面约定一致）。
        string line = ChecksumFile.FormatLine("cbf43926", "data.bin", NumberFormat.Decimal);
        Assert.Equal("3421780262 *data.bin", line);

        string path = WriteTemp("out.crc32", line + "\n");
        var entry = Assert.Single(ChecksumFile.Parse(path).Entries);
        Assert.Equal("cbf43926", entry.HashHex);
    }

    [Fact]
    public void Crc32WrittenAsHexWhenRequested()
    {
        string line = ChecksumFile.FormatLine("cbf43926", "data.bin", NumberFormat.Hex);
        Assert.Equal("cbf43926 *data.bin", line);
    }
}

/// <summary>
/// .sfv 与 HashCheck（Windows 资源管理器扩展）的互兼容：HashCheck 对 CRC-32 用的容器就是 .sfv，
/// 写入格式为「文件名居左、按最长文件名补空格对齐、单空格分隔、小写 8 位 CRC32、无文件头」。
/// </summary>
public class SfvHashCheckCompatTests
{
    private static string WriteTemp(string fileName, string content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static readonly List<(string Name, string Hex)> Sample =
    [
        ("short.bin", "0a1b2c3d"),
        ("a-longer-name.bin", "cbf43926"),
    ];

    [Fact]
    public void FormatSfv_MatchesHashCheckLayout()
    {
        // 最长文件名 17 字符：短名右侧补 8 个空格，再统一跟一个空格，对齐 CRC 列。
        Assert.Equal(
            "short.bin         0a1b2c3d\n" +
            "a-longer-name.bin cbf43926\n",
            ChecksumFile.FormatSfv(Sample));
    }

    [Fact]
    public void FormatSfv_SingleEntry_HasSingleSpaceSeparator()
    {
        Assert.Equal("data.bin cbf43926\n", ChecksumFile.FormatSfv([("data.bin", "cbf43926")]));
        Assert.Equal(string.Empty, ChecksumFile.FormatSfv([]));
    }

    [Fact]
    public void FormatSfv_UppercasesNothing_AlwaysLowerHex()
    {
        // 入参即便有大写十六进制，写出也统一小写（与 HashCheck 的 WHFMT_LOWERCASE 一致）。
        Assert.Equal("data.bin cbf43926\n", ChecksumFile.FormatSfv([("data.bin", "CBF43926")]));
    }

    [Theory]
    [InlineData("data.bin cbf43926\n", "data.bin")]          // HashCheck 原生单空格
    [InlineData("data.bin    cbf43926\n", "data.bin")]       // HashCheck 的对齐补齐
    [InlineData("my movie.mkv cbf43926\n", "my movie.mkv")]  // 文件名带空格
    [InlineData("data.bin 3421780262\n", "data.bin")]        // 十进制 CRC32
    [InlineData("data.bin 0xCBF43926\n", "data.bin")]        // 0x 前缀十六进制
    [InlineData("cbf43926 *data.bin\n", "data.bin")]         // 本工具旧格式（向后兼容）
    [InlineData("data.bin cbf43926\r\n", "data.bin")]        // CRLF 行尾
    [InlineData("  data.bin cbf43926  \n", "data.bin")]      // 行首尾空白
    public void ParsesSfvNativeLines(string content, string expectedName)
    {
        string path = WriteTemp("a.sfv", content);
        var parsed = ChecksumFile.Parse(path);

        Assert.Empty(parsed.Warnings);
        Assert.Equal(HashKind.Crc32, parsed.Kind);
        var entry = Assert.Single(parsed.Entries);
        Assert.Equal("cbf43926", entry.HashHex);
        Assert.Equal(expectedName, entry.FileName);
    }

    [Theory]
    [InlineData("data.bin cbf43926\n")]
    [InlineData("data.bin    cbf43926\n")]
    public void ParsesSfvNativeLines_InCrc32ExtensionToo(string content)
    {
        // .crc32 与 .sfv 同为 CRC-32 容器，两种行格式都要能读。
        string path = WriteTemp("a.crc32", content);
        var entry = Assert.Single(ChecksumFile.Parse(path).Entries);
        Assert.Equal("cbf43926", entry.HashHex);
        Assert.Equal("data.bin", entry.FileName);
    }

    [Fact]
    public void SfvCommentsAndBlankLinesAreSkipped()
    {
        string content =
            "; Generated by HashCheck\n" +
            "\n" +
            "data.bin cbf43926\n" +
            "; UNREADABLE:\n";
        string path = WriteTemp("a.sfv", content);

        var parsed = ChecksumFile.Parse(path);
        Assert.Empty(parsed.Warnings);
        var entry = Assert.Single(parsed.Entries);
        Assert.Equal("data.bin", entry.FileName);
    }

    [Fact]
    public void SfvWriteParseRoundTrip()
    {
        string content = ChecksumFile.FormatSfv(Sample);
        string path = WriteTemp("a.sfv", content);

        var parsed = ChecksumFile.Parse(path);
        Assert.Empty(parsed.Warnings);
        Assert.Equal(2, parsed.Entries.Count);
        Assert.Equal("short.bin", parsed.Entries[0].FileName);
        Assert.Equal("0a1b2c3d", parsed.Entries[0].HashHex);
        Assert.Equal("a-longer-name.bin", parsed.Entries[1].FileName);
        Assert.Equal("cbf43926", parsed.Entries[1].HashHex);
    }

    [Fact]
    public void SfvTargetOnlyForCrc32WithSfvExtension()
    {
        Assert.True(ChecksumFile.IsSfvTarget("dir/out.sfv", HashKind.Crc32));
        Assert.True(ChecksumFile.IsSfvTarget("dir/OUT.SFV", HashKind.Crc32));
        Assert.False(ChecksumFile.IsSfvTarget("dir/out.crc32", HashKind.Crc32));
        Assert.False(ChecksumFile.IsSfvTarget("dir/out.sfv", HashKind.Md5));
    }
}

public class LanguageDetectionTests
{
    [Theory]
    [InlineData("zh-CN", Language.SimplifiedChinese)]
    [InlineData("zh-Hans", Language.SimplifiedChinese)]
    [InlineData("zh-TW", Language.TraditionalChinese)]
    [InlineData("zh-Hant", Language.TraditionalChinese)]
    [InlineData("zh-HK", Language.TraditionalChinese)]
    [InlineData("ja-JP", Language.Japanese)]
    [InlineData("ko-KR", Language.Korean)]
    [InlineData("fr-FR", Language.French)]
    [InlineData("de-DE", Language.German)]
    [InlineData("es-ES", Language.Spanish)]
    [InlineData("ru-RU", Language.Russian)]
    [InlineData("pt-BR", Language.Portuguese)]
    [InlineData("it-IT", Language.Italian)]
    [InlineData("en-US", Language.English)]
    [InlineData("en-GB", Language.English)]
    [InlineData("xx-XX", Language.English)] // 未知语言回退到英语
    public void DetectFromCulture_MapsSystemCulture(string cultureName, Language expected)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        Assert.Equal(expected, LanguageExtensions.DetectFromCulture(culture));
    }

    [Fact]
    public void AllConcrete_HasElevenLanguages()
    {
        Assert.Equal(11, LanguageExtensions.AllConcrete.Count);
        // 不含「跟随系统」之类的特殊状态
        Assert.DoesNotContain((Language)99, LanguageExtensions.AllConcrete);
    }

    [Fact]
    public void NativeName_IsNonEmptyForEveryLanguage()
    {
        foreach (Language language in LanguageExtensions.AllConcrete)
        {
            Assert.False(string.IsNullOrWhiteSpace(language.NativeName()));
        }
    }

    [Fact]
    public void AppSettings_FollowSystemLanguageDefaultsToTrue()
    {
        var settings = new AppSettings();
        Assert.True(settings.FollowSystemLanguage);
    }

    [Fact]
    public void AppSettings_Crc32ExtensionDefaultsToCrc32()
    {
        var settings = new AppSettings();
        Assert.Equal(".crc32", settings.Crc32Extension);
        Assert.Equal(ChecksumFile.Crc32Extension, settings.Crc32Extension);
    }
}
