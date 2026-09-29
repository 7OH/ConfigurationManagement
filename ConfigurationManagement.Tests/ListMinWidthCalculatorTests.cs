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

    /// <summary>
    /// Сценарий issue #309: сумма ширин всех колонок больше вьюпорта (узкое окно).
    /// Минимальная ширина должна быть НЕ МЕНЬШЕ фактической суммы — тогда полоса
    /// прокрутки дотягивает до последней (новой) колонки, а не обрезает её.
    /// </summary>
    [Fact]
    public void Compute_ColumnsWiderThanViewport_MinWidthReachesLastColumn()
    {
        // Набор как на скриншоте issue #309: ведущие 24+40+26, фиксированное
        // «Название» 250, десять колонок значений, включая «Действия» и «№ релиза».
        var leading = new[] { C(24), C(40), C(26) };
        var values = new[]
        {
            C(120), C(160), C(170), C(200), C(140), C(90), C(130), C(130), C(160), C(100)
        };

        const double viewport = 982; // ширина окна пользователя со скриншота
        var total = ListMinWidthCalculator.Compute(250, 220, leading, 8, values);

        // 250 + 90 + 16 + 1400 = 1756: минимум строго больше вьюпорта — полоса есть,
        // и равна сумме ВСЕХ видимых колонок — последняя колонка достижима.
        Assert.Equal(250 + (24 + 40 + 26) + 8 * 2 + 1400, total);
        Assert.True(total > viewport, "Минимальная ширина должна превышать вьюпорт — полоса прокрутки нужна.");
        Assert.True(total >= 250 + 90 + 1400, "Полоса должна дотягивать до последней (новой) колонки.");
    }

    /// <summary>
    /// Сценарий-антирегресс issue #255: сумма ширин всех колонок помещается во
    /// вьюпорт — минимальная ширина не должна превышать вьюпорт, иначе появится
    /// ложная горизонтальная полоса при помещающихся колонках.
    /// </summary>
    [Fact]
    public void Compute_ColumnsFitViewport_NoHorizontalScrollbar()
    {
        var leading = new[] { C(24), C(26) };
        var values = new[] { C(120), C(80) };

        const double viewport = 500;
        var total = ListMinWidthCalculator.Compute(150, 220, leading, 8, values);

        // 150 + 50 + 16 + 200 = 416 <= 500: минимум не превышает вьюпорт — полосы нет.
        Assert.Equal(150 + 50 + 16 + 200, total);
        Assert.True(total <= viewport, "Минимальная ширина не должна превышать вьюпорт — полоса прокрутки не нужна.");
    }

    /// <summary>
    /// Fallback имени при нулевой ширине «Названия»: пока колонка звёздная (ширина не
    /// задана перетаскиванием разделителя), в расчёт идёт фиксированный минимум, а не
    /// ноль — иначе сумма занижалась бы и полоса не доезжала бы до последней колонки
    /// (issue #309).
    /// </summary>
    [Fact]
    public void Compute_NameZeroWidth_UsesFallback()
    {
        var leading = new[] { C(24), C(26) };
        var values = new[] { C(120), C(80) };

        var total = ListMinWidthCalculator.Compute(nameWidth: 0, nameFallback: 220, leading, 0, values);

        // 220 (fallback) + 50 + 200 = 470: ноль ширины имени не обнуляет её вклад.
        Assert.Equal(220 + 50 + 200, total);
    }

    /// <summary>
    /// Последняя (одна) колонка в конце списка достижима: сумма видимых колонок,
    /// включая самую правую широкую, попадает в минимум целиком — горизонтальная
    /// полоса обязана дотягивать до неё, а не обрезать (issue #309).
    /// </summary>
    [Fact]
    public void Compute_SingleWideLastColumn_Reachable()
    {
        var leading = new[] { C(24), C(26) };
        // В конце стоит единственная широкая колонка — минимум обязан включать её всю.
        var values = new[] { C(500) };

        const double viewport = 400; // уже: одна широкая колонка не помещается
        var total = ListMinWidthCalculator.Compute(220, 220, leading, 0, values);

        Assert.Equal(220 + 50 + 500, total);
        Assert.True(total > viewport, "Полоса нужна: сумма колонок больше вьюпорта.");
        Assert.True(total >= 220 + 50 + 500,
            "Минимум должен включать правую колонку целиком — последняя колонка достижима.");
    }

    /// <summary>
    /// Сценарий issue #309 (повторный регресс 0.3.9.156): фактическая желаемая ширина
    /// строки (длинное название базы в горизонтальном StackPanel) больше суммы ширин
    /// колонок — минимум обязан дотягивать до реального контента, иначе при прокрутке
    /// «до конца» последняя колонка остаётся лишь частично видимой.
    /// </summary>
    [Fact]
    public void Compute_ContentWiderThanColumns_UsesContentWidth()
    {
        var leading = new[] { C(24), C(26) };
        var values = new[] { C(120), C(80) };

        // Сумма колонок: 220 + 50 + 200 = 470, а строка хочет 640 (длинное имя базы).
        var total = ListMinWidthCalculator.Compute(220, 220, leading, 0, values, actualContentWidth: 640);

        Assert.Equal(640, total);
        Assert.True(total > 470, "Минимум должен быть не меньше фактической ширины строки.");
    }

    /// <summary>
    /// Анти-регресс issue #255 (ложная полоса при помещающихся колонках): фактическая
    /// ширина строк НЕ увеличивает минимум, когда сумма колонок уже покрывает контент.
    /// Полоса появляется только когда контент реально шире области.
    /// </summary>
    [Fact]
    public void Compute_ContentNarrowerThanColumns_KeepsColumnSum()
    {
        var leading = new[] { C(24), C(26) };
        var values = new[] { C(120), C(80) };

        // Сумма колонок 470, строка хочет меньше (400) — минимум остаётся по сумме.
        var total = ListMinWidthCalculator.Compute(220, 220, leading, 0, values, actualContentWidth: 400);

        Assert.Equal(470, total);

        // И сумма, и контент помещаются во вьюпорт — полосы быть не должно.
        const double viewport = 600;
        Assert.True(total <= viewport, "Минимум не должен превышать вьюпорт — ложной полосы нет.");
    }

    /// <summary>
    /// Нулевая фактическая ширина (строки ещё не материализованы) не меняет расчёт —
    /// минимум остаётся по сумме колонок с первого прохода раскладки (issue #309).
    /// </summary>
    [Fact]
    public void Compute_NoContentYet_KeepsColumnSum()
    {
        var leading = new[] { C(24), C(26) };
        var values = new[] { C(120), C(80) };

        var total = ListMinWidthCalculator.Compute(220, 220, leading, 0, values, actualContentWidth: 0);

        Assert.Equal(470, total);
    }
}