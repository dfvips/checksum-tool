namespace CheckSumTool.Core;

/// <summary>
/// CRC-32（IEEE 802.3，PKZip / SFV 工具所用变体）：
/// 标准多项式 <c>0x04C11DB7</c>（按位反序后的等价形式 <c>0xEDB88320</c>）、
/// init=<c>0xFFFFFFFF</c>、refin/refout=<c>true</c>、xorout=<c>0xFFFFFFFF</c>。
/// 校验值 <c>"123456789"</c> = <c>0xCBF43926</c> = <c>3421780262</c>。
///
/// 输出格式：.sfv 工具普遍采用 8 位小写十六进制，本类也采用这种形式以保证与现有工具兼容。
/// </summary>
public sealed class Crc32
{
    // 采用反序（右移）实现时，必须使用反序后的多项式常量 0xEDB88320（0x04C11DB7 的位反序等价形式）。
    private const uint Polynomial = 0xEDB88320u;

    /// <summary>256 项查表。</summary>
    private static readonly uint[] Table = BuildTable();

    private uint _crc = uint.MaxValue;

    /// <summary>当前校验值（已应用 xorout）。</summary>
    public uint Value => ~_crc;

    /// <summary>复位到初始状态。</summary>
    public void Reset() => _crc = uint.MaxValue;

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }

    /// <summary>追加一段数据。</summary>
    public void Update(ReadOnlySpan<byte> data)
    {
        uint crc = _crc;
        for (int i = 0; i < data.Length; i++)
        {
            crc = Table[(int)((crc ^ data[i]) & 0xFF)] ^ (crc >> 8);
        }

        _crc = crc;
    }

    /// <summary>一次性计算。</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var c = new Crc32();
        c.Update(data);
        return c.Value;
    }
}