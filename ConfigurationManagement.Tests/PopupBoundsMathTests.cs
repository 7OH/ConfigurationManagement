using Configuration_Management;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой геометрии всплывающей панели выбора окна (issue #359): зажатие панели
/// в границах главного окна и ограничение её размера размером окна.
/// </summary>
public sealed class PopupBoundsMathTests
{
    private const double Margin = PopupBoundsMath.DefaultMargin; // 8
    private const double Gap = PopupBoundsMath.DefaultGap;       // 4

    [Theory]
    [InlineData(1200, 1184)] // окно 1200 → ширина с учётом отступа 8 с каждой стороны
    [InlineData(900, 884)]
    [InlineData(100, 84)]
    public void MaxPopupSize_SubtractsMarginFromBothSides(double bounds, double expected)
    {
        Assert.Equal(expected, PopupBoundsMath.MaxPopupSize(bounds, Margin));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void MaxPopupSize_InvalidBounds_ReturnsZero(double bounds)
    {
        // Некорректный размер границ — ограничение не задаётся (0), чтобы не резать панель.
        Assert.Equal(0, PopupBoundsMath.MaxPopupSize(bounds, Margin));
    }

    [Fact]
    public void MaxPopupSize_MarginLargerThanBounds_ReturnsZero()
    {
        Assert.Equal(0, PopupBoundsMath.MaxPopupSize(10, 8)); // 10 - 16 < 0
    }

    [Fact]
    public void PlaceAboveClamped_TargetInside_KeepsLeftEdgeAboveTarget()
    {
        // Индикатор в центре: панель стоит по его левому краю, над ним, с зазором 4.
        var (left, top) = PopupBoundsMath.PlaceAboveClamped(
            targetLeft: 500, targetTop: 730,
            popupWidth: 280, popupHeight: 120,
            boundsWidth: 1200, boundsHeight: 760,
            margin: Margin, gap: Gap);

        Assert.Equal(500, left);
        Assert.Equal(730 - Gap - 120, top); // 606
    }

    [Fact]
    public void PlaceAboveClamped_NearRightEdge_ClampsToLeftOfOverflow()
    {
        // Индикатор у правого края (главный баг #359): панель 280 шириной при
        // left=1100 ушла бы за правый край 1200 → зажимаем до 1200-8-280=912.
        var (left, top) = PopupBoundsMath.PlaceAboveClamped(
            targetLeft: 1100, targetTop: 730,
            popupWidth: 280, popupHeight: 120,
            boundsWidth: 1200, boundsHeight: 760,
            margin: Margin, gap: Gap);

        Assert.Equal(912, left);
        Assert.Equal(606, top);
    }

    [Fact]
    public void PlaceAboveClamped_NearLeftEdge_ClampsToMargin()
    {
        // Индикатор у левого края: left=2 меньше отступа 8 → прижимаем к 8.
        var (left, _) = PopupBoundsMath.PlaceAboveClamped(
            targetLeft: 2, targetTop: 730,
            popupWidth: 280, popupHeight: 120,
            boundsWidth: 1200, boundsHeight: 760,
            margin: Margin, gap: Gap);

        Assert.Equal(Margin, left);
    }

    [Fact]
    public void PlaceAboveClamped_PopupWiderThanWindow_SticksToMargin()
    {
        // Панель шире окна: maxLeft = 1200-8-1300 = -108 < margin → зажимаем к margin.
        var (left, _) = PopupBoundsMath.PlaceAboveClamped(
            targetLeft: 600, targetTop: 730,
            popupWidth: 1300, popupHeight: 120,
            boundsWidth: 1200, boundsHeight: 760,
            margin: Margin, gap: Gap);

        Assert.Equal(Margin, left);
    }

    [Fact]
    public void PlaceAboveClamped_NearTop_ClampsToMargin()
    {
        // Индикатор у верхнего края: 70-4-120 = -54 < 8 → прижимаем к 8.
        var (_, top) = PopupBoundsMath.PlaceAboveClamped(
            targetLeft: 500, targetTop: 70,
            popupWidth: 280, popupHeight: 120,
            boundsWidth: 1200, boundsHeight: 760,
            margin: Margin, gap: Gap);

        Assert.Equal(Margin, top);
    }

    [Fact]
    public void PlaceAboveClamped_PopupTallerThanWindow_SticksToMargin()
    {
        // Панель выше окна: maxTop = 760-8-800 = -48 < margin → зажимаем к margin.
        var (_, top) = PopupBoundsMath.PlaceAboveClamped(
            targetLeft: 500, targetTop: 730,
            popupWidth: 280, popupHeight: 800,
            boundsWidth: 1200, boundsHeight: 760,
            margin: Margin, gap: Gap);

        Assert.Equal(Margin, top);
    }

    [Fact]
    public void PlaceAboveClamped_InvalidBounds_OnlyOffsetsAboveTarget()
    {
        // Размеры границ неизвестны (<=0): клампинг по ним выключен, остаётся
        // только подъём над целью на gap.
        var (left, top) = PopupBoundsMath.PlaceAboveClamped(
            targetLeft: 500, targetTop: 730,
            popupWidth: 280, popupHeight: 120,
            boundsWidth: 0, boundsHeight: 0,
            margin: Margin, gap: Gap);

        Assert.Equal(500, left);
        Assert.Equal(606, top);
    }

    [Fact]
    public void PlaceAboveClamped_Defaults_MatchConstants()
    {
        // Без явных margin/gap используются константы: 8 и 4.
        var withDefaults = PopupBoundsMath.PlaceAboveClamped(
            1100, 730, 280, 120, 1200, 760);
        var withExplicit = PopupBoundsMath.PlaceAboveClamped(
            1100, 730, 280, 120, 1200, 760, Margin, Gap);

        Assert.Equal(withExplicit, withDefaults);
        Assert.Equal(8, PopupBoundsMath.DefaultMargin);
        Assert.Equal(4, PopupBoundsMath.DefaultGap);
    }
}
