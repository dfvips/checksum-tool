using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CheckSumTool.Localization;
using CheckSumTool.Services;

namespace CheckSumTool.ViewModels;

/// <summary>
/// 各功能面板视图模型的公共基类：持有共享设置与文案，
/// 并在语言/设置变化时统一刷新计算属性。
/// </summary>
/// <remarks>
/// **状态栏消息归面板自己所有**（而不是汇总到主窗口）：
/// 这样切换标签时状态栏自动显示当前面板的消息，
/// 运行中也不会出现三个面板互相覆盖的混合消息。
/// </remarks>
public abstract partial class PanelViewModelBase : ObservableObject
{
    private readonly Action<string, bool> _showToast;
    private string _statusKey = "StatusReady";
    private object?[] _statusArgs = [];

    protected PanelViewModelBase(AppSettings settings, Action<string, bool> showToast)
    {
        Settings = settings;
        _showToast = showToast;

        Loc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or "Item[]" or "Item" or "Language")
            {
                OnPropertyChanged(string.Empty);
                OnLanguageChanged();

                // 切换语言后按新语言重新拼一次状态消息
                StatusMessage = Loc.Format(_statusKey, _statusArgs);
            }
        };

        Settings.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);

        // 初始化状态栏消息：直接从默认 key/args 计算一次，
        // 避免「界面刚打开时 _statusMessage 还是空串、状态栏只显示指示点」的空白。
        // 之后若 Loc.Language 变化，Loc.PropertyChanged 处理器会按新语言重拼。
        _statusMessage = Localizer.Instance.Format(_statusKey, _statusArgs);
    }

    /// <summary>共享的应用设置。</summary>
    public AppSettings Settings { get; }

    /// <summary>文案访问器，供 XAML 以 <c>{Binding Loc[Key]}</c> 使用。</summary>
    public Localizer Loc { get; } = Localizer.Instance;

    /// <summary>当前状态消息的文案 key（便于测试与调试；不随语言变化）。</summary>
    public string StatusKey => _statusKey;

    /// <summary>当前状态消息（已本地化）。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>语言切换后刷新无法通过计算属性重算的内容（例如列表项）。</summary>
    protected virtual void OnLanguageChanged()
    {
    }

    /// <summary>设置本面板的状态栏消息。切换语言后会自动重新拼装。</summary>
    protected void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusMessage = Loc.Format(key, args);
    }

    /// <summary>弹出一条成功轻提示。</summary>
    protected void ShowToast(string key, params object?[] args)
        => _showToast(Loc.Format(key, args), false);

    /// <summary>弹出一条警告轻提示（红色图标）。</summary>
    protected void ShowWarning(string key, params object?[] args)
        => _showToast(Loc.Format(key, args), true);

    /// <summary>供视图层回报错误。</summary>
    public void SetError(string key, params object?[] args) => SetStatus(key, args);
}
