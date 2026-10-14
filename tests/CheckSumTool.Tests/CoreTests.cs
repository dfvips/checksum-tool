using System.Text;
using CheckSumTool.Core;
using Xunit;

namespace CheckSumTool.Tests;

public class Crc64Tests
{
    /// <summary>CRC-64/XZ（ECMA-182 反射）标准校验值。</summary>
    [Fact]
    public void CheckValue_MatchesStandard()
    {
        ulong value = Crc64.Compute(Encoding.ASCII.GetBytes("123456789"));
        Assert.Equal(0x995DC9BBDF1939FAUL, value);
    }

    [Fact]
    public void EmptyInput_IsZero()
    {
        Assert.Equal(0UL, Crc64.Compute(ReadOnlySpan<byte>.Empty));
    }

    /// <summary>分段调用与一次性调用结果必须一致。</summary>
    [Fact]
    public void IncrementalMatchesOneShot()
    {
        byte[] data = new byte[100_000];
        new Random(2024).NextBytes(data);

        ulong oneShot = Crc64.Compute(data);

        var incremental = new Crc64();
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

    /// <summary>与 8 字节对齐的路径（slicing-by-8 主循环）必须正确。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1023)]
    [InlineData(1024)]
    public void AllLengthsAreStable(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(i * 31 + 7);
        }

        var incremental = new Crc64();
        incremental.Update(data);
        Assert.Equal(Crc64.Compute(data), incremental.Value);
    }

    /// <summary>
    /// 与项目内 crc64ecma.cs 的原始实现逐位等价（同样的多项式、初值、slicing-by-8 分块与分段调用方式）。
    /// </summary>
    [Fact]
    public void MatchesLegacyCrc64EcmaImplementation()
    {
        var random = new Random(20261013);
        int[] lengths = [0, 1, 2, 3, 7, 8, 9, 15, 16, 17, 31, 32, 33, 255, 256, 1000, 65_536];

        foreach (int length in lengths)
        {
            byte[] data = new byte[length];
            random.NextBytes(data);

            // 一次性
            Assert.Equal(LegacyCrc64Ecma.Compute(data), Crc64.Compute(data));

            // 随机分段（模拟流式读取）
            var mine = new Crc64();
            var legacy = new LegacyCrc64Ecma();
            int offset = 0;
            while (offset < data.Length)
            {
                int size = Math.Min(random.Next(1, 4096), data.Length - offset);
                mine.Update(data.AsSpan(offset, size));
                legacy.Update(data.AsSpan(offset, size));
                offset += size;
            }

            Assert.Equal(legacy.Value, mine.Value);
        }
    }

    /// <summary>写入文件时的文本必须是十进制整数（与 crc64ecma.cs 的 ulong 输出一致）。</summary>
    [Fact]
    public void WritesDecimalLikeLegacyTool()
    {
        ulong value = Crc64.Compute(Encoding.ASCII.GetBytes("123456789"));
        Assert.Equal("11051210869376104954", value.ToString());           // 0x995DC9BBDF1939FA
        Assert.Equal("11051210869376104954", ChecksumFile.FormatHash("995dc9bbdf1939fa", NumberFormat.Decimal));
        Assert.Equal(
            "11051210869376104954 *movie.mp4",
            ChecksumFile.FormatLine("995dc9bbdf1939fa", "movie.mp4", NumberFormat.Decimal));
    }
}

/// <summary>
/// crc64ecma.cs 的原始实现，原样搬运过来作为等价性基准，不参与产品逻辑。
/// </summary>
internal sealed class LegacyCrc64Ecma
{
    private const ulong Polynomial = 0xC96C5795D7870F42UL;
    private static readonly ulong[,] Table = BuildTable();
    private ulong _crc;

    private static ulong[,] BuildTable()
    {
        var table = new ulong[8, 256];

        for (ulong i = 0; i < 256; i++)
        {
            ulong crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            }

            table[0, i] = crc;
        }

        for (int slice = 1; slice < 8; slice++)
        {
            for (int i = 0; i < 256; i++)
            {
                ulong crc = table[slice - 1, i];
                table[slice, i] = table[0, crc & 0xFF] ^ (crc >> 8);
            }
        }

        return table;
    }

    public ulong Value => _crc;

    public void Update(ReadOnlySpan<byte> data)
    {
        ulong crc = ~_crc;
        int i = 0;

        while (i + 8 <= data.Length)
        {
            ulong block = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i, 8));
            crc ^= block;

            crc =
                Table[7, crc & 0xFF] ^
                Table[6, (crc >> 8) & 0xFF] ^
                Table[5, (crc >> 16) & 0xFF] ^
                Table[4, (crc >> 24) & 0xFF] ^
                Table[3, (crc >> 32) & 0xFF] ^
                Table[2, (crc >> 40) & 0xFF] ^
                Table[1, (crc >> 48) & 0xFF] ^
                Table[0, crc >> 56];

            i += 8;
        }

        for (; i < data.Length; i++)
        {
            crc = Table[0, (byte)(crc ^ data[i])] ^ (crc >> 8);
        }

        _crc = ~crc;
    }

    public static ulong Compute(ReadOnlySpan<byte> data)
    {
        var c = new LegacyCrc64Ecma();
        c.Update(data);
        return c.Value;
    }
}

public class ChecksumFileParseTests
{
    private static string WriteTemp(string content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "sample.crc64");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    /// <summary>十进制 + " *" 分隔（本项目既有文件的真实格式）。</summary>
    [Fact]
    public void ParsesDecimalWithAsteriskSeparator()
    {
        string path = WriteTemp("6967259512934720053 *我不是大师_01.mp4\n");
        var parsed = ChecksumFile.Parse(path);

        Assert.Empty(parsed.Warnings);
        var entry = Assert.Single(parsed.Entries);
        Assert.Equal("我不是大师_01.mp4", entry.FileName);
        Assert.Equal("60b0ad9ce66f6235", entry.HashHex);
        Assert.Equal("6967259512934720053", entry.RawHash);
        Assert.Equal(HashKind.Crc64, entry.Kind);
    }

    [Fact]
    public void ParsesMd5WithTwoSpaceSeparator()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "sample.md5");
        File.WriteAllText(path, "900150983CD24FB0D6963F7D28E17F72  a b c.bin\n", new UTF8Encoding(false));

        var parsed = ChecksumFile.Parse(path);
        var entry = Assert.Single(parsed.Entries);

        Assert.Equal(HashKind.Md5, entry.Kind);
        Assert.Equal("a b c.bin", entry.FileName);
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", entry.HashHex);
    }

    [Fact]
    public void ParsesHexCrc64()
    {
        string path = WriteTemp("60b0ad9ce66f6235 *video.mp4\n");
        var entry = Assert.Single(ChecksumFile.Parse(path).Entries);
        Assert.Equal("60b0ad9ce66f6235", entry.HashHex);
    }

    [Fact]
    public void ParsesHexCrc64WithPrefix()
    {
        string path = WriteTemp("0x60B0AD9CE66F6235 *video.mp4\n");
        var entry = Assert.Single(ChecksumFile.Parse(path).Entries);
        Assert.Equal("60b0ad9ce66f6235", entry.HashHex);
    }

    /// <summary>兼容旧版控制台工具输出的 "文件名====校验值" 形式。</summary>
    [Fact]
    public void ParsesLegacyEqualsFormat()
    {
        string path = WriteTemp("video.mp4====6967259512934720053\n");
        var entry = Assert.Single(ChecksumFile.Parse(path).Entries);
        Assert.Equal("video.mp4", entry.FileName);
        Assert.Equal("60b0ad9ce66f6235", entry.HashHex);
    }

    [Fact]
    public void SkipsBlankAndCommentLines()
    {
        string path = WriteTemp("\n# comment\n\n; another\n60b0ad9ce66f6235 *a.mp4\n");
        var parsed = ChecksumFile.Parse(path);
        Assert.Single(parsed.Entries);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void ReportsInvalidLinesAsWarnings()
    {
        string path = WriteTemp("not-a-hash *x.mp4\n60b0ad9ce66f6235 *ok.mp4\n");
        var parsed = ChecksumFile.Parse(path);
        Assert.Single(parsed.Entries);
        Assert.Single(parsed.Warnings);
    }

    [Theory]
    [InlineData("6967259512934720053", "60b0ad9ce66f6235")]
    [InlineData("60b0ad9ce66f6235", "60b0ad9ce66f6235")]
    [InlineData("0x60B0AD9CE66F6235", "60b0ad9ce66f6235")]
    [InlineData("18446744073709551615", "ffffffffffffffff")]
    [InlineData("0", "0000000000000000")]
    public void NormalizesCrc64Tokens(string token, string expectedHex)
    {
        Assert.True(ChecksumFile.TryNormalizeHash(token, HashKind.Crc64, out string hex));
        Assert.Equal(expectedHex, hex);
    }

    [Theory]
    [InlineData("zzz")]
    [InlineData("")]
    [InlineData("18446744073709551616")] // 超出 ulong 范围
    public void RejectsInvalidCrc64Tokens(string token)
    {
        Assert.False(ChecksumFile.TryNormalizeHash(token, HashKind.Crc64, out _));
    }

    [Fact]
    public void Md5Requires32HexChars()
    {
        Assert.True(ChecksumFile.TryNormalizeHash("900150983cd24fb0d6963f7d28e17f72", HashKind.Md5, out _));
        Assert.False(ChecksumFile.TryNormalizeHash("900150983cd24fb0d6963f7d28e17f7", HashKind.Md5, out _));
        Assert.False(ChecksumFile.TryNormalizeHash("6967259512934720053", HashKind.Md5, out _));
    }

    [Fact]
    public void FormatLineRoundTripsDecimal()
    {
        string line = ChecksumFile.FormatLine("60b0ad9ce66f6235", "视频 01.mp4", NumberFormat.Decimal);
        Assert.Equal("6967259512934720053 *视频 01.mp4", line);

        string path = WriteTemp(line + "\n");
        var entry = Assert.Single(ChecksumFile.Parse(path).Entries);
        Assert.Equal("视频 01.mp4", entry.FileName);
        Assert.Equal("60b0ad9ce66f6235", entry.HashHex);
    }

    [Fact]
    public void FormatLineRoundTripsHex()
    {
        string line = ChecksumFile.FormatLine("60b0ad9ce66f6235", "video.mp4", NumberFormat.Hex);
        Assert.Equal("60b0ad9ce66f6235 *video.mp4", line);
    }

    [Fact]
    public void Md5IsAlwaysWrittenAsHex()
    {
        string hex = "900150983cd24fb0d6963f7d28e17f72";
        Assert.Equal(hex, ChecksumFile.FormatHash(hex, NumberFormat.Decimal));
    }

    [Fact]
    public void DetectsChecksumFileExtensions()
    {
        Assert.True(ChecksumFile.IsChecksumFile("a.crc64"));
        Assert.True(ChecksumFile.IsChecksumFile("a.CRC64"));
        Assert.True(ChecksumFile.IsChecksumFile("a.md5"));
        Assert.False(ChecksumFile.IsChecksumFile("a.mp4"));
        Assert.False(ChecksumFile.IsChecksumFile("a.txt"));
    }
}

public class HashEngineTests
{
    [Fact]
    public async Task ComputesMd5OfKnownFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "abc.bin");
        await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("abc"));

        var outcome = await HashEngine.ComputeAsync(path, HashKind.Md5, null, CancellationToken.None);
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", outcome.Hex);
        Assert.Equal(3, outcome.Bytes);
    }

    [Fact]
    public async Task ComputesCrc64OfKnownFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "abc.bin");
        await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("123456789"));

        var outcome = await HashEngine.ComputeAsync(path, HashKind.Crc64, null, CancellationToken.None);
        Assert.Equal("995dc9bbdf1939fa", outcome.Hex);
    }

    [Fact]
    public async Task SupportsCancellation()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "big.bin");
        await File.WriteAllBytesAsync(path, new byte[32 * 1024 * 1024]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => HashEngine.ComputeAsync(path, HashKind.Crc64, null, cts.Token));
    }

    [Fact]
    public void ResolvesTargetRelativeToChecksumFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-tests", "dir");
        string checksum = Path.Combine(dir, "a.crc64");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(dir, "movie.mp4")),
            HashEngine.ResolveTargetPath(checksum, "movie.mp4"));

        Assert.Equal(
            Path.GetFullPath(Path.Combine(dir, "sub", "movie.mp4")),
            HashEngine.ResolveTargetPath(checksum, "sub/movie.mp4"));
    }

    [Fact]
    public void KeepsAbsoluteTargetPaths()
    {
        string absolute = Path.Combine(Path.GetTempPath(), "elsewhere", "movie.mp4");
        Assert.Equal(absolute, HashEngine.ResolveTargetPath("/tmp/a.crc64", absolute));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    public void FormatsSizes(long bytes, string expected)
        => Assert.Equal(expected, HashEngine.FormatSize(bytes));
}
