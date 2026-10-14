using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CheckSumTool.Core;
using CheckSumTool.Helpers;
using CheckSumTool.Services;

namespace CheckSumTool.ViewModels;

/// <summary>「合并」面板：把多个 .crc64 / .md5 整合成一个。</summary>
public sealed partial class MergeViewModel : PanelViewModelBase
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private const int MaxNameLength = 120;

    public MergeViewModel(AppSettings settings, Action<string, bool> showToast)
        : base(settings, showToast)
    {
        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(FileSummary));
            OnPropertyChanged(nameof(PreviewText));
            OnPropertyChanged(nameof(HasKindConflict));
            OnPropertyChanged(nameof(OutputLocationHint));
            OnPropertyChanged(nameof(OutputTooltip));
            MergeCommand.NotifyCanExecuteChanged();
        };
    }

    public ObservableCollection<ChecksumSourceItem> Files { get; } = new();


    [ObservableProperty]
    private string _outputBaseName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    private bool _isMerging;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    [NotifyPropertyChangedFor(nameof(OutputLocationHint))]
    [NotifyPropertyChangedFor(nameof(OutputTooltip))]
    private HashKind _outputKind = HashKind.Crc64;

    /// <summary>按文件名去重（设置页控制）。</summary>
    public bool Deduplicate => Settings.MergeDeduplicate;

    /// <summary>按文件名排序（设置页控制）。</summary>
    public bool SortByName => Settings.MergeSort;

    public bool HasFiles => Files.Count > 0;

    public string FileSummary => Files.Count == 0
        ? Loc["FilesEmpty"]
        : Loc.Format("MergeFilesSummary", Files.Count, Files.Sum(f => f.EntryCount));

    /// <summary>是否混入了不同算法的校验文件。</summary>
    public bool HasKindConflict => Files.Select(f => f.Kind).Distinct().Count() > 1;

    /// <summary>输出位置说明：单份直接给出路径，多份说明「按文件夹各合并一份」。</summary>
    public string OutputLocationHint
    {
        get
        {
            var paths = ResolveOutputPaths();
            return paths.Count switch
            {
                0 => Loc["MergeOutputDefault"],
                1 => Loc.Format("OutputLocationPath", paths[0]),
                _ => Loc.Format("MergeOutputMulti", paths.Count),
            };
        }
    }

    /// <summary>文件名输入框的提示：记录数预览 + 输出位置（多文件夹时逐条列出实际输出文件）。</summary>
    public string OutputTooltip
    {
        get
        {
            var paths = ResolveOutputPaths();
            if (paths.Count == 0)
            {
                return PreviewText;
            }

            string location = paths.Count == 1
                ? Loc.Format("OutputLocationPath", paths[0])
                : Loc.Format("MergeOutputMulti", paths.Count) + "\n" + string.Join("\n", paths.Take(8));

            return $"{PreviewText}\n{location}";
        }
    }

    public string PreviewText
    {
        get
        {
            if (Files.Count == 0)
            {
                return Loc["MergePreviewEmpty"];
            }

            if (HasKindConflict)
            {
                return Loc["MergePreviewConflict"];
            }

            var (entries, dropped) = BuildMergedEntries();
            return dropped > 0
                ? Loc.Format("MergePreviewDedup", Files.Sum(f => f.EntryCount), entries.Count, dropped)
                : Loc.Format("MergePreviewCount", entries.Count);
        }
    }

    private bool CanMerge => Files.Count > 0 && !HasKindConflict && !IsMerging;

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(FileSummary));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(OutputLocationHint));
        OnPropertyChanged(nameof(OutputTooltip));
    }

    // ---------------------------------------------------------------- 拖入

    public void AddFiles(IReadOnlyList<string> paths)
    {
        // 目录展开为其下一层文件（文件夹选择器返回的是目录路径）
        paths = FilePaths.ExpandDirectories(paths);

        var known = new HashSet<string>(Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        int added = 0;
        int rejected = 0;

        foreach (string path in paths)
        {
            if (!ChecksumFile.IsChecksumFile(path))
            {
                rejected++;
                continue;
            }

            if (!known.Add(path))
            {
                continue;
            }

            var parsed = ChecksumFile.Parse(path);
            Files.Add(new ChecksumSourceItem
            {
                Path = path,
                Kind = parsed.Kind,
                EntryCount = parsed.Entries.Count,
                WarningCount = parsed.Warnings.Count,
                Entries = parsed.Entries,
            });
            added++;
        }

        if (added > 0)
        {
            if (!HasKindConflict)
            {
                OutputKind = Files[0].Kind;
            }

            RefreshStatus();

            // 每次追加后只要仍存在算法混用，就给出警告提示
            if (HasKindConflict)
            {
                ShowWarning("KindConflict");
            }
        }
        else if (rejected > 0)
        {
            SetStatus("StatusOnlyChecksums");
        }
    }

    [RelayCommand]
    private void RemoveFile(ChecksumSourceItem? item)
    {
        if (item is null)
        {
            return;
        }

        Files.Remove(item);

        if (!HasKindConflict && Files.Count > 0)
        {
            OutputKind = Files[0].Kind;
        }

        OnPropertyChanged(nameof(OutputLocationHint));
        OnPropertyChanged(nameof(OutputTooltip));
        ShowToast("ToastRemovedFile", item.FileName);
        RefreshStatus();
    }

    /// <summary>列表变化后刷新状态栏：还有文件就报当前汇总，空列表则回到默认。</summary>
    private void RefreshStatus()
        => SetStatus(Files.Count == 0 ? "StatusReady" : "StatusFilesLoaded", FileSummary);

    [RelayCommand]
    private void Clear()
    {
        Files.Clear();
        SetStatus("StatusReady");
    }

    partial void OnOutputBaseNameChanged(string value)
    {
        string trimmed = value.Trim();
        foreach (HashKind kind in Enum.GetValues<HashKind>())
        {
            foreach (string ext in kind.AllExtensions())
            {
                if (trimmed.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    OutputKind = kind;
                    if (kind == HashKind.Crc32)
                    {
                        Settings.Crc32Extension = ext;
                    }

                    OutputBaseName = trimmed[..^ext.Length];
                    return;
                }
            }
        }

        OnPropertyChanged(nameof(OutputLocationHint));
        OnPropertyChanged(nameof(OutputTooltip));
    }

    // ---------------------------------------------------------------- 合并

    private (List<(string Name, string Hex)> Entries, int Dropped) BuildMergedEntries()
        => BuildMergedEntries(Files);

    /// <summary>
    /// 合并一组校验文件的记录（同一目录内的那些）。
    /// 去重/排序按设置生效，只在组内进行。
    /// </summary>
    private (List<(string Name, string Hex)> Entries, int Dropped) BuildMergedEntries(
        IReadOnlyList<ChecksumSourceItem> files)
    {
        var ordered = new List<(string Name, string Hex)>();
        var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int dropped = 0;

        foreach (var file in files)
        {
            foreach (var entry in file.Entries)
            {
                if (Deduplicate && indexByName.TryGetValue(entry.FileName, out int existing))
                {
                    ordered[existing] = (entry.FileName, entry.HashHex);
                    dropped++;
                    continue;
                }

                indexByName[entry.FileName] = ordered.Count;
                ordered.Add((entry.FileName, entry.HashHex));
            }
        }

        if (SortByName)
        {
            ordered = ordered
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return (ordered, dropped);
    }

    /// <summary>按校验文件所在目录分组（保持首次出现的顺序）。</summary>
    private Dictionary<string, List<ChecksumSourceItem>> GroupByDirectory()
    {
        var groups = new Dictionary<string, List<ChecksumSourceItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Files)
        {
            if (!groups.TryGetValue(file.Directory, out var list))
            {
                groups[file.Directory] = list = new List<ChecksumSourceItem>();
            }

            list.Add(file);
        }

        return groups;
    }

    /// <summary>同一目录内一组校验文件的默认输出名：单文件用文件名，多文件用「第一个 - 最后一个」。</summary>
    private static string BuildGroupName(IReadOnlyList<ChecksumSourceItem> files)
    {
        string first = Trim(Path.GetFileNameWithoutExtension(files[0].FileName));

        if (files.Count == 1)
        {
            return first;
        }

        string last = Trim(Path.GetFileNameWithoutExtension(files[^1].FileName));
        return Trim($"{first} - {last}");
    }

    private static string Trim(string name)
        => name.Length <= MaxNameLength ? name : name[..MaxNameLength];

    /// <summary>
    /// 计算输出路径，按「校验文件所在目录」分组：目录 → 输出文件全路径。
    /// 留空时每个目录各合并一份、按该目录内的校验文件命名；手填文件名时每个目录同名；
    /// 手填绝对路径时所有记录汇总输出到一个文件。
    /// </summary>
    public Dictionary<string, string> ResolveOutputPathsByDirectory()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Files.Count == 0)
        {
            return result;
        }

        string ext = OutputKind == HashKind.Crc32 ? Settings.Crc32Extension : OutputKind.Extension();
        string raw = OutputBaseName.Trim();

        if (raw.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[..^ext.Length];
        }

        raw = raw.Trim().TrimEnd('.', ' ');

        var groups = GroupByDirectory();

        if (Path.IsPathRooted(raw) || raw.Contains('/') || raw.Contains('\\'))
        {
            string full = Path.GetFullPath(raw + ext);
            foreach (string directory in groups.Keys)
            {
                result[directory] = full;
            }

            return result;
        }

        if (raw.Length > 0)
        {
            string name = GenerateViewModel.SanitizeName(raw) + ext;
            foreach (string directory in groups.Keys)
            {
                result[directory] = Path.Combine(directory, name);
            }

            return result;
        }

        foreach (var (directory, files) in groups)
        {
            result[directory] = Path.Combine(directory, GenerateViewModel.SanitizeName(BuildGroupName(files)) + ext);
        }

        return result;
    }

    public IReadOnlyList<string> ResolveOutputPaths()
        => ResolveOutputPathsByDirectory()
            .Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private void Merge()
    {
        if (HasKindConflict)
        {
            ShowWarning("KindConflict");
            return;
        }

        var outputPaths = ResolveOutputPathsByDirectory();
        if (outputPaths.Count == 0)
        {
            SetStatus("StatusInvalidName");
            return;
        }

        IsMerging = true;
        try
        {
            NumberFormat format = OutputKind switch
            {
                HashKind.Md5 => NumberFormat.Hex,
                HashKind.Crc64 => NumberFormat.Decimal,
                HashKind.Crc32 => NumberFormat.Decimal,
                _ => NumberFormat.Hex,
            };

            int written = 0;
            int records = 0;
            int dropped = 0;
            string lastName = string.Empty;

            // 按文件夹分别合并：每个目录只收本目录内校验文件的记录，输出到本目录，
            // 这样合并结果与记录里的相对文件名仍然对得上（跨目录合并会指向别处的文件）。
            foreach (var (directory, files) in GroupByDirectory())
            {
                var (entries, groupDropped) = BuildMergedEntries(files);
                if (entries.Count == 0)
                {
                    continue;
                }

                string outputPath = outputPaths[directory];
                // 合并目标为 .sfv 时同样写 SFV 原生格式（HashCheck 兼容）。
                string content = ChecksumFile.IsSfvTarget(outputPath, OutputKind)
                    ? ChecksumFile.FormatSfv(entries)
                    : string.Join("\n", entries.Select(e => ChecksumFile.FormatLine(e.Hex, e.Name, format))) + "\n";

                File.WriteAllText(outputPath, content, Utf8NoBom);

                written++;
                records += entries.Count;
                dropped += groupDropped;
                lastName = Path.GetFileName(outputPath);
            }

            if (written == 0)
            {
                SetStatus("StatusNoExport");
                return;
            }

            if (written == 1)
            {
                SetStatus(
                    dropped > 0 ? "StatusMergedDedup" : "StatusMerged",
                    Files.Count,
                    lastName,
                    records,
                    dropped);
                ShowToast("ToastMergeSuccess", lastName);
            }
            else
            {
                SetStatus(
                    dropped > 0 ? "StatusMergedManyDedup" : "StatusMergedMany",
                    Files.Count,
                    written,
                    records,
                    dropped);
                ShowToast("ToastMergeSuccessMany", written);
            }
        }
        catch (Exception ex)
        {
            SetStatus("StatusMergeFailed", ex.Message);
        }
        finally
        {
            IsMerging = false;
        }
    }
}
