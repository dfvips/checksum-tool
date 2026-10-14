using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

// Avalonia 11.3 起推荐使用 IDataTransfer，但同步的 IDataObject 仍然可用且更简洁，
// 这里显式抑制过时警告以保持代码可读性。
#pragma warning disable CS0618

namespace CheckSumTool.Helpers;

/// <summary>统一的拖拽区行为：高亮、提取文件路径、展开目录。</summary>
public static class DropZone
{
    public const string DraggingClass = "dragging";

    public static void Attach(Border zone, Action<IReadOnlyList<string>> onDrop)
    {
        DragDrop.SetAllowDrop(zone, true);

        zone.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            bool ok = e.Data.Contains(DataFormats.Files);
            e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;

            if (ok)
            {
                zone.Classes.Add(DraggingClass);
            }
            else
            {
                zone.Classes.Remove(DraggingClass);
            }
        });

        zone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => zone.Classes.Remove(DraggingClass));

        zone.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            zone.Classes.Remove(DraggingClass);
            e.Handled = true;

            var paths = ExtractPaths(e.Data);
            if (paths.Count > 0)
            {
                onDrop(paths);
            }
        });
    }

    /// <summary>从拖拽数据中提取本地路径；拖入目录时展开其下一层文件。</summary>
    public static List<string> ExtractPaths(IDataObject data)
    {
        var result = new List<string>();
        var items = data.GetFiles();
        if (items is null)
        {
            return result;
        }

        foreach (var item in items)
        {
            string? path = item.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                try
                {
                    result.AddRange(Directory
                        .EnumerateFiles(path)
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
                }
                catch
                {
                    // 忽略无权限目录
                }
            }
            else
            {
                result.Add(path);
            }
        }

        return result;
    }
}
