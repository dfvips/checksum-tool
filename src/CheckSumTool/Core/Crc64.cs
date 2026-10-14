using System.Buffers.Binary;

namespace CheckSumTool.Core;

/// <summary>
/// CRC64-ECMA（ECMA-182 反射实现），与项目内 <c>crc64ecma.cs</c> 的原始实现逐位等价，
/// 也与 Go 标准库 <c>hash/crc64</c> 的 ECMA 表、7-Zip 的 CRC64 一致。
///
/// 参数：poly=0xC96C5795D7870F42（反射）、init=0xFFFF_FFFF_FFFF_FFFF、
///      refin=true、refout=true、xorout=0xFFFF_FFFF_FFFF_FFFF。
/// 校验值 "123456789" = 0x995DC9BBDF1939FA = 11051210869376104954。
///
/// 写入 .crc64 文件时使用该值的<b>无符号十进制</b>形式（与 crc64ecma.cs 的 ulong 输出一致），
/// 界面上不提供进制选择。
///
/// 使用 slicing-by-8 查表，吞吐量约为逐字节实现的 4~6 倍。
/// </summary>
public sealed class Crc64
{
    private const ulong Polynomial = 0xC96C5795D7870F42UL;

    /// <summary>8 张 256 项查表，按 slice * 256 + index 扁平存放。</summary>
    private static readonly ulong[] Table = BuildTable();

    private ulong _crc = ulong.MaxValue;

    /// <summary>当前校验值（已应用 xorout）。</summary>
    public ulong Value => ~_crc;

    /// <summary>复位到初始状态。</summary>
    public void Reset() => _crc = ulong.MaxValue;

    private static ulong[] BuildTable()
    {
        var table = new ulong[8 * 256];

        for (ulong i = 0; i < 256; i++)
        {
            ulong crc = i;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            }

            table[i] = crc;
        }

        for (int slice = 1; slice < 8; slice++)
        {
            int prev = (slice - 1) * 256;
            int cur = slice * 256;

            for (int i = 0; i < 256; i++)
            {
                ulong crc = table[prev + i];
                table[cur + i] = table[(int)(crc & 0xFF)] ^ (crc >> 8);
            }
        }

        return table;
    }

    /// <summary>追加一段数据。</summary>
    public void Update(ReadOnlySpan<byte> data)
    {
        ulong crc = _crc;
        int i = 0;

        while (i + 8 <= data.Length)
        {
            ulong block = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i, 8));
            crc ^= block;

            crc =
                Table[(7 * 256) + (int)(crc & 0xFF)] ^
                Table[(6 * 256) + (int)((crc >> 8) & 0xFF)] ^
                Table[(5 * 256) + (int)((crc >> 16) & 0xFF)] ^
                Table[(4 * 256) + (int)((crc >> 24) & 0xFF)] ^
                Table[(3 * 256) + (int)((crc >> 32) & 0xFF)] ^
                Table[(2 * 256) + (int)((crc >> 40) & 0xFF)] ^
                Table[(1 * 256) + (int)((crc >> 48) & 0xFF)] ^
                Table[(int)(crc >> 56)];

            i += 8;
        }

        for (; i < data.Length; i++)
        {
            crc = Table[(int)((crc ^ data[i]) & 0xFF)] ^ (crc >> 8);
        }

        _crc = crc;
    }

    /// <summary>一次性计算。</summary>
    public static ulong Compute(ReadOnlySpan<byte> data)
    {
        var c = new Crc64();
        c.Update(data);
        return c.Value;
    }
}
