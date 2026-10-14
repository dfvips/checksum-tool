namespace CheckSumTool.Helpers;

/// <summary>路径列表处理：目录展开为文件。</summary>
public static class FilePaths
{
    /// <summary>导入目录时要静默跳过的系统杂物文件。</summary>
    private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini",
    };

    /// <summary>
    /// 把路径列表里的**目录**展开为其下一层文件（非递归），普通文件原样保留。
    /// 目录顺序按文件名字典序，保证「选择文件夹」与拖入目录的结果稳定可预期。
    /// </summary>
    /// <remarks>
    /// 拖拽与「选择文件夹」两处入口都要经过这里：
    /// 文件夹选择器返回的是目录路径，若不展开就会把整个目录当成一个文件加进列表。
    /// <para>
    /// 展开目录时会静默跳过两类文件：以点开头的隐藏文件（<c>.DS_Store</c>、<c>.gitignore</c> 等）
    /// 以及 <c>Thumbs.db</c> / <c>desktop.ini</c> 等系统杂物。它们不会传给上层，
    /// 因此也不会触发「已忽略」之类的提示——导入一个 macOS 文件夹不该有噪音。
    /// </para>
    /// <para>
    /// 注意：隐藏文件只在**目录展开**时过滤；用户显式拖入的单个文件即使以点开头也照常保留。
    /// </para>
    /// </remarks>
    public static List<string> ExpandDirectories(IEnumerable<string> paths)
    {
        var result = new List<string>();

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                try
                {
                    result.AddRange(Directory
                        .EnumerateFiles(path)
                        .Where(p => !IsJunk(p) && !IsDotHidden(p))
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
                }
                catch
                {
                    // 忽略无权限或已消失的目录
                }
            }
            else if (!IsJunk(path))
            {
                result.Add(path);
            }
        }

        return result;
    }

    /// <summary>是否为应当静默忽略的系统杂物文件。</summary>
    private static bool IsJunk(string path) => JunkNames.Contains(Path.GetFileName(path));

    /// <summary>文件名是否以点开头（Unix 隐藏文件，如 <c>.DS_Store</c>、<c>.gitignore</c>、<c>.hidden.mp4</c>）。</summary>
    private static bool IsDotHidden(string path)
    {
        string name = Path.GetFileName(path);
        return name.Length > 0 && name[0] == '.';
    }

    /// <summary>供拖拽入径判断单个路径要不要忽略。</summary>
    public static bool IsIgnorable(string path)
        => !string.IsNullOrWhiteSpace(path) && IsJunk(path);
}
