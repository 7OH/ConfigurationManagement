using Configuration_Management;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистых расчётов размеров/позиции окон (<see cref="WindowSizeMath"/>, issue #308/#305):
/// клампинг высоты окна по контенту и подъём окна, уходящего за нижний край рабочей области.
/// </summary>
public sealed class WindowSizeMathTests
{
    [Theory]
    [InlineData(500, 420, 800, 500)] // контент в пределах ограничений
    [InlineData(300, 420, 800, 420)] // контент меньше MinHeight
    [InlineData(950, 420, 800, 800)] // контент больше MaxHeight
    [InlineData(420, 420, 800, 420)] // ровно MinHeight
    [InlineData(800, 420, 800, 800)] // ровно MaxHeight
    public void ClampHeight_RespectsMinAndMax(double desired, double min, double max, double expected)
    {
        Assert.Equal(expected, WindowSizeMath.ClampHeight(desired, min, max));
    }

    [Fact]
    public void ClampHeight_MaxZero_MeansNoLimit()
    {
        // max = 0 — ограничение не задано: результат не режется сверху.
        Assert.Equal(1200, WindowSizeMath.ClampHeight(1200, 420, 0));
    }

    [Fact]
    public void ClampHeight_NegativeDesired_IsRaisedToMin()
    {
        Assert.Equal(420, WindowSizeMath.ClampHeight(-10, 420, 800));
    }

    [Fact]
    public void FitTop_InsideWorkArea_KeepsPosition()
    {
        // top=100, height=600, область 0..1080: окно помещается — позиция не меняется.
        Assert.Equal(100, WindowSizeMath.FitTop(100, 600, 0, 1080));
    }

    [Fact]
    public void FitTop_BelowWorkAreaBottom_RaisesWindow()
    {
        // top=700, height=600, область 0..1080: низ 1300 > 1080 — поднимаем до 480.
        Assert.Equal(480, WindowSizeMath.FitTop(700, 600, 0, 1080));
    }

    [Fact]
    public void FitTop_TallerThanWorkArea_SticksToTop()
    {
        // Окно выше рабочей области: прижимаем к верхнему краю (не даём уйти за него).
        Assert.Equal(0, WindowSizeMath.FitTop(200, 1400, 0, 1080));
    }

    [Fact]
    public void FitTop_InvalidWorkArea_KeepsPosition()
    {
        // Некорректная область (низ <= верх) — позицию не трогаем.
        Assert.Equal(50, WindowSizeMath.FitTop(50, 600, 100, 100));
    }
}