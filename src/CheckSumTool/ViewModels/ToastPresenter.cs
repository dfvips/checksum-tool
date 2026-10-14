using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckSumTool.ViewModels;

/// <summary>
/// 轻提示的显示状态：文案 + 显隐 + 是否警告。
/// </summary>
/// <remarks>
/// 主窗口与设置窗口各持有一份，互不串台——
/// 设置窗口打开期间触发的提示只会出现在设置窗口上，不会落到被遮挡的主窗口里。
/// </remarks>
public sealed partial class ToastPresenter : ObservableObject
{
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isVisible;

    /// <summary>是否为警告类提示（决定图标颜色）。</summary>
    [ObservableProperty]
    private bool _isWarning;

    /// <summary>弹出一条轻提示，2.6 秒后自动消失；连续调用会重置计时。</summary>
    public async void Show(string message, bool isWarning = false)
    {
        Message = message;
        IsWarning = isWarning;
        IsVisible = true;

        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.6), cts.Token);

            if (!cts.IsCancellationRequested)
            {
                IsVisible = false;
            }
        }
        catch (OperationCanceledException)
        {
            // 被新的提示取代，交给新提示负责收起
        }
    }
}
