namespace CheckSumTool.Core;

/// <summary>校验算法类型。</summary>
public enum HashKind
{
    /// <summary>CRC-64/XZ，128 位以下、16 个十六进制字符或 20 位十进制整数。</summary>
    Crc64 = 0,

    /// <summary>MD5，32 个十六进制字符。</summary>
    Md5 = 1,

    /// <summary>SHA-1，40 个十六进制字符（已不推荐用于安全场景，但仍是常见的校验文件格式）。</summary>
    Sha1 = 2,

    /// <summary>SHA-256，64 个十六进制字符。</summary>
    Sha256 = 3,

    /// <summary>SHA-512，128 个十六进制字符。</summary>
    Sha512 = 4,

    /// <summary>SHA3-256，64 个十六进制字符。</summary>
    Sha3_256 = 5,

    /// <summary>SHA3-512，128 个十六进制字符。</summary>
    Sha3_512 = 6,

    /// <summary>CRC-32（IEEE 802.3），8 个十六进制字符或最多 10 位十进制整数。</summary>
    Crc32 = 7,

    /// <summary>SHA-384，96 个十六进制字符（已不推荐用于安全场景，但仍是常见的校验文件格式）。</summary>
    Sha384 = 8,

    /// <summary>SHA3-384，96 个十六进制字符。</summary>
    Sha3_384 = 9,
}

/// <summary>
/// 校验值在文件中的书写形式。
/// 注意：CRC64 固定使用 <see cref="Decimal"/>（与 crc64ecma.cs 一致），界面上不提供进制选择；
/// 该枚举仅作为读写接口的参数保留。
/// </summary>
public enum NumberFormat
{
    /// <summary>十进制无符号整数（crc64 的固定写法，例如 6967259512934720053）。</summary>
    Decimal = 0,

    /// <summary>十六进制小写，定长 16 位。仅用于 MD5 等超过 64 位的值。</summary>
    Hex = 1,
}

public static class HashKindExtensions
{
    /// <summary>每个算法对应的首选输出扩展名（含点，小写）。</summary>
    public static string Extension(this HashKind kind) => kind switch
    {
        HashKind.Crc64 => ".crc64",
        HashKind.Md5 => ".md5",
        HashKind.Sha1 => ".sha1",
        HashKind.Sha256 => ".sha256",
        HashKind.Sha512 => ".sha512",
        HashKind.Sha3_256 => ".sha3-256",
        HashKind.Sha3_512 => ".sha3-512",
        HashKind.Crc32 => ".crc32",
        HashKind.Sha384 => ".sha384",
        HashKind.Sha3_384 => ".sha3-384",
        _ => ".crc64",
    };

    /// <summary>该算法可被识别的所有扩展名（含点，小写）。首项为首选扩展名。</summary>
    /// <remarks>
    /// CRC-32 同时识别 <c>.crc32</c>（优先格式）与 <c>.sfv</c>（Simple File Verification，仅为兼容旧工具保留）。
    /// </remarks>
    public static IReadOnlyList<string> AllExtensions(this HashKind kind) => kind switch
    {
        HashKind.Crc32 => [".crc32", ".sfv"],
        _ => [kind.Extension()],
    };

    /// <summary>用于展示的名称。</summary>
    public static string DisplayName(this HashKind kind) => kind switch
    {
        HashKind.Crc64 => "CRC64",
        HashKind.Md5 => "MD5",
        HashKind.Sha1 => "SHA-1",
        HashKind.Sha256 => "SHA-256",
        HashKind.Sha512 => "SHA-512",
        HashKind.Sha3_256 => "SHA3-256",
        HashKind.Sha3_512 => "SHA3-512",
        HashKind.Crc32 => "CRC-32",
        HashKind.Sha384 => "SHA-384",
        HashKind.Sha3_384 => "SHA3-384",
        _ => "CRC64",
    };

    /// <summary>校验值的小写十六进制字符串长度。</summary>
    public static int HexLength(this HashKind kind) => kind switch
    {
        HashKind.Crc64 => 16,
        HashKind.Md5 => 32,
        HashKind.Sha1 => 40,
        HashKind.Sha256 => 64,
        HashKind.Sha3_256 => 64,
        HashKind.Sha512 => 128,
        HashKind.Sha3_512 => 128,
        HashKind.Crc32 => 8,
        HashKind.Sha384 => 96,
        HashKind.Sha3_384 => 96,
        _ => 16,
    };

    /// <summary>把任意扩展名（<c>.sfv</c>、<c>.crc32</c> 等）映射回算法；未识别时返回 null。</summary>
    public static HashKind? FromPath(string path)
    {
        string ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            return null;
        }

        foreach (HashKind kind in Enum.GetValues<HashKind>())
        {
            foreach (string allowed in kind.AllExtensions())
            {
                if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    return kind;
                }
            }
        }

        return null;
    }
}