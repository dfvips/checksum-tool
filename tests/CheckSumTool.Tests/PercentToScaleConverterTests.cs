using System.Globalization;
using CheckSumTool.Converters;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>
/// 行内进度转换器：行内进度条用 <c>Border + ScaleTransform</c> 自绘，
/// 由它把 0~100 的整数进度换算成 0.0~1.0 的缩放系数。
/// </summary>
public class PercentToScaleConverterTests
{
    private readonly PercentToScaleConverter _converter = PercentToScaleConverter.Instance;

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 0.01)]
    [InlineData(50, 0.5)]
    [InlineData(97, 0.97)]
    [InlineData(100, 1.0)]
    public void ConvertsWithinRange(int percent, double expected)
        => AssertDelta(expected, _converter.Convert(percent, typeof(double), null, CultureInfo.InvariantCulture));

    /// <summary>xUnit 的 (double,double,int) 重载与泛型重载存在歧义，这里自己比带精度的相等。</summary>
    private static void AssertDelta(double expected, object? actual)
    {
        Assert.NotNull(actual);
        Assert.True(Math.Abs(expected - (double)actual!) < 1e-9, $"期望 {expected}，实际 {actual}");
    }
    [Theory]
    [InlineData(-20)]
    [InlineData(500)]
    public void ClampsOutOfRange(int percent)
        => Assert.InRange((double)_converter.Convert(percent, typeof(double), null, CultureInfo.InvariantCulture)!, 0, 1);

    [Fact]
    public void HandlesDouble()
        => Assert.Equal(0.42, (double)_converter.Convert(42.0, typeof(double), null, CultureInfo.InvariantCulture)!, 10);

    [Fact]
    public void NullYieldsZero()
        => Assert.Equal(0.0, _converter.Convert(null, typeof(double), null, CultureInfo.InvariantCulture));

    [Fact]
    public void UnsupportedTypeYieldsZero()
        => Assert.Equal(0.0, _converter.Convert("abc", typeof(double), null, CultureInfo.InvariantCulture));

    [Fact]
    public void RoundTripsBackToPercent()
        => Assert.Equal(50, _converter.ConvertBack(0.5, typeof(int), null, CultureInfo.InvariantCulture));
}
