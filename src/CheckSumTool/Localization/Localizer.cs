using System.ComponentModel;
using System.Globalization;

namespace CheckSumTool.Localization;

/// <summary>
/// 全局文案访问器。界面用 <c>{Binding Loc[Key]}</c> 绑定，切换语言时触发
/// <c>Item[]</c> 通知，所有索引器绑定会自动刷新。
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public static Localizer Instance { get; } = new();

    private Language _language = Language.SimplifiedChinese;

    private Localizer()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Language Language
    {
        get => _language;
        set
        {
            if (_language == value)
            {
                return;
            }

            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));

            // 通知所有索引器绑定重新求值
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        }
    }

    /// <summary>按当前语言取文案。</summary>
    public string this[string key] => Strings.Get(_language, key);

    /// <summary>按当前语言取文案并填充占位符。</summary>
    public string Format(string key, params object?[] args)
    {
        string template = Strings.Get(_language, key);
        return args.Length == 0
            ? template
            : string.Format(CultureInfo.CurrentCulture, template, args);
    }
}
