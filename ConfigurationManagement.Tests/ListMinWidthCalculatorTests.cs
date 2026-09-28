using Configuration_Management;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты расчёта минимальной ширины области списка баз (issue #309): сумма ширин
/// ВСЕХ видимых колонок — ведущие кнопки, «Название», колонки значений (включая
/// «Действия») — плюс боковые отступы. Скрытые и звёздные колонки считаются
/// корректно, чтобы горизонтальная полоса доходила до последней колонки.
/// </summary>
public sealed class ListMinWidthCalculatorTests
{
    private static ListMinWidthCalculator.Column C(double width, bool visible = true)
        => new(width, visible);

    [Fact]
    public void Compute_AllVisibleColumns_SumsEveryColumn()
    {
        var leading = new[] { C(24), C(0), C(40), C(26) };
        var values = new[] { C(120), C(160), C(80), C(120), C(200), C(140), C(90), C(130), C(130), C(170) };

        // «Название» звёздная → берётся минимум 220; ведущие: 24+40+26=90;
        // значения: 120+160+80+120+200+140+90+130+130+170=1340; отступы 8*2.
        var total = ListMinWidthCalculator.Compute(nameWidth: 0, nameFallback: 220, leading, padding: 8, values);

        Assert.Equal(220 + 90 + 16 + 1340, total);
    }

    [Fact]
    public void Compute_HiddenColumns_TakeNoSpace()
    {
        // Колонка «Действия» скрыта (ширина 0), «Последняя копия» выключена —
        // места в сумме они не занимают.
        var leading = new[] { C(24), C(0), C(40), C(26) };
        var values = new[]
        {
            C(120), C(160), C(80), C(120), C(200), C(140), C(90), C(130),
            C(130, visible: false), C(0)
        };

        var total = ListMinWidthCalculator.Compute(0, 220, leading, 0, values);

        Assert.Equal(220 + 90 + 1040, total); // без скрытой 130 и нулевой 0
    }

    [Fact]
    public void Compute_FixedNameWidth_UsesItInsteadOfFallback()
    {
        var total = ListMinWidthCalculator.Compute(
            nameWidth: 300, nameFallback: 220,
            Array.Empty<ListMinWidthCalculator.Column>(), 0,
            new[] { C(120) });

        Assert.Equal(300 + 120, total);
    }

    [Fact]
    public void Compute_NoValueColumns_ReturnsNameAndLeadingWithPadding()
    {
        var total = ListMinWidthCalculator.Compute(
            0, 220,
            new[] { C(24), C(40), C(26) },
            8,
            Array.Empty<ListMinWidthCalculator.Column>());

        Assert.Equal(220 + 90 + 16, total);
    }

    [Fact]
    public void SumVisible_IgnoresHiddenAndZeroWidth()
    {
        var columns = new[] { C(50), C(60, visible: false), C(0), C(70) };

        Assert.Equal(120, ListMinWidthCalculator.SumVisible(columns));
    }

    [Fact]
    public void SumVisible_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, ListMinWidthCalculator.SumVisible(Array.Empty<ListMinWidthCalculator.Column>()));
    }
}