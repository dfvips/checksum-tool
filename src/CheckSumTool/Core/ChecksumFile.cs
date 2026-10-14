using System.Globalization;
using System.Text;

namespace CheckSumTool.Core;

/// <summary>校验文件中的一行记录：<c>&lt;校验值&gt; *&lt;文件名&gt;</c>。</summary>
public sealed class ChecksumEntry
{
    /// <summary>记录中的文件名（通常为纯文件名，不含目录）。</summary>
    public required string FileName { get; init; }

    /// <summary>归一化后的十六进制校验值（小写）。</summary>
    public required string HashHex { get; init; }

    /// <summary>文件中原始书写的校验值。</summary>
    public required string RawHash { get; init; }

    /// <summary>来源校验文件的完整路径。</summary>
    public required string SourcePath { get; init; }

    /// <summary>来源文件中的行号（从 1 开始）。</summary>
    public int LineNumber { get; init; }

    /// <summary>该记录所属的校验类型。</summary>
    public required HashKind Kind { get; init; }
}

/// <summary>一个校验文件（.crc64 / .md5 / .sha1 / .sha256 / .sha384 / .sha512 / .sha3-256 / .sha3-384 / .sha3-512 / .crc32 / .sfv）的解析结果。</summary>
public sealed class ParsedChecksumFile
{
    public required string Path { get; init; }
    public required HashKind Kind { get; init; }
    public List<ChecksumEntry> Entries { get; } = new();
    public List<string> Warnings { get; } = new();

    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}

/// <summary>校验文本文件的读写。</summary>
public static class ChecksumFile
{
    public const string Crc64Extension = ".crc64";
    public const string Md5Extension = ".md5";
    public const string Sha1Extension = ".sha1";
    public const string Sha256Extension = ".sha256";
    public const string Sha384Extension = ".sha384";
    public const string Sha512Extension = ".sha512";
    public const string Sha3_256Extension = ".sha3-256";
    public const string Sha3_384Extension = ".sha3-384";
    public const string Sha3_512Extension = ".sha3-512";

    /// <summary>CRC-32 首选扩展名（<c>.crc32</c> 为优先格式，界面默认输出与下拉标签均使用它）。</summary>
    public const string Crc32Extension = ".crc32";

    /// <summary>CRC-32 兼容扩展名（Simple File Verification，旧工具常用，仅为读取/兼容保留）。</summary>
    public const string Crc32AltExtension = ".sfv";

    /// <summary>判断路径是否为本工具可识别的校验文件。</summary>
    public static bool IsChecksumFile(string path)
    {
        HashKind? kind = HashKindExtensions.FromPath(path);
        return kind.HasValue;
    }

    /// <summary>解析一个校验文件。</summary>
    public static ParsedChecksumFile Parse(string path)
    {
        HashKind? detected = HashKindExtensions.FromPath(path);
        if (detected is null)
        {
            return new ParsedChecksumFile
            {
                Path = path,
                Kind = HashKind.Crc64,
                Warnings = { $"不支持的校验文件扩展名：{Path.GetExtension(path)}" },
            };
        }

        HashKind kind = detected.Value;
        var result = new ParsedChecksumFile { Path = path, Kind = kind };

        string[] lines;
        try
        {
            // detectEncodingFromByteOrderMarks: true —— 同时兼容 UTF-8(含/不含 BOM) 与 UTF-16。
            using var reader = new StreamReader(path, System.Text.Encoding.UTF8, true);
            lines = reader.ReadToEnd().Split('\n');
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"无法读取文件：{ex.Message}");
            return result;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            // 行首尾空白都剥掉（HashCheck 同样容忍行首空格、注释前空白）。
            string raw = lines[i].Trim(' ', '\t', '\r').TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (raw.StartsWith('#') || raw.StartsWith(';'))
            {
                continue;
            }

            if (TryParseLine(raw, kind, path, i + 1, out ChecksumEntry? entry, out string? warning))
            {
                result.Entries.Add(entry!);
            }
            else if (warning is not null)
            {
                result.Warnings.Add($"第 {i + 1} 行：{warning}");
            }
        }

        return result;
    }

    /// <summary>解析单行文本。支持 <c>hash *name</c>、<c>hash  name</c> 以及 <c>name====hash</c> 三种写法。</summary>
    private static bool TryParseLine(
        string line,
        HashKind kind,
        string sourcePath,
        int lineNumber,
        out ChecksumEntry? entry,
        out string? warning)
    {
        entry = null;
        warning = null;

        string? hashToken = null;
        string? name = null;

        // 兼容旧版控制台工具输出的 "文件名====十进制校验值" 形式。
        int eq = line.IndexOf("====", StringComparison.Ordinal);
        if (eq > 0)
        {
            string left = line[..eq].Trim();
            string right = line[(eq + 4)..].Trim();
            if (IsValidHashToken(right, kind) && !IsValidHashToken(left, kind))
            {
                hashToken = right;
                name = left;
            }
        }

        if (hashToken is null)
        {
            int sep = IndexOfWhitespace(line);
            if (sep <= 0)
            {
                warning = "无法识别的格式，已跳过";
                return false;
            }

            hashToken = line[..sep].Trim();
            name = line[sep..].TrimStart();
            if (name.StartsWith('*'))
            {
                name = name[1..];
            }

            name = name.Trim();

            // SFV 反序格式（HashCheck / winSFV 等 Windows 工具）：<文件名> <CRC32>。
            // 仅当行首 token 不是合法校验值时才尝试，保证 <hash> *<name>、<hash>  <name> 优先解析。
            if (kind == HashKind.Crc32 && !IsValidHashToken(hashToken, kind))
            {
                int lastSep = LastIndexOfWhitespace(line);
                if (lastSep > 0)
                {
                    string tail = line[(lastSep + 1)..].Trim();
                    string head = line[..lastSep].TrimEnd();
                    if (head.Length > 0 && IsValidHashToken(tail, kind))
                    {
                        hashToken = tail;
                        name = head;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(name))
        {
            warning = "缺少文件名，已跳过";
            return false;
        }

        if (!TryNormalizeHash(hashToken, kind, out string hex))
        {
            warning = $"校验值 \"{hashToken}\" 不是合法的 {kind.DisplayName()} 值，已跳过";
            return false;
        }

        entry = new ChecksumEntry
        {
            FileName = name,
            HashHex = hex,
            RawHash = hashToken,
            SourcePath = sourcePath,
            LineNumber = lineNumber,
            Kind = kind,
        };
        return true;
    }

    private static int IndexOfWhitespace(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsWhiteSpace(s[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int LastIndexOfWhitespace(string s)
    {
        for (int i = s.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(s[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsValidHashToken(string token, HashKind kind)
        => TryNormalizeHash(token, kind, out _);

    /// <summary>
    /// 把文件中书写的校验值归一化为定长小写十六进制字符串。
    /// CRC-64 同时接受十进制整数与十六进制；其他算法只接受定长十六进制；
    /// CRC-32 同时接受 8 位十六进制与十进制整数。
    /// </summary>
    public static bool TryNormalizeHash(string token, HashKind kind, out string hex)
    {
        hex = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        token = token.Trim().Trim('"');

        if (kind is HashKind.Md5 or HashKind.Sha1
            or HashKind.Sha256 or HashKind.Sha384 or HashKind.Sha512
            or HashKind.Sha3_256 or HashKind.Sha3_384 or HashKind.Sha3_512)
        {
            int expected = kind.HexLength();
            if (token.Length != expected || !IsHex(token))
            {
                return false;
            }

            hex = token.ToLowerInvariant();
            return true;
        }

        if (kind == HashKind.Crc32)
        {
            // 兼容 0x 前缀的 8 位十六进制
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string body = token[2..];
                if (body.Length is 0 or > 8 || !IsHex(body))
                {
                    return false;
                }

                hex = uint.Parse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    .ToString("x8", CultureInfo.InvariantCulture);
                return true;
            }

            bool hasHexLetter = token.Any(c => (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

            if (hasHexLetter)
            {
                // 含 a-f 必然是十六进制
                if (token.Length is 0 or > 8 || !IsHex(token))
                {
                    return false;
                }

                hex = uint.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    .ToString("x8", CultureInfo.InvariantCulture);
                return true;
            }

            // 纯数字：按十进制解释（uint 范围 0~4294967295）
            if (token.Length <= 10 && uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out uint dec))
            {
                hex = dec.ToString("x8", CultureInfo.InvariantCulture);
                return true;
            }

            // 兜底：长度正好 8 的十六进制
            if (token.Length == 8 && IsHex(token))
            {
                hex = uint.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    .ToString("x8", CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        if (kind == HashKind.Crc64)
        {
            bool hasHexPrefix = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (hasHexPrefix)
            {
                string body = token[2..];
                if (body.Length is 0 or > 16 || !IsHex(body))
                {
                    return false;
                }

                hex = ulong.Parse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    .ToString("x16", CultureInfo.InvariantCulture);
                return true;
            }

            bool hasHexLetter = token.Any(c => (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

            if (hasHexLetter)
            {
                // 含 a-f 必然是十六进制（十进制不会有字母）。
                if (token.Length is 0 or > 16 || !IsHex(token))
                {
                    return false;
                }

                hex = ulong.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    .ToString("x16", CultureInfo.InvariantCulture);
                return true;
            }

            // 纯数字：优先按十进制解释（与常见 crc64 文本一致）。
            if (token.Length <= 20 && ulong.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out ulong dec))
            {
                hex = dec.ToString("x16", CultureInfo.InvariantCulture);
                return true;
            }

            // 兜底：长度正好 16 的十六进制。
            if (token.Length == 16 && IsHex(token))
            {
                hex = ulong.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    .ToString("x16", CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool IsHex(string s)
    {
        foreach (char c in s)
        {
            bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok)
            {
                return false;
            }
        }

        return s.Length > 0;
    }

    /// <summary>把归一化后的十六进制值还原成文件中要书写的文本。</summary>
    public static string FormatHash(string hex, NumberFormat format)
    {
        if (format == NumberFormat.Decimal && hex.Length == 16)
        {
            // CRC64：十进制无符号整数（与 crc64ecma.cs 的 ulong 输出一致）
            return ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
        }

        if (format == NumberFormat.Decimal && hex.Length == 8)
        {
            // CRC-32：十进制无符号整数
            return uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
        }

        return hex.ToLowerInvariant();
    }

    /// <summary>生成一行记录文本：<c>&lt;校验值&gt; *&lt;文件名&gt;</c>。</summary>
    public static string FormatLine(string hashHex, string fileName, NumberFormat format)
        => $"{FormatHash(hashHex, format)} *{fileName}";

    /// <summary>
    /// 生成 SFV 原生格式文本（Simple File Verification，<c>.sfv</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// HashCheck（Windows 资源管理器扩展）对 CRC-32 使用的容器就是 <c>.sfv</c>，
    /// 其写入格式为「文件名在前、CRC-32 在后」的定宽对齐列：
    /// <c>%-{最长文件名长度}s %s\r\n</c>，即文件名左对齐、按最长文件名补空格、
    /// 再跟一个空格、最后是小写 8 位十六进制 CRC-32；文件不含文件头，';' 开头的行视为注释。
    /// </para>
    /// <para>
    /// 行尾按项目约定用 LF（HashCheck 读取时会先把 \r 归一化为 \n，LF 内容同样可解析）。
    /// </para>
    /// </remarks>
    public static string FormatSfv(IReadOnlyList<(string Name, string Hex)> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        int width = 0;
        foreach (var (name, _) in entries)
        {
            width = Math.Max(width, name.Length);
        }

        var sb = new StringBuilder(entries.Count * (width + 12));
        foreach (var (name, hex) in entries)
        {
            sb.Append(name.PadRight(width))
                .Append(' ')
                .Append(FormatHash(hex, NumberFormat.Hex))
                .Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>目标路径是否应按 SFV 原生格式书写（<c>.sfv</c> 且算法为 CRC-32）。</summary>
    public static bool IsSfvTarget(string path, HashKind kind)
        => kind == HashKind.Crc32
            && path.EndsWith(Crc32AltExtension, StringComparison.OrdinalIgnoreCase);
}