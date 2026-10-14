using System.Globalization;
using Avalonia.Data.Converters;

namespace CheckSumTool.Converters;

/// <summary>
/// 把行内进度（0~100 的整数）转成缩放系数（0.0~1.0），
/// 供 <c>Border</c> 的 <c>ScaleTransform</c> 使用，自行绘制行内进度条。
/// </summary>
/// <remarks>
/// 之所以不用 <see cref="Avalonia.Controls.ProgressBar"/>：
/// 它在非约束宽度下会把指示器的 Width 写死，导致行内进度条尺寸失真、溢出单元格。
/// </remarks>
public sealed class PercentToScaleConverter : IValueConverter
{
    public static readonly PercentToScaleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double percent = value switch
        {
            int i => i,
            double d => d,
            float f => f,
            long l => l,
            _ => 0,
        };

        return Math.Clamp(percent / 100.0, 0, 1);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? (int)(d * 100) : 0;
}
