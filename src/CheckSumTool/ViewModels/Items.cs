using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CheckSumTool.Core;
using CheckSumTool.Localization;

namespace CheckSumTool.ViewModels;

/// <summary>校验结果状态。</summary>
public enum VerifyStatus
{
    Pending,
    Running,
    Ok,
    Mismatch,
    Missing,
    Error,
    Cancelled,
}

/// <summary>一个已拖入的校验文件（.crc64 / .md5）。</summary>
public sealed class ChecksumSourceItem
{
    public required string Path { get; init; }
    public required HashKind Kind { get; init; }
    public required int EntryCount { get; init; }
    public required int WarningCount { get; init; }
    public required List<ChecksumEntry> Entries { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public string Summary => WarningCount > 0
        ? Localizer.Instance.Format("KindRecordsWarn", Kind.DisplayName(), EntryCount, WarningCount)
        : Localizer.Instance.Format("KindRecords", Kind.DisplayName(), EntryCount);

    public string Tooltip => Path;
}

/// <summary>生成过程中的单文件状态。</summary>
public enum GenerateItemState
{
    /// <summary>还没轮到。</summary>
    Pending,

    /// <summary>正在计算，状态列显示进度条。</summary>
    Running,

    /// <summary>已完成。</summary>
    Done,

    /// <summary>读取或计算失败。</summary>
    Failed,

    /// <summary>被停止中断。</summary>
    Cancelled,
}

/// <summary>一个待生成校验值的普通文件（生成过程中逐行更新进度）。</summary>
public sealed partial class SourceFileItem : ObservableObject
{
    public required string Path { get; init; }
    public long Size { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>0 字节（例如空文件或读取不到元数据）时不显示大小。</summary>
    public string SizeText => Size > 0 ? HashEngine.FormatSize(Size) : string.Empty;

    public string Tooltip => Path;

    /// <summary>第二行摘要：体积 · 目录（缺省项自动省略）。</summary>
    public string Detail
    {
        get
        {
            string size = SizeText;
            bool hasDir = !string.IsNullOrEmpty(Directory);

            return (size.Length, hasDir) switch
            {
                (0, true) => Directory,
                (> 0, false) => size,
                (> 0, true) => $"{size} · {Directory}",
                _ => string.Empty,
            };
        }
    }

    /// <summary>已读取字节数（只在计算线程写入）。</summary>
    public long BytesDone;

    /// <summary>文件总字节数。</summary>
    public long BytesTotal;

    private long _lastPushTicks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _progress;

    /// <summary>当前正在计算的算法名（如 <c>SHA3-256</c>）；仅运行中显示在进度条左侧。</summary>
    [ObservableProperty]
    private string _currentFormat = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVerifying))]
    [NotifyPropertyChangedFor(nameof(ShowStatusText))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private GenerateItemState _state = GenerateItemState.Pending;

    public string ProgressText => $"{Progress}%";

    /// <summary>本行是否正在计算：为真时状态列显示进度条而不是文字。</summary>
    public bool IsVerifying => State == GenerateItemState.Running;

    /// <summary>是否显示状态文字（计算中改由进度条表达）。</summary>
    public bool ShowStatusText => State != GenerateItemState.Running;

    /// <summary>状态列文字：随状态变化，不再永远停在「等待中」。</summary>
    public string StatusText => Localizer.Instance[State switch
    {
        GenerateItemState.Done => "StatusDone",
        GenerateItemState.Failed => "StatusFailed",
        GenerateItemState.Cancelled => "StatusCancelled",
        _ => "StatusPending",
    }];

    public IBrush StatusBrush => State switch
    {
        GenerateItemState.Done => Palette.Ok,
        GenerateItemState.Failed => Palette.Fail,
        _ => Palette.Muted,
    };

    /// <summary>由计算线程调用：累加已读字节，并按 15Hz 节流把进度切回界面线程。</summary>
    public void ReportBytes(long bytes)
    {
        long done = System.Threading.Interlocked.Add(ref BytesDone, bytes);

        long now = Stopwatch.GetTimestamp();
        long last = Volatile.Read(ref _lastPushTicks);
        if (now - last < Stopwatch.Frequency / 15)
        {
            return;
        }

        Volatile.Write(ref _lastPushTicks, now);

        double percent = BytesTotal > 0 ? done * 100.0 / BytesTotal : 0;
        int value = (int)Math.Clamp(percent, 0, 100);

        Dispatcher.UIThread.Post(() => Progress = value);
    }

    /// <summary>在界面线程上收尾：把进度补到 100% 并写入最终状态。</summary>
    public void FinishProgress(GenerateItemState finalState)
    {
        Progress = BytesTotal > 0 ? 100 : 0;
        State = finalState;
    }

    /// <summary>
    /// 按当前已读字节强制刷新一次进度。<see cref="ReportBytes"/> 的 15Hz 节流会吞掉最后一次上报，
    /// 一个格式算完时若不刷新，行进度会停在 47% 这种「看着像卡住」的值上。
    /// </summary>
    public void SyncProgress()
    {
        long done = System.Threading.Interlocked.Read(ref BytesDone);
        double percent = BytesTotal > 0 ? done * 100.0 / BytesTotal : 0;
        int value = (int)Math.Clamp(percent, 0, 100);

        Dispatcher.UIThread.Post(() => Progress = value);
    }
}

/// <summary>校验结果中的一行（状态可变，需通知界面刷新）。</summary>
public sealed partial class VerifyResultItem : ObservableObject
{
    public required string FileName { get; init; }
    public required string SourceName { get; init; }

    /// <summary>已读取字节数（只在计算线程写入，界面线程读取时刷新为可绑定属性）。</summary>
    public long BytesDone;

    /// <summary>该文件的总字节数，用于计算百分比。</summary>
    public long BytesTotal;

    private long _lastPushTicks;

    [ObservableProperty]
    private string _expected = "—";

    [ObservableProperty]
    private string _actual = "—";

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(ShowStatusText))]
    private VerifyStatus _status = VerifyStatus.Pending;

    /// <summary>本行是否正在计算哈希：为真时状态列显示单文件进度条而不是文字。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusText))]
    private bool _isVerifying;

    /// <summary>单文件进度（0~100）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _progress;

    /// <summary>进度百分比文案，例如 <c>42%</c>。</summary>
    public string ProgressText => $"{Progress}%";

    /// <summary>是否显示状态文字（校验中改由进度条表达）。</summary>
    public bool ShowStatusText => !IsVerifying;

    public string StatusText
    {
        get
        {
            // 校验中：位置让给进度条，文字留空
            if (IsVerifying)
            {
                return string.Empty;
            }

            return Localizer.Instance[Status switch
            {
                VerifyStatus.Ok => "StatusOk",
                VerifyStatus.Mismatch => "StatusMismatch",
                VerifyStatus.Missing => "StatusMissing",
                VerifyStatus.Error => "StatusError",
                VerifyStatus.Cancelled => "StatusCancelled",
                VerifyStatus.Running => "StatusRunning",
                _ => "StatusPending",
            }];
        }
    }

    /// <summary>由计算线程调用：累加已读字节，并按 15Hz 节流把进度切回界面线程。</summary>
    public void ReportBytes(long bytes)
    {
        long done = Interlocked.Add(ref BytesDone, bytes);

        long now = Stopwatch.GetTimestamp();
        long last = Volatile.Read(ref _lastPushTicks);
        if (now - last < Stopwatch.Frequency / 15)
        {
            return;
        }

        Volatile.Write(ref _lastPushTicks, now);

        double percent = BytesTotal > 0 ? done * 100.0 / BytesTotal : 0;
        int value = (int)Math.Clamp(percent, 0, 100);

        Dispatcher.UIThread.Post(() => Progress = value);
    }

    /// <summary>在界面线程上收尾：把进度补到 100% 并关闭进度条。</summary>
    public void FinishProgress()
    {
        Progress = BytesTotal > 0 ? 100 : 0;
        IsVerifying = false;
    }

    public string StatusGlyph => Status switch
    {
        VerifyStatus.Ok => "✓",
        VerifyStatus.Mismatch => "✕",
        VerifyStatus.Missing => "?",
        VerifyStatus.Error => "!",
        VerifyStatus.Cancelled => "–",
        VerifyStatus.Running => "…",
        _ => "·",
    };

    public IBrush StatusBrush => Status switch
    {
        VerifyStatus.Ok => Palette.Ok,
        VerifyStatus.Mismatch => Palette.Fail,
        VerifyStatus.Missing => Palette.Warn,
        VerifyStatus.Error => Palette.Fail,
        _ => Palette.Muted,
    };

    public string Tooltip =>
        $"{SourceName} → {FileName}\n" +
        $"{Localizer.Instance["ColumnExpected"]}: {Expected}\n" +
        $"{Localizer.Instance["ColumnActual"]}: {Actual}";

    /// <summary>语言切换后由面板调用，刷新依赖文案的属性。</summary>
    public void RefreshLocalizedText() => OnPropertyChanged(nameof(StatusText));
}

/// <summary>固定语义色（浅色 / 深色下均可读）。</summary>
public static class Palette
{
    public static readonly IBrush Ok = Brush.Parse("#67C23B");
    public static readonly IBrush Fail = Brush.Parse("#EF4444");
    public static readonly IBrush Warn = Brush.Parse("#F59E0B");
    public static readonly IBrush Muted = Brush.Parse("#8A94A6");
}
