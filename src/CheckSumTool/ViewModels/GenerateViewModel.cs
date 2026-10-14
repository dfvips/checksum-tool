using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CheckSumTool.Core;
using CheckSumTool.Helpers;
using CheckSumTool.Services;

namespace CheckSumTool.ViewModels;

/// <summary>「生成」面板：把普通文件计算成 .crc64 / .md5，输出到源文件所在目录。</summary>
public sealed partial class GenerateViewModel : PanelViewModelBase
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private const int MaxNameLength = 120;

    private CancellationTokenSource? _cts;
    private long _lastReportTicks;

    /// <summary>用户是否手动改过文件名；手动改过之后就不再自动跟随文件列表。</summary>
    private bool _nameTouched;

    /// <summary>程序内部正在赋值文件名（用于区分用户输入）。</summary>
    private bool _settingName;

    public GenerateViewModel(AppSettings settings, Action<string, bool> showToast)
        : base(settings, showToast)
    {
        foreach (var format in CreateOutputFormats())
        {
            format.RefreshLabel(Loc[format.LabelKey]);
            OutputFormats.Add(format);
            format.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GenerateFormatOption.IsSelected))
                {
                    StartCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(OutputPreview));
                    OnPropertyChanged(nameof(OutputLocationHint));
                }
            };
        }

        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(FileSummary));
            OnPropertyChanged(nameof(OutputLocationHint));
            OnPropertyChanged(nameof(OutputPreview));
            StartCommand.NotifyCanExecuteChanged();

            if (Files.Count == 0)
            {
                _nameTouched = false;
            }

            SyncDefaultName();
        };
    }

    public ObservableCollection<SourceFileItem> Files { get; } = new();

    /// <summary>生成面板可多选的输出格式，按名称排序。</summary>
    public ObservableCollection<GenerateFormatOption> OutputFormats { get; } = new();

    private static IReadOnlyList<GenerateFormatOption> CreateOutputFormats() =>
    [
        new(HashKind.Crc32, ".crc32", "OutputKindCrc32"),
        new(HashKind.Crc32, ".sfv", "OutputKindCrc32Sfv"),
        new(HashKind.Crc64, ".crc64", "OutputKindCrc64", isSelected: true),
        new(HashKind.Md5, ".md5", "OutputKindMd5"),
        new(HashKind.Sha1, ".sha1", "OutputKindSha1"),
        new(HashKind.Sha256, ".sha256", "OutputKindSha256"),
        new(HashKind.Sha384, ".sha384", "OutputKindSha384"),
        new(HashKind.Sha512, ".sha512", "OutputKindSha512"),
        new(HashKind.Sha3_256, ".sha3-256", "OutputKindSha3_256"),
        new(HashKind.Sha3_384, ".sha3-384", "OutputKindSha3_384"),
        new(HashKind.Sha3_512, ".sha3-512", "OutputKindSha3_512"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyPropertyChangedFor(nameof(DropHint))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentText))]
    private double _progress;

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private int _doneCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _outputBaseName = string.Empty;

    public string ProgressPercentText => $"{Progress:0}%";

    public bool HasFiles => Files.Count > 0;

    public string FileSummary => Files.Count == 0
        ? Loc["FilesEmpty"]
        : Loc.Format("FilesSummary", Files.Count, HashEngine.FormatSize(Files.Sum(f => f.Size)));

    public string DropHint => Loc[IsRunning ? "GenerateDropHintBusy" : "GenerateDropHint"];

    public string OutputLocationHint
    {
        get
        {
            if (Files.Count == 0)
            {
                return Loc["OutputLocationSame"];
            }

            if (IsExplicitPath(OutputBaseName))
            {
                return Loc.Format("OutputLocationPath", OutputPreview);
            }

            var dirs = Files.Select(f => f.Directory).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            return dirs.Count == 1
                ? Loc.Format("OutputLocationPath", dirs[0])
                : Loc.Format("OutputLocationMulti", dirs.Count);
        }
    }

    public string OutputPreview => string.Join(
        Environment.NewLine,
        OutputFormats.Where(f => f.IsSelected)
            .SelectMany(ResolveOutputPaths)
            .Take(12));

    public string SelectedFormatSummary => string.Join(" · ", OutputFormats.Where(f => f.IsSelected).Select(f => f.Label));

    private bool CanStart => !IsRunning && Files.Count > 0 && OutputFormats.Any(f => f.IsSelected);

    private bool CanStop => IsRunning;

    private bool CanClear => !IsRunning;

    protected override void OnLanguageChanged()
    {
        foreach (var format in OutputFormats)
        {
            format.RefreshLabel(Loc[format.LabelKey]);
        }

        OnPropertyChanged(nameof(OutputLocationHint));
        OnPropertyChanged(nameof(OutputPreview));
        OnPropertyChanged(nameof(SelectedFormatSummary));
    }

    partial void OnOutputBaseNameChanged(string value)
    {
        if (_settingName)
        {
            return;
        }

        _nameTouched = true;

        // 用户手动输入已知扩展名时，将文件名还原为 basename，并单选对应格式。
        string trimmed = value.Trim();
        var format = OutputFormats.FirstOrDefault(f =>
            trimmed.EndsWith(f.Extension, StringComparison.OrdinalIgnoreCase));
        if (format is not null)
        {
            foreach (var option in OutputFormats)
            {
                option.IsSelected = ReferenceEquals(option, format);
            }

            _settingName = true;
            OutputBaseName = trimmed[..^format.Extension.Length];
            _settingName = false;
        }

        OnPropertyChanged(nameof(OutputLocationHint));
        OnPropertyChanged(nameof(OutputPreview));
    }

    // ---------------------------------------------------------------- 默认文件名

    /// <summary>
    /// 默认文件名跟随文件列表：
    /// 单文件取该文件名（不含扩展名）；多文件取「第一个文件名 - 最后一个文件名」。
    /// 文件跨多个目录时留空——每个目录各生成一份、按各自目录内的文件命名，见
    /// <see cref="ResolveOutputPathsByDirectory"/>。
    /// 用户手动改过之后不再自动覆盖。
    /// </summary>
    private void SyncDefaultName()
    {
        if (_nameTouched)
        {
            return;
        }

        _settingName = true;
        OutputBaseName = BuildDefaultName();
        _settingName = false;
    }

    private string BuildDefaultName()
    {
        if (Files.Count == 0)
        {
            return string.Empty;
        }

        // 跨目录时没有「一个」名字可用：每个目录各生成一份，各自命名
        return GroupByDirectory().Count > 1 ? string.Empty : AutoBaseName(Files);
    }

    /// <summary>同一目录内一组文件的自动文件名：单文件用文件名，多文件用「第一个 - 最后一个」。</summary>
    private static string AutoBaseName(IReadOnlyList<SourceFileItem> files)
    {
        string first = Trim(Path.GetFileNameWithoutExtension(files[0].FileName));

        if (files.Count == 1)
        {
            return first;
        }

        string last = Trim(Path.GetFileNameWithoutExtension(files[^1].FileName));
        return Trim($"{first} - {last}");
    }

    /// <summary>按源文件所在目录分组（保持首次出现的顺序）。</summary>
    private Dictionary<string, List<SourceFileItem>> GroupByDirectory()
    {
        var groups = new Dictionary<string, List<SourceFileItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Files)
        {
            if (!groups.TryGetValue(file.Directory, out var list))
            {
                groups[file.Directory] = list = new List<SourceFileItem>();
            }

            list.Add(file);
        }

        return groups;
    }

    private static string Trim(string name)
        => name.Length <= MaxNameLength ? name : name[..MaxNameLength];

    // ---------------------------------------------------------------- 拖入

    public void AddFiles(IReadOnlyList<string> paths)
    {
        if (IsRunning)
        {
            SetStatus("StatusBusyGenerate");
            return;
        }

        // 目录展开为其下一层文件（文件夹选择器返回的是目录路径）
        paths = FilePaths.ExpandDirectories(paths);

        var known = new HashSet<string>(Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        int added = 0;
        int rejected = 0;

        foreach (string path in paths)
        {
            if (ChecksumFile.IsChecksumFile(path))
            {
                rejected++;
                continue;
            }

            if (!known.Add(path))
            {
                continue;
            }

            long size = 0;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch
            {
                // 忽略
            }

            Files.Add(new SourceFileItem { Path = path, Size = size });
            added++;
        }

        if (added > 0)
        {
            RefreshStatus();
        }

        if (rejected > 0)
        {
            ShowWarning("ToastIgnoredChecksums");
        }
    }

    [RelayCommand]
    private void RemoveFile(SourceFileItem? item)
    {
        if (item is null || IsRunning)
        {
            return;
        }

        Files.Remove(item);
        ShowToast("ToastRemovedFile", item.FileName);
        RefreshStatus();
    }

    /// <summary>列表变化后刷新状态栏：还有文件就报当前汇总，空列表则回到默认。</summary>
    private void RefreshStatus()
        => SetStatus(Files.Count == 0 ? "StatusReady" : "StatusFilesLoaded", FileSummary);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Files.Clear();
        Progress = 0;
        CurrentFile = string.Empty;
        DoneCount = TotalCount = 0;
        _nameTouched = false;
        SyncDefaultName();
        SetStatus("StatusReady");
    }

    // ---------------------------------------------------------------- 生成

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var fileList = Files.ToList();
        var formats = OutputFormats.Where(f => f.IsSelected).ToArray();
        if (fileList.Count == 0 || formats.Length == 0)
        {
            return;
        }

        if (formats.Any(f => ResolveOutputPaths(f).Count == 0))
        {
            SetStatus("StatusInvalidName");
            return;
        }

        // 目录 → 输出文件；跨目录时每个目录一份（按目录内的文件命名）
        var targetsPerFormat = new Dictionary<string, string>[formats.Length];
        for (int f = 0; f < formats.Length; f++)
        {
            targetsPerFormat[f] = ResolveOutputPathsByDirectory(formats[f]);
            if (targetsPerFormat[f].Count == 0)
            {
                SetStatus("StatusInvalidName");
                return;
            }
        }

        int workerCount = Math.Clamp(Settings.Parallelism, 1, AppSettings.MaxParallelism);
        long totalBytes = fileList.Sum(f => f.Size) * formats.Length;
        long completedBytes = 0;
        int totalTasks = fileList.Count * formats.Length;
        int doneTasks = 0;
        int writtenFiles = 0;
        int writtenRecords = 0;
        var writtenPaths = new List<string>();
        var stopwatch = Stopwatch.StartNew();

        // 扁平任务队列：**先按文件、再按格式**排序（同一个文件的各个算法挨在一起）。
        // 每个文件都是「所有已选格式算完才轮到下一个文件」：
        //   · 行进度从 0 连续走到 100%，不会出现「算完 CRC64 卡在 50% 等 MD5 阶段」的假暂停；
        //   · 并发额度仍然跨格式共享——1 个文件 + N 个格式 = N 个算法并行。
        var tasks = new List<(int Format, int File)>(totalTasks);
        for (int i = 0; i < fileList.Count; i++)
        {
            for (int f = 0; f < formats.Length; f++)
            {
                tasks.Add((f, i));
            }
        }

        var formatResults = new string?[formats.Length][];
        for (int f = 0; f < formats.Length; f++)
        {
            formatResults[f] = new string?[fileList.Count];
        }

        var formatCompleted = new int[formats.Length];
        var fileCompleted = new int[fileList.Count];
        var fileFailed = new bool[fileList.Count];
        var fileCancelled = new bool[fileList.Count];

        IsRunning = true;
        SetStatus("StatusGenerating");
        DoneCount = 0;
        TotalCount = totalTasks;
        Progress = 0;
        Interlocked.Exchange(ref _lastReportTicks, 0);

        foreach (var file in fileList)
        {
            file.BytesDone = 0;
            // 每行展示该文件在**所有已选格式**上的累计进度（所以总量要乘以格式数）。
            file.BytesTotal = file.Size * formats.Length;
            file.Progress = 0;
            file.State = GenerateItemState.Pending;
        }

        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;
        var inFlight = new long[workerCount];

        // 任务按「文件 → 格式」连续排布（下标 = 文件序号 × 格式数 + 格式序号），逐个 CAS 认领。
        // 调度目标：
        //   1) worker 优先把自己那个文件剩下的格式算完 —— 该行进度 0→100% 连续，
        //      不会出现「算完 CRC64 卡在 50% 等下一个格式阶段」的假暂停；
        //   2) 空出来的 worker 先去认领还没被别人占用的文件 —— 同时执行的文件数 ≈ 并发数；
        //   3) 文件都已被认领时，顺手帮别的文件算剩余格式 —— 只拖 1 个文件、勾多个算法也能并行。
        var taskClaimed = new int[totalTasks];
        int nextFile = 0;

        bool TryClaimTaskForFile(int fileIndex, out int taskIndex)
        {
            for (int f = 0; f < formats.Length; f++)
            {
                int index = fileIndex * formats.Length + f;
                if (Interlocked.CompareExchange(ref taskClaimed[index], 1, 0) == 0)
                {
                    taskIndex = index;
                    return true;
                }
            }

            taskIndex = -1;
            return false;
        }

        bool TryClaimAnyTask(out int taskIndex)
        {
            for (int i = 0; i < fileList.Count; i++)
            {
                if (TryClaimTaskForFile(i, out taskIndex))
                {
                    return true;
                }
            }

            taskIndex = -1;
            return false;
        }

        void ReportCurrent(bool force = false)
        {
            long done = Interlocked.Read(ref completedBytes);
            for (int i = 0; i < inFlight.Length; i++)
            {
                done += Interlocked.Read(ref inFlight[i]);
            }

            ReportProgress(done, totalBytes, force);
        }

        try
        {
            var workers = new Task[workerCount];
            for (int w = 0; w < workerCount; w++)
            {
                workers[w] = RunWorkerAsync(w);
            }

            await Task.WhenAll(workers);
        }
        finally
        {
            stopwatch.Stop();
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }

        if (ct.IsCancellationRequested)
        {
            // 收尾：把仍停在「计算中」的行标记为已取消（未轮到保持「等待中」）。
            foreach (var file in fileList)
            {
                if (file.State == GenerateItemState.Running)
                {
                    file.FinishProgress(GenerateItemState.Cancelled);
                }
            }

            SetStatus("StatusGenerateStopped");
            return;
        }

        Progress = 100;
        if (writtenFiles > 0)
        {
            ShowToast("ToastGenerateSuccess", writtenFiles);
        }

        SetStatus(
            writtenFiles switch
            {
                0 => "StatusGenerateNone",
                1 => "StatusGenerateOne",
                _ => "StatusGenerateMany",
            },
            writtenFiles switch
            {
                0 => null,
                1 => Path.GetFileName(writtenPaths[0]),
                _ => (object)writtenFiles,
            },
            writtenRecords,
            HashEngine.FormatDuration(stopwatch.Elapsed));

        // 单个 worker：认领「文件」而不是单个算法，把该文件的所有格式算完再换下一个。
        async Task RunWorkerAsync(int workerId)
        {
            int myFile = -1;

            while (!ct.IsCancellationRequested)
            {
                // 1) 自己那个文件还有格式没算 → 接着算（保持该行进度连续）
                if (myFile >= 0 && TryClaimTaskForFile(myFile, out int taskIndex))
                {
                    await RunTaskAsync(taskIndex, workerId);
                    continue;
                }

                myFile = -1;

                // 2) 自己那份算完了 → 认领一个还没被别人占用的文件
                int claim = Interlocked.Increment(ref nextFile) - 1;
                if (claim < fileList.Count)
                {
                    myFile = claim;
                    if (TryClaimTaskForFile(myFile, out taskIndex))
                    {
                        await RunTaskAsync(taskIndex, workerId);
                    }

                    continue;
                }

                // 3) 文件都已被认领 → 挑一个还有剩余格式的帮忙（1 个文件 + 多个算法时才有用）
                if (TryClaimAnyTask(out taskIndex))
                {
                    await RunTaskAsync(taskIndex, workerId);
                    continue;
                }

                break;
            }
        }

        // 执行一个「文件 × 格式」任务，算完就地结算「文件行状态」与「该格式写盘」。
        async Task RunTaskAsync(int taskIndex, int workerId)
        {
            var (f, i) = tasks[taskIndex];
            var format = formats[f];
            var file = fileList[i];

            CurrentFile = file.FileName;
            Interlocked.Exchange(ref inFlight[workerId], 0);
            file.CurrentFormat = format.Kind.DisplayName();
            file.State = GenerateItemState.Running;

            try
            {
                var outcome = await HashEngine.ComputeAsync(
                    file.Path,
                    format.Kind,
                    n =>
                    {
                        Interlocked.Add(ref inFlight[workerId], n);
                        file.ReportBytes(n);
                        ReportCurrent();
                    },
                    ct);

                Interlocked.Add(ref completedBytes, outcome.Bytes);
                Interlocked.Exchange(ref inFlight[workerId], 0);
                formatResults[f][i] = outcome.Hex;
                Interlocked.Increment(ref doneTasks);
                DoneCount = Volatile.Read(ref doneTasks);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref inFlight[workerId], 0);
                fileCancelled[i] = true;
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref inFlight[workerId], 0);
                fileFailed[i] = true;
                SetStatus("StatusComputeFailed", file.FileName, ex.Message);
            }

            // 该文件这个格式算完了：按实际字节数强制刷一次行进度。
            // 15Hz 节流会吞掉最后一次上报，否则进度会停在 47% 这种「看着像卡住」的值。
            file.SyncProgress();

            // 该文件在所有已选格式上都跑完 → 结算这一行的最终状态
            if (Interlocked.Increment(ref fileCompleted[i]) == formats.Length)
            {
                file.FinishProgress(fileFailed[i]
                    ? GenerateItemState.Failed
                    : fileCancelled[i]
                        ? GenerateItemState.Cancelled
                        : GenerateItemState.Done);
            }

            // 该格式在所有文件上都跑完 → 写盘（保证「停止时已完成的格式仍会写出」）
            if (Interlocked.Increment(ref formatCompleted[f]) == fileList.Count)
            {
                WriteFormatOutput(f);
            }

            ReportCurrent(force: true);
        }

        // 把某个格式收集到的结果按输出文件分组写盘（同一目录的记录写进该目录的那一份）
        void WriteFormatOutput(int f)
        {
            var format = formats[f];
            var byDirectory = targetsPerFormat[f];
            var results = formatResults[f];

            var groups = new Dictionary<string, List<(string Name, string Hex)>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < fileList.Count; i++)
            {
                if (results[i] is null
                    || !byDirectory.TryGetValue(fileList[i].Directory, out string? outputPath))
                {
                    continue;
                }

                if (!groups.TryGetValue(outputPath, out var entries))
                {
                    groups[outputPath] = entries = new List<(string, string)>();
                }

                entries.Add((fileList[i].FileName, results[i]!));
            }

            NumberFormat numberFormat = format.Kind is HashKind.Crc64 or HashKind.Crc32
                ? NumberFormat.Decimal
                : NumberFormat.Hex;

            foreach (var (outputPath, entries) in groups)
            {
                if (entries.Count == 0)
                {
                    continue;
                }

                try
                {
                    WriteChecksumFile(outputPath, entries, format.Kind, numberFormat);
                    writtenFiles++;
                    writtenRecords += entries.Count;
                    writtenPaths.Add(outputPath);
                }
                catch (Exception ex)
                {
                    SetStatus("StatusComputeFailed", Path.GetFileName(outputPath), ex.Message);
                }
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _cts?.Cancel();
        SetStatus("StatusStopRequested");
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>把文件名清洗成合法的文件名。</summary>
    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        string result = sb.ToString().Trim();
        return result.Length == 0 ? "checksums" : result;
    }

    private static bool IsExplicitPath(string value)
    {
        string trimmed = value.Trim();
        return Path.IsPathRooted(trimmed)
            || trimmed.Contains('/')
            || trimmed.Contains('\\');
    }

    /// <summary>计算所有已选格式的目标路径（并保持选项顺序）。</summary>
    public IReadOnlyList<string> ResolveOutputPaths()
        => OutputFormats.Where(f => f.IsSelected)
            .SelectMany(f => ResolveOutputPaths(f))
            .ToArray();

    /// <summary>计算指定格式的最终输出路径集合。</summary>
    public IReadOnlyList<string> ResolveOutputPaths(GenerateFormatOption format)
        => ResolveOutputPathsByDirectory(format)
            .Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// 计算指定格式的输出路径，按「源文件所在目录」分组：目录 → 输出文件全路径。
    /// · 留空（自动）：每个目录各一份，按该目录内的文件命名（单文件用文件名，多文件用「第一个 - 最后一个」）；
    /// · 手填文件名：每个目录各一份、同名；
    /// · 手填绝对路径：所有文件汇总输出到那一个文件。
    /// </summary>
    public Dictionary<string, string> ResolveOutputPathsByDirectory(GenerateFormatOption format)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Files.Count == 0)
        {
            return result;
        }

        string ext = format.Extension;
        string raw = OutputBaseName.Trim();

        if (raw.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[..^ext.Length];
        }

        raw = raw.Trim().TrimEnd('.', ' ');

        var groups = GroupByDirectory();

        if (IsExplicitPath(raw))
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
            string name = SanitizeName(raw) + ext;
            foreach (string directory in groups.Keys)
            {
                result[directory] = Path.Combine(directory, name);
            }

            return result;
        }

        foreach (var (directory, files) in groups)
        {
            result[directory] = Path.Combine(directory, SanitizeName(AutoBaseName(files)) + ext);
        }

        return result;
    }

    private static void WriteChecksumFile(string path, List<(string Name, string Hex)> entries, HashKind kind, NumberFormat format)
    {
        // .sfv 是 HashCheck 的 CRC-32 容器：必须写成「文件名居左、小写 CRC32」的 SFV 原生格式，
        // 否则 HashCheck 无法识别（它按扩展名 .sfv 只认反序格式）。其余格式保持 <hash> *<name>。
        string content = ChecksumFile.IsSfvTarget(path, kind)
            ? ChecksumFile.FormatSfv(entries)
            : string.Join("\n", entries.Select(e => ChecksumFile.FormatLine(e.Hex, e.Name, format))) + "\n";

        File.WriteAllText(path, content, Utf8NoBom);
    }

    private void ReportProgress(long done, long total, bool force = false)
    {
        if (!force)
        {
            long now = Stopwatch.GetTimestamp();
            long last = Interlocked.Read(ref _lastReportTicks);
            if (now - last < Stopwatch.Frequency / 20)
            {
                return;
            }

            Interlocked.Exchange(ref _lastReportTicks, now);
        }

        double percent = total <= 0 ? 0 : Math.Clamp(done * 100.0 / total, 0, 100);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Progress = percent);
    }
}

/// <summary>生成面板上的一个算法/扩展名复选项。</summary>
public sealed partial class GenerateFormatOption : ObservableObject
{
    public GenerateFormatOption(HashKind kind, string extension, string labelKey, bool isSelected = false)
    {
        Kind = kind;
        Extension = extension;
        LabelKey = labelKey;
        _isSelected = isSelected;
    }

    public HashKind Kind { get; }

    public string Extension { get; }

    public string LabelKey { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _label = string.Empty;

    public void RefreshLabel(string value) => Label = value;
}
