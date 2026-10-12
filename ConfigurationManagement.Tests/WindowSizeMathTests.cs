using Configuration_Management;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистых расчётов размеров/позиции окон (<see cref="WindowSizeMath"/>, issue #308/#305):
/// клампинг высоты окна по контенту, подъём окна, уходящего за нижний край рабочей области,
/// и границы максимизации на рабочую область монитора (issue #368).
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

    // ---- ComputeMaximizedBounds (issue #368: максимизация на рабочую область) ----

    private static (int X, int Y, int Width, int Height) MaxBounds(
        (int Left, int Top, int Right, int Bottom) monitor,
        (int Left, int Top, int Right, int Bottom) work)
    {
        WindowSizeMath.ComputeMaximizedBounds(monitor, work,
            out int x, out int y, out int w, out int h);
        return (x, y, w, h);
    }

    [Fact]
    public void MaximizeBounds_TaskbarAtBottom_WindowCoversWorkAreaOnly()
    {
        var monitor = (0, 0, 1920, 1080);
        var work = (0, 0, 1920, 1040); // панель задач 40px внизу

        Assert.Equal((0, 0, 1920, 1040), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_TaskbarAtTop_WindowDoesNotCoverIt()
    {
        var monitor = (0, 0, 1920, 1080);
        var work = (0, 40, 1920, 1080);

        Assert.Equal((0, 40, 1920, 1040), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_TaskbarAtLeft_WindowDoesNotCoverIt()
    {
        var monitor = (0, 0, 1920, 1080);
        var work = (60, 0, 1920, 1080);

        Assert.Equal((60, 0, 1860, 1080), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_TaskbarAtRight_WindowDoesNotCoverIt()
    {
        var monitor = (0, 0, 1920, 1080);
        var work = (0, 0, 1860, 1080);

        Assert.Equal((0, 0, 1860, 1080), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_WorkAreaEqualsMonitor_MaximizesToFullScreen()
    {
        var monitor = (0, 0, 2560, 1440);
        var work = (0, 0, 2560, 1440);

        Assert.Equal((0, 0, 2560, 1440), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_SecondaryMonitor_PositionIsRelativeToItsOrigin()
    {
        // Второй монитор справа от основного (1920x1080).
        var monitor = (1920, 0, 3840, 1080);
        var work = (1920, 0, 3840, 1040);

        // ptMaxPosition задаётся относительно монитора, а не экрана.
        Assert.Equal((0, 0, 1920, 1040), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_MonitorWithNegativeCoordinates_RelativePositionStaysNonNegative()
    {
        // Монитор слева от основного — отрицательные координаты в системе экранов.
        var monitor = (-1920, 0, 0, 1080);
        var work = (-1920, 0, 0, 1040);

        Assert.Equal((0, 0, 1920, 1040), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_TaskbarsOnTwoSides_CombinedOffsets()
    {
        // Панель задач 60px слева и 40px сверху (док + закреплённая панель).
        var monitor = (0, 0, 2560, 1440);
        var work = (60, 40, 2560, 1440);

        Assert.Equal((60, 40, 2500, 1400), MaxBounds(monitor, work));
    }

    [Fact]
    public void MaximizeBounds_WorkAreaOnElevatedSecondaryMonitor_RelativeOffsets()
    {
        // Второй монитор выше и праве первого, панель задач — внизу.
        var monitor = (1920, -400, 3840, 680);
        var work = (1920, -400, 3840, 640);

        Assert.Equal((0, 0, 1920, 1040), MaxBounds(monitor, work));
    }
}