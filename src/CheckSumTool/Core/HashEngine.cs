using System.Buffers;
using System.Security.Cryptography;

namespace CheckSumTool.Core;

/// <summary>一次哈希计算的结果。</summary>
public readonly record struct HashOutcome(string Hex, long Bytes, TimeSpan Elapsed)
{
    public bool Cancelled { get; init; }
}

/// <summary>流式哈希计算（CRC64 / MD5 / SHA-1 / SHA-256 / SHA-384 / SHA-512 / SHA3-256 / SHA3-384 / SHA3-512 / CRC-32），
/// 支持进度回调与取消。</summary>
public static class HashEngine
{
    // 4MB：足够大的顺序读取块，既能摊薄系统调用开销，又不会过度占用内存。
    // （实测 1MB 与 4MB 在热缓存下吞吐相同，冷缓存时更大的块略微占优）
    private const int BufferSize = 1024 * 1024 * 4;

    /// <summary>
    /// 按算法名缓存「原生 SHA3 是否可用」的探测结果。
    /// Windows / Linux 的 .NET 通常提供原生 SHA3（有硬件加速或高度优化实现），此时优先使用；
    /// macOS 上 .NET 与系统库都不提供，探测失败后回退到纯托管 Keccak-f。
    /// 逐算法探测（而不是只看 SHA3-256），以便某个变体可用时也能用上原生实现。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> s_nativeSha3Support = new();

    private static bool IsNativeSha3Available(HashAlgorithmName name)
    {
        string key = name.Name ?? string.Empty;
        if (s_nativeSha3Support.TryGetValue(key, out bool cached))
        {
            return cached;
        }

        bool supported;
        try
        {
            using var probe = IncrementalHash.CreateHash(name);
            supported = true;
        }
        catch
        {
            supported = false;
        }

        s_nativeSha3Support[key] = supported;
        return supported;
    }

    /// <summary>
    /// 计算单个文件的校验值。返回小写十六进制字符串。
    /// </summary>
    /// <param name="path">目标文件路径。</param>
    /// <param name="kind">算法。</param>
    /// <param name="onBytesRead">已读取字节数的增量回调。</param>
    /// <param name="ct">取消令牌。</param>
    public static Task<HashOutcome> ComputeAsync(
        string path,
        HashKind kind,
        Action<long>? onBytesRead,
        CancellationToken ct)
        => Task.Run(() => Compute(path, kind, onBytesRead, ct), CancellationToken.None);

    private static HashOutcome Compute(
        string path,
        HashKind kind,
        Action<long>? onBytesRead,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long total = 0;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.SequentialScan);

            // SHA3-256 / SHA3-384 / SHA3-512：优先用原生 IncrementalHash（Windows/Linux 有硬件加速或高度优化实现），
            // 不支持的平台（macOS）回退到纯托管 Keccak-f。逐算法探测，可用即用原生。
            if (kind is HashKind.Sha3_256 or HashKind.Sha3_384 or HashKind.Sha3_512)
            {
                HashAlgorithmName sha3Name = kind switch
                {
                    HashKind.Sha3_256 => new HashAlgorithmName("SHA3-256"),
                    HashKind.Sha3_384 => new HashAlgorithmName("SHA3-384"),
                    _ => new HashAlgorithmName("SHA3-512"),
                };

                if (IsNativeSha3Available(sha3Name))
                {
                    using var inc = IncrementalHash.CreateHash(sha3Name);

                    int m;
                    while ((m = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        inc.AppendData(buffer, 0, m);
                        total += m;
                        onBytesRead?.Invoke(m);
                    }

                    string sha3Hex = Convert.ToHexString(inc.GetHashAndReset()).ToLowerInvariant();
                    return new HashOutcome(sha3Hex, total, sw.Elapsed);
                }
            }

            HashAlgorithmName? algo = kind switch
            {
                HashKind.Md5 => HashAlgorithmName.MD5,
                HashKind.Sha1 => HashAlgorithmName.SHA1,
                HashKind.Sha256 => HashAlgorithmName.SHA256,
                HashKind.Sha384 => HashAlgorithmName.SHA384,
                HashKind.Sha512 => HashAlgorithmName.SHA512,
                _ => null,
            };

            if (algo is not null)
            {
                using var inc = IncrementalHash.CreateHash(algo.Value);
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    inc.AppendData(buffer, 0, n);
                    total += n;
                    onBytesRead?.Invoke(n);
                }

                string hex = Convert.ToHexString(inc.GetHashAndReset()).ToLowerInvariant();
                return new HashOutcome(hex, total, sw.Elapsed);
            }

            if (kind == HashKind.Crc64)
            {
                var crc = new Crc64();
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    crc.Update(buffer.AsSpan(0, n));
                    total += n;
                    onBytesRead?.Invoke(n);
                }

                return new HashOutcome(crc.Value.ToString("x16"), total, sw.Elapsed);
            }

            if (kind == HashKind.Crc32)
            {
                var crc = new Crc32();
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    crc.Update(buffer.AsSpan(0, n));
                    total += n;
                    onBytesRead?.Invoke(n);
                }

                return new HashOutcome(crc.Value.ToString("x8"), total, sw.Elapsed);
            }

            if (kind is HashKind.Sha3_256 or HashKind.Sha3_384 or HashKind.Sha3_512)
            {
                var sha3 = kind switch
                {
                    HashKind.Sha3_256 => Sha3.Sha256(),
                    HashKind.Sha3_384 => Sha3.Sha384(),
                    _ => Sha3.Sha512(),
                };
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    sha3.Update(buffer.AsSpan(0, n));
                    total += n;
                    onBytesRead?.Invoke(n);
                }

                return new HashOutcome(Convert.ToHexString(sha3.GetHash()).ToLowerInvariant(), total, sw.Elapsed);
            }

            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported hash kind");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 把校验文件里记录的文件名解析为真实路径：绝对路径直接使用，
    /// 否则相对校验文件所在目录解析。
    /// </summary>
    public static string ResolveTargetPath(string checksumFilePath, string entryFileName)
    {
        string dir = Path.GetDirectoryName(checksumFilePath) ?? string.Empty;

        string normalized = entryFileName
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalized))
        {
            return normalized;
        }

        return Path.GetFullPath(Path.Combine(dir, normalized));
    }

    /// <summary>人性化显示字节数。</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value:0.##} {units[unit]}";
    }

    /// <summary>人性化显示耗时。</summary>
    public static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 1)
        {
            return $"{ts.TotalMilliseconds:0} ms";
        }

        if (ts.TotalMinutes < 1)
        {
            return $"{ts.TotalSeconds:0.00} s";
        }

        return $"{(int)ts.TotalMinutes} 分 {ts.Seconds} 秒";
    }
}
