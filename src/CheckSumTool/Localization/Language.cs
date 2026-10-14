using System.Globalization;

namespace CheckSumTool.Localization;

/// <summary>界面语言。新增语种时直接把新语种追加到末尾，索引值即为语言数组里的下标。</summary>
public enum Language
{
    SimplifiedChinese = 0,
    TraditionalChinese = 1,
    English = 2,
    Japanese = 3,
    Korean = 4,
    French = 5,
    German = 6,
    Spanish = 7,
    Russian = 8,
    Portuguese = 9,
    Italian = 10,
}

public static class LanguageExtensions
{
    /// <summary>语言自身的显示名（始终用该语言书写，不随当前语言变化）。</summary>
    public static string NativeName(this Language language) => language switch
    {
        Language.TraditionalChinese => "繁體中文",
        Language.English => "English",
        Language.Japanese => "日本語",
        Language.Korean => "한국어",
        Language.French => "Français",
        Language.German => "Deutsch",
        Language.Spanish => "Español",
        Language.Russian => "Русский",
        Language.Portuguese => "Português",
        Language.Italian => "Italiano",
        _ => "简体中文",
    };

    /// <summary>对应的 IETF 语言标签（用于 CultureInfo 匹配）。</summary>
    public static string CultureTag(this Language language) => language switch
    {
        Language.SimplifiedChinese => "zh-Hans",
        Language.TraditionalChinese => "zh-Hant",
        Language.English => "en",
        Language.Japanese => "ja",
        Language.Korean => "ko",
        Language.French => "fr",
        Language.German => "de",
        Language.Spanish => "es",
        Language.Russian => "ru",
        Language.Portuguese => "pt",
        Language.Italian => "it",
        _ => "zh-Hans",
    };

    /// <summary>所有具体语言（不含「跟随系统」之类的特殊状态）。</summary>
    public static IReadOnlyList<Language> AllConcrete { get; } =
    [
        Language.SimplifiedChinese,
        Language.TraditionalChinese,
        Language.English,
        Language.Japanese,
        Language.Korean,
        Language.French,
        Language.German,
        Language.Spanish,
        Language.Russian,
        Language.Portuguese,
        Language.Italian,
    ];

    /// <summary>根据系统 CultureInfo 推测最匹配的具体语言。匹配失败时回退到英语。</summary>
    public static Language DetectFromCulture(CultureInfo culture)
    {
        string name = culture.Name.ToLowerInvariant();

        // 简体/繁体按完整 BCP-47 标签判定（如 zh-Hans / zh-Hant / zh-CN / zh-TW）
        if (name.StartsWith("zh-hans", StringComparison.Ordinal) || name is "zh-cn" or "zh-sg")
        {
            return Language.SimplifiedChinese;
        }

        if (name.StartsWith("zh-hant", StringComparison.Ordinal) || name is "zh-tw" or "zh-hk" or "zh-mo")
        {
            return Language.TraditionalChinese;
        }

        if (name.StartsWith("zh", StringComparison.Ordinal))
        {
            // 其它 zh-*（如 zh-CHS）默认按简体处理
            return Language.SimplifiedChinese;
        }

        // 其它语言按两字母 ISO 代码匹配
        string two = name.Length >= 2 ? name[..2] : name;
        return two switch
        {
            "ja" => Language.Japanese,
            "ko" => Language.Korean,
            "fr" => Language.French,
            "de" => Language.German,
            "es" => Language.Spanish,
            "ru" => Language.Russian,
            "pt" => Language.Portuguese,
            "it" => Language.Italian,
            "en" => Language.English,
            _ => Language.English,
        };
    }
}