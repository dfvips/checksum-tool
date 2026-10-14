using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CheckSumTool.Core;
using CheckSumTool.Helpers;
using CheckSumTool.Services;

namespace CheckSumTool.ViewModels;

/// <summary>「校验」面板：拖入或点选 .crc64 / .md5，逐条比对同目录下的关联文件。</summary>
public sealed partial class VerifyViewModel : PanelViewModelBase
{
    private CancellationTokenSource? _cts;
    private long _lastReportTicks;

    public VerifyViewModel(AppSettings settings, Action<string, bool> showToast)
        : base(settings, showToast)
    {
        Sources.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSources));
            OnPropertyChanged(nameof(SourceSummary));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(DropHint));
            StartCommand.NotifyCanExecuteChanged();
        };

        Results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(ResultSummary));
        };
    }

    public ObservableCollection<ChecksumSourceItem> Sources { get; } = new();

    public ObservableCollection<VerifyResultItem> Results { get; } = new();

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
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private int _passCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private int _failCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private int _missingCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private int _errorCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private int _totalCount;

    public string ProgressPercentText => $"{Progress:0}%";

    /// <summary>只要加载了校验文件，结果列表就会列出全部记录（未校验的显示为「等待中」）。</summary>
    public bool HasResults => Results.Count > 0;

    public bool HasSources => Sources.Count > 0;

    public bool IsEmpty => Sources.Count == 0;

    public string SourceSummary => Loc.Format("LoadedSummary", Sources.Count, Sources.Sum(s => s.EntryCount));

    public string DropHint => Loc[IsRunning ? "VerifyDropHintBusy" : "VerifyDropHint"];

    public string ResultSummary => Loc.Format("ResultSummary", PassCount, FailCount, MissingCount, TotalCount);

    private bool CanStart => !IsRunning && Sources.Count > 0;

    private bool CanStop => IsRunning;

    private bool CanClear => !IsRunning;

    protected override void OnLanguageChanged()
    {
        foreach (var row in Results)
        {
            row.RefreshLocalizedText();
        }
    }

    // ---------------------------------------------------------------- 拖入

    /// <summary>追加文件；运行中会被拒绝。</summary>
    public void AddFiles(IReadOnlyList<string> paths)
    {
        if (IsRunning)
        {
            SetStatus("StatusBusyVerify");
            return;
        }

        // 目录展开为其下一层文件（文件夹选择器返回的是目录路径）
        paths = FilePaths.ExpandDirectories(paths);

        var known = new HashSet<string>(Sources.Select(s => s.Path), StringComparer.OrdinalIgnoreCase);
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
            Sources.Add(new ChecksumSourceItem
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
            RebuildRows();
            RefreshStatus();
        }
        else if (rejected > 0)
        {
            SetStatus("StatusOnlyChecksums");
        }
    }

    [RelayCommand]
    private void RemoveSource(ChecksumSourceItem? item)
    {
        if (item is null || IsRunning)
        {
            return;
        }

        Sources.Remove(item);
        RebuildRows();
        ShowToast("ToastRemovedFile", item.FileName);
        RefreshStatus();
    }

    /// <summary>列表变化后刷新状态栏：还有校验文件就报当前汇总，空列表则回到默认。</summary>
    private void RefreshStatus()
        => SetStatus(Sources.Count == 0 ? "StatusReady" : "StatusFilesLoaded", SourceSummary);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Sources.Clear();
        RebuildRows();
        SetStatus("StatusReady");
    }

    /// <summary>
    /// 依据当前已加载的校验文件重建结果列表：每条记录对应一行，
    /// 初始状态为「等待中」，校验过程中逐行更新。
    /// </summary>
    private List<VerifyTask> RebuildRows()
    {
        Results.Clear();

        var tasks = new List<VerifyTask>();
        foreach (var source in Sources)
        {
            foreach (var entry in source.Entries)
            {
                var row = new VerifyResultItem
                {
                    FileName = entry.FileName,
                    SourceName = source.FileName,
                    Expected = entry.HashHex,
                };

                Results.Add(row);
                tasks.Add(new VerifyTask(source, entry, row));
            }
        }

        PassCount = FailCount = MissingCount = ErrorCount = 0;
        TotalCount = Results.Count;
        Progress = 0;
        CurrentFile = string.Empty;

        return tasks;
    }

    private readonly record struct VerifyTask(
        ChecksumSourceItem Source,
        ChecksumEntry Entry,
        VerifyResultItem Row);

    // ---------------------------------------------------------------- 校验

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var tasks = RebuildRows();

        if (tasks.Count == 0)
        {
            SetStatus("StatusNoRecords");
            return;
        }

        // 预解析目标路径、记录每个文件大小并统计总字节数（缺失的文件不计入总进度）。
        var targets = new string[tasks.Count];
        var sizes = new long[tasks.Count];
        long totalBytes = 0;
        for (int i = 0; i < tasks.Count; i++)
        {
            targets[i] = HashEngine.ResolveTargetPath(tasks[i].Source.Path, tasks[i].Entry.FileName);
            try
            {
                if (File.Exists(targets[i]))
                {
                    sizes[i] = new FileInfo(targets[i]).Length;
                    totalBytes += sizes[i];
                }
            }
            catch
            {
                // 忽略无法读取元数据的文件
            }
        }

        int workerCount = Math.Clamp(Settings.Parallelism, 1, AppSettings.MaxParallelism);
        IsRunning = true;
        SetStatus("StatusVerifying");
        Interlocked.Exchange(ref _lastReportTicks, 0);

        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        int next = 0;
        long completedBytes = 0;
        var inFlight = new long[workerCount];
        var stopwatch = Stopwatch.StartNew();

        void ReportCurrent(bool force = false)
        {
            long done = Interlocked.Read(ref completedBytes);
            for (int i = 0; i < inFlight.Length; i++)
            {
                done += Interlocked.Read(ref inFlight[i]);
            }

            ReportProgress(done, totalBytes, force);
        }

        // worker 是运行在 UI 线程上的协程，await 计算时让出线程，因此更新行状态无需加锁。
        var workers = new Task[workerCount];
        for (int w = 0; w < workerCount; w++)
        {
            workers[w] = RunWorkerAsync(w);
        }

        try
        {
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
            foreach (var task in tasks)
            {
                if (task.Row.Status is VerifyStatus.Pending or VerifyStatus.Running)
                {
                    task.Row.IsVerifying = false;
                    task.Row.Status = VerifyStatus.Cancelled;
                }
            }

            SetStatus("StatusVerifyStopped");
        }
        else
        {
            Progress = 100;
            // 统计摘要统一显示在状态栏右侧，这里只报耗时
            SetStatus("StatusVerifyFinished", HashEngine.FormatDuration(stopwatch.Elapsed));

            if (FailCount == 0 && MissingCount == 0 && ErrorCount == 0)
            {
                ShowToast("ToastVerifySuccess", TotalCount);
            }
            else
            {
                ShowWarning("ToastVerifyIssues", PassCount, FailCount, MissingCount);
            }
        }

        async Task RunWorkerAsync(int workerId)
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                int index = Interlocked.Increment(ref next) - 1;
                if (index >= tasks.Count)
                {
                    break;
                }

                var (source, entry, row) = tasks[index];
                string target = targets[index];

                if (!File.Exists(target))
                {
                    row.Status = VerifyStatus.Missing;
                    MissingCount++;
                    CurrentFile = entry.FileName;
                    SetStatus("StatusMissingTarget", target);
                    continue;
                }

                row.Status = VerifyStatus.Running;
                CurrentFile = entry.FileName;
                inFlight[workerId] = 0;

                // 本行的单文件进度
                row.BytesDone = 0;
                row.BytesTotal = sizes[index];
                row.Progress = 0;
                row.IsVerifying = true;

                try
                {
                    var outcome = await HashEngine.ComputeAsync(
                        target,
                        source.Kind,
                        n =>
                        {
                            Interlocked.Add(ref inFlight[workerId], n);
                            row.ReportBytes(n);
                            ReportCurrent();
                        },
                        ct);

                    Interlocked.Add(ref completedBytes, outcome.Bytes);
                    inFlight[workerId] = 0;

                    row.FinishProgress();
                    row.Actual = outcome.Hex;
                    row.Detail = $"{source.Kind.DisplayName()} · {HashEngine.FormatSize(outcome.Bytes)} · {HashEngine.FormatDuration(outcome.Elapsed)}";

                    if (string.Equals(outcome.Hex, entry.HashHex, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Status = VerifyStatus.Ok;
                        PassCount++;
                    }
                    else
                    {
                        row.Status = VerifyStatus.Mismatch;
                        FailCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    inFlight[workerId] = 0;
                    row.FinishProgress();
                    row.Status = VerifyStatus.Cancelled;
                    break;
                }
                catch (Exception ex)
                {
                    inFlight[workerId] = 0;
                    row.FinishProgress();
                    row.Status = VerifyStatus.Error;
                    row.Detail = ex.Message;
                    ErrorCount++;
                }

                ReportCurrent(force: true);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _cts?.Cancel();
        SetStatus("StatusStopRequested");
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
