using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace CheckSumTool.Helpers;

/// <summary>调用系统文件/文件夹选择器。</summary>
public static class FilePicker
{
    /// <summary>校验文件类型过滤器：覆盖全部 10 种算法的 11 种扩展名，同时给出「所有文件」兜底。</summary>
    public static readonly FilePickerFileType ChecksumFileType = new(
        "校验文件 (*.crc64, *.md5, *.sha1, *.sha256, *.sha384, *.sha512, *.sha3-256, *.sha3-384, *.sha3-512, *.crc32, *.sfv)")
    {
        Patterns =
        [
            "*.crc64", "*.md5",
            "*.sha1", "*.sha256", "*.sha384", "*.sha512",
            "*.sha3-256", "*.sha3-384", "*.sha3-512",
            "*.crc32", "*.sfv",
        ],
        AppleUniformTypeIdentifiers = ["public.data"],
        MimeTypes = ["application/octet-stream"],
    };

    /// <summary>选择一个或多个文件。</summary>
    public static async Task<IReadOnlyList<string>> PickFilesAsync(
        Visual owner,
        string title,
        bool checksumOnly = false)
    {
        var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (storage is null || !storage.CanOpen)
        {
            return Array.Empty<string>();
        }

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = checksumOnly
                ? [ChecksumFileType, FilePickerFileTypes.All]
                : [FilePickerFileTypes.All],
        };

        var files = await storage.OpenFilePickerAsync(options);
        return ToLocalPaths(files);
    }

    /// <summary>选择一个或多个文件夹（拖入后会自动展开其下一层文件）。</summary>
    public static async Task<IReadOnlyList<string>> PickFoldersAsync(Visual owner, string title)
    {
        var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (storage is null || !storage.CanPickFolder)
        {
            return Array.Empty<string>();
        }

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
        };

        var folders = await storage.OpenFolderPickerAsync(options);
        return ToLocalPaths(folders);
    }

    private static IReadOnlyList<string> ToLocalPaths(IEnumerable<IStorageItem> items)
    {
        var paths = new List<string>();

        foreach (var item in items)
        {
            string? path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }
}
