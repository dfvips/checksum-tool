using Avalonia.Controls;

namespace CheckSumTool.Controls;

/// <summary>
/// 轻提示宿主：底部居中的浮层，DataContext 应为 <see cref="ViewModels.ToastPresenter"/>。
/// </summary>
/// <remarks>
/// 主窗口与设置窗口各放一个，各自绑定自己那一份提示状态，避免提示出现在被遮挡的窗口上。
/// </remarks>
public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
    }
}
