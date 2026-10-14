using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace CheckSumTool.Controls;
/// <summary>
/// 轻量下拉选择器（Element 风格）。菜单项在代码里按需生成，
/// 避免模板绑定在弹出层中断裂的问题。
/// </summary>
public partial class SelectBox : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SelectBox, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<SelectBox, int>(
            nameof(SelectedIndex),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PlaceholderProperty =
        AvaloniaProperty.Register<SelectBox, string?>(nameof(Placeholder), "—");

    private INotifyCollectionChanged? _observed;
    private Flyout? _flyout;

    static SelectBox()
    {
        ItemsSourceProperty.Changed.AddClassHandler<SelectBox>((box, _) => box.OnItemsSourceChanged());
        SelectedIndexProperty.Changed.AddClassHandler<SelectBox>((box, _) => box.Rebuild());
    }

    public SelectBox()
    {
        InitializeComponent();
        Rebuild();

        _flyout = this.FindControl<Button>("RootButton")?.Flyout as Flyout;
        if (_flyout is not null)
        {
            _flyout.Opened += (_, _) =>
            {
                SyncMenuWidth();
                AlignMenuWithControl();
            };
        }

        // 尺寸变化时同步菜单宽度，保证弹出时宽度已经正确（避免弹出后再变宽导致位置偏差）
        PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                SyncMenuWidth();
            }
        };
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public string? Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private void OnItemsSourceChanged()
    {
        if (_observed is not null)
        {
            _observed.CollectionChanged -= OnCollectionChanged;
        }

        _observed = ItemsSource as INotifyCollectionChanged;
        if (_observed is not null)
        {
            _observed.CollectionChanged += OnCollectionChanged;
        }

        Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>把弹出面板宽度对齐到控件宽度。</summary>
    private void SyncMenuWidth()
    {
        if (this.FindControl<Border>("MenuHost") is { } host && Bounds.Width > 0)
        {
            host.Width = Bounds.Width;
        }
    }

    /// <summary>
    /// 弹出后测量菜单相对本控件的实际横向偏移并补偿。
    /// 不同平台/主题下弹出层可能带有额外内边距（Fluent 的 FlyoutPresenter 模板里
    /// 内层 Border 用的是硬编码 DynamicResource，样式 setter 覆盖不到），
    /// 这里做一次自适应校正，保证菜单左右边缘与控件严格对齐。
    /// </summary>
    private void AlignMenuWithControl()
    {
        if (_flyout is null
            || this.FindControl<Border>("MenuHost") is not { } menu
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var controlOrigin = this.TranslatePoint(new Point(0, 0), topLevel);
        var menuOrigin = menu.TranslatePoint(new Point(0, 0), topLevel);

        if (controlOrigin is null || menuOrigin is null)
        {
            return;
        }

        double delta = menuOrigin.Value.X - controlOrigin.Value.X;
        if (Math.Abs(delta) > 0.5)
        {
            _flyout.HorizontalOffset -= delta;
        }
    }

    private List<string> CurrentItems()
    {
        var items = new List<string>();
        if (ItemsSource is null)
        {
            return items;
        }

        foreach (object? item in ItemsSource)
        {
            items.Add(item?.ToString() ?? string.Empty);
        }

        return items;
    }

    private void Rebuild()
    {
        List<string> items = CurrentItems();
        bool hasSelection = SelectedIndex >= 0 && SelectedIndex < items.Count;

        if (this.FindControl<TextBlock>("LabelText") is { } label)
        {
            label.Text = hasSelection ? items[SelectedIndex] : Placeholder ?? "—";

            if (hasSelection)
            {
                label.Classes.Remove("placeholder");
            }
            else if (!label.Classes.Contains("placeholder"))
            {
                label.Classes.Add("placeholder");
            }
        }

        if (this.FindControl<Border>("MenuHost") is not { } host)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 2 };

        // 最多显示 3 项，超出则出现滚动条（选项项高 36 + 间距 2）
        const int MaxVisibleOptions = 3;
        const int OptionHeight = 36;
        const int OptionSpacing = 2;

        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            var option = new Button
            {
                Content = items[i],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };

            option.Classes.Add("option");
            if (index == SelectedIndex)
            {
                option.Classes.Add("selected");
            }

            option.Click += (_, _) =>
            {
                SelectedIndex = index;

                if (this.FindControl<Button>("RootButton")?.Flyout is Flyout flyout)
                {
                    flyout.Hide();
                }
            };

            panel.Children.Add(option);
        }

        if (panel.Children.Count > MaxVisibleOptions)
        {
            host.Child = new ScrollViewer
            {
                Content = panel,
                MaxHeight = MaxVisibleOptions * OptionHeight + (MaxVisibleOptions - 1) * OptionSpacing,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            };
        }
        else
        {
            host.Child = panel;
        }
    }
}
