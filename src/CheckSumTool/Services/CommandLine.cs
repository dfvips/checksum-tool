using CheckSumTool.Core;

namespace CheckSumTool.Services;

/// <summary>命令行参数解析：支持通过文件关联或显式 <c>--verify</c> 直接进入校验。</summary>
public static class CommandLine
{
    /// <summary>
    /// 从参数中提取需要校验的校验文件路径。
    /// 支持两种形式：
    /// <list type="bullet">
    ///   <item>文件关联直接传入：<c>CheckSumTool.exe "D:\a\b.crc64"</c></item>
    ///   <item>显式指定：<c>CheckSumTool.exe --verify "D:\a\b.crc64"</c></item>
    /// </list>
    /// </summary>
    public static List<string> ExtractChecksumFiles(IEnumerable<string> args)
    {
        var result = new List<string>();
        var list = args.ToList();

        for (int i = 0; i < list.Count; i++)
        {
            string arg = list[i];

            if (arg is "--verify" or "-V")
            {
                if (i + 1 < list.Count)
                {
                    Add(result, list[++i]);
                }

                continue;
            }

            // 其余以 - 开头的视为选项，忽略
            if (arg.StartsWith('-'))
            {
                continue;
            }

            Add(result, arg);
        }

        return result;
    }

    private static void Add(List<string> result, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string full = Path.GetFullPath(path);

        if (File.Exists(full)
            && ChecksumFile.IsChecksumFile(full)
            && !result.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(full);
        }
    }
}
