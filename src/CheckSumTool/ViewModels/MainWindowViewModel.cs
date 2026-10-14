using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CheckSumTool.Core;
using CheckSumTool.Localization;
using CheckSumTool.Services;

namespace CheckSumTool.ViewModels;

/// <summary>主视图模型：面板切换、主题切换、设置项与全局状态。</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public MainWindowViewModel()
    {
        _settings = SettingsService.Load();

        // 面板自己保管状态消息，主窗口只负责把「当前面板」的那份转发到状态栏
        Verify = new VerifyViewModel(_settings, ShowToast);
        Generate = new GenerateViewModel(_settings, ShowToast);
        Merge = new MergeViewModel(_settings, ShowToast);

        // 语言与主题来自设置
        ApplyInitialLanguage();
        RefreshLocalizedOptions();

        Loc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or "Item[]" or "Item" or "Language")
            {
                RefreshLocalizedOptions();
                OnPropertyChanged(string.Empty);
            }
        };

        _settings.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);

        foreach (var panel in new PanelViewModelBase[] { Verify, Generate, Merge })
        {
            panel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PanelViewModelBase.StatusMessage)
                    && ReferenceEquals(SelectedPanel, panel))
                {
                    OnPropertyChanged(nameof(StatusMessage));
                }
            };
        }

        // 校验面板的记录有无变化时，同步状态栏统计的可见性
        Verify.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VerifyViewModel.HasResults))
            {
                OnPropertyChanged(nameof(ShowVerifySummary));
            }
        };
    }

    public VerifyViewModel Verify { get; }

    public GenerateViewModel Generate { get; }

    public MergeViewModel Merge { get; }

    public Localizer Loc { get; } = Localizer.Instance;

    public AppSettings Settings => _settings;

    // ---------------------------------------------------------------- 面板切换

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVerifyTab))]
    [NotifyPropertyChangedFor(nameof(IsGenerateTab))]
    [NotifyPropertyChangedFor(nameof(IsMergeTab))]
    [NotifyPropertyChangedFor(nameof(ShowVerifySummary))]
    [NotifyPropertyChangedFor(nameof(StatusMessage))]
    private int _selectedTabIndex;

    /// <summary>状态栏是否显示校验统计（仅校验面板且有记录时）。</summary>
    public bool ShowVerifySummary => IsVerifyTab && Verify.HasResults;

    // ---------------------------------------------------------------- 状态栏

    /// <summary>当前选中的面板（决定状态栏显示谁的消息）。</summary>
    private PanelViewModelBase? SelectedPanel => SelectedTabIndex switch
    {
        0 => Verify,
        1 => Generate,
        2 => Merge,
        _ => null,
    };

    /// <summary>
    /// 状态栏消息：始终取自当前面板自身的那一份。
    /// 切换面板时即时切换，不会残留上一个面板的消息。
    /// </summary>
    public string StatusMessage => SelectedPanel?.StatusMessage ?? string.Empty;

    public string VersionText => "v" + UpdateService.CurrentVersion;

    public bool IsVerifyTab
    {
        get => SelectedTabIndex == 0;
        set
        {
            if (value)
            {
                SelectedTabIndex = 0;
            }
        }
    }

    public bool IsGenerateTab
    {
        get => SelectedTabIndex == 1;
        set
        {
            if (value)
            {
                SelectedTabIndex = 1;
            }
        }
    }

    public bool IsMergeTab
    {
        get => SelectedTabIndex == 2;
        set
        {
            if (value)
            {
                SelectedTabIndex = 2;
            }
        }
    }

    // ---------------------------------------------------------------- 主题

    public ThemeMode ThemeMode
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value)
            {
                return;
            }

            _settings.Theme = value;
            ApplyTheme(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
        }
    }

    public bool IsSystemTheme
    {
        get => ThemeMode == ThemeMode.System;
        set
        {
            if (value)
            {
                ThemeMode = ThemeMode.System;
            }
        }
    }

    public bool IsLightTheme
    {
        get => ThemeMode == ThemeMode.Light;
        set
        {
            if (value)
            {
                ThemeMode = ThemeMode.Light;
            }
        }
    }

    public bool IsDarkTheme
    {
        get => ThemeMode == ThemeMode.Dark;
        set
        {
            if (value)
            {
                ThemeMode = ThemeMode.Dark;
            }
        }
    }

    /// <summary>依次在 跟随系统 → 浅色 → 深色 之间循环。</summary>
    public void CycleTheme()
    {
        ThemeMode = ThemeMode switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    /// <summary>把主题模式应用到 Avalonia。</summary>
    public void ApplyTheme(ThemeMode mode)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    // ---------------------------------------------------------------- 设置页

    public ObservableCollection<string> LanguageOptions { get; } = new();

    public ObservableCollection<string> ParallelismOptions { get; } = new();

    public int LanguageIndex
    {
        // 下拉第一项（索引 0）表示「跟随系统」；
        // 其余项按 (int)Language + 1 排布，因此减去 1 即得到具体语言下标。
        get => _settings.FollowSystemLanguage ? 0 : (int)_settings.Language + 1;
        set
        {
            if (value <= 0)
            {
                // 跟随系统：记录一个上次选择的具体语言（不影响显示），并立即应用系统语言
                _settings.FollowSystemLanguage = true;
                Loc.Language = LanguageExtensions.DetectFromCulture(CultureInfo.CurrentUICulture);
                OnPropertyChanged();
                return;
            }

            int langIndex = value - 1;
            _settings.FollowSystemLanguage = false;
            _settings.Language = (Language)langIndex;
            Loc.Language = _settings.Language;
            OnPropertyChanged();
        }
    }

    public int ParallelismIndex
    {
        get => _settings.Parallelism - 1;
        set
        {
            if (value < 0)
            {
                return;
            }

            _settings.Parallelism = value + 1;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 启动时的语言初始化：跟随系统则按系统区域检测，
    /// 否则用设置里保存的具体语言。
    /// </summary>
    private void ApplyInitialLanguage()
    {
        if (_settings.FollowSystemLanguage)
        {
            Loc.Language = LanguageExtensions.DetectFromCulture(CultureInfo.CurrentUICulture);
        }
        else
        {
            Loc.Language = _settings.Language;
        }
    }

    /// <summary>
    /// 重建下拉选项文本。用「替换元素」而不是「替换集合」，
    /// 以免 SelectedIndex 被重置。
    /// </summary>
    private void RefreshLocalizedOptions()
    {
        // 语言下拉第一项为「跟随系统」，其后为各具体语言（按原生名称显示）。
        var langs = new List<string> { Loc["LangSystem"] };
        langs.AddRange(LanguageExtensions.AllConcrete.Select(l => l.NativeName()));
        Fill(LanguageOptions, langs);

        Fill(ParallelismOptions, Enumerable.Range(1, AppSettings.MaxParallelism).Select(i => i.ToString()).ToArray());
    }

    private static void Fill(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        if (target.Count != values.Count)
        {
            target.Clear();
            foreach (string value in values)
            {
                target.Add(value);
            }

            return;
        }

        for (int i = 0; i < values.Count; i++)
        {
            if (target[i] != values[i])
            {
                target[i] = values[i];
            }
        }
    }

    // ---------------------------------------------------------------- 版本更新 / 关于

    /// <summary>是否正在检查更新（期间禁用按钮，避免重复点击）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdateCommand))]
    private bool _isCheckingUpdate;

    private bool CanCheckUpdate => !IsCheckingUpdate;

    /// <summary>
    /// 检查 GitHub 上的最新 Release：
    /// 有新版本 → 提示并打开 Release 页面；已是最新 → 提示「当前已是最新版本」；失败 → 警告提示。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckUpdate))]
    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        try
        {
            var result = await UpdateService.CheckAsync(CancellationToken.None);

            switch (result.Status)
            {
                case UpdateStatus.UpdateAvailable:
                    ShowToast(Loc.Format("ToastUpdateAvailable", result.LatestTag ?? string.Empty));
                    if (!string.IsNullOrEmpty(result.ReleaseUrl))
                    {
                        UpdateService.OpenUrl(result.ReleaseUrl);
                    }

                    break;

                case UpdateStatus.UpToDate:
                    ShowToast(Loc["ToastUpToDate"]);
                    break;

                default:
                    ShowToast(Loc["ToastUpdateFailed"], isWarning: true);
                    break;
            }
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    /// <summary>打开项目仓库页面（「关于软件」整行点击）。</summary>
    [RelayCommand]
    private void OpenRepository() => UpdateService.OpenUrl(UpdateService.RepositoryUrl);

    // ---------------------------------------------------------------- 轻提示

    /// <summary>主窗口的轻提示。</summary>
    public ToastPresenter Toast { get; } = new();

    /// <summary>设置窗口的轻提示（设置窗口打开期间生效）。</summary>
    public ToastPresenter SettingsToast { get; } = new();

    /// <summary>
    /// 设置窗口是否已打开。设置窗口是模态对话框，主窗口被它挡住，
    /// 因此打开期间的提示一律显示在设置窗口上（由设置窗口自己在开关时维护）。
    /// </summary>
    public bool IsSettingsOpen { get; set; }

    /// <summary>弹出一条轻提示，2.6 秒后自动消失；连续调用会重置计时。</summary>
    public void ShowToast(string message, bool isWarning = false)
        => (IsSettingsOpen ? SettingsToast : Toast).Show(message, isWarning);

    // ---------------------------------------------------------------- 状态栏


    /// <summary>窗口关闭前保存设置。</summary>
    public void PersistSettings(double width, double height)
    {
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        SettingsService.Save(_settings);
    }
}
