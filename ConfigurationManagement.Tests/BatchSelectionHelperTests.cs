using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой логики мультивыделения «для выделенных»:
/// правило секций дерева («Закреплённые» vs обычный список, issue #326)
/// и построение набора при правом клике (issue #313).
/// </summary>
public sealed class BatchSelectionHelperTests
{
    // ======================= Issue #326: секции не смешиваются =======================

    [Fact]
    public void ApplyModifiedClick_CtrlClickInOtherSection_ClearsCurrentSetAndStartsNew()
    {
        // Набор собран в обычном списке (секция не закреплённая)…
        var current = new[] { "regular-1", "regular-2" };

        // …Ctrl-клик по закреплённой строке не добавляет её к обычным, а начинает новый набор.
        var result = BatchSelectionHelper.ApplyModifiedClick(
            current,
            currentSectionIsPinned: false,
            targetId: "pinned-1",
            targetSectionIsPinned: true,
            modifier: "Ctrl");

        Assert.Equal(new[] { "pinned-1" }, result.OrderBy(x => x));
    }

    [Fact]
    public void ApplyModifiedClick_CtrlClickInsideSameSection_TogglesOnlyTarget()
    {
        var current = new[] { "regular-1" };

        // Ctrl+клик по второй обычной строке — добавление.
        var added = BatchSelectionHelper.ApplyModifiedClick(
            current, currentSectionIsPinned: false, "regular-2", targetSectionIsPinned: false, "Ctrl");
        Assert.Equal(new[] { "regular-1", "regular-2" }, added.OrderBy(x => x));

        // Повторный Ctrl+клик по той же строке — снятие (toggle).
        var removed = BatchSelectionHelper.ApplyModifiedClick(
            added, currentSectionIsPinned: false, "regular-2", targetSectionIsPinned: false, "Ctrl");
        Assert.Equal(new[] { "regular-1" }, removed.OrderBy(x => x));
    }

    [Fact]
    public void ApplyModifiedClick_ShiftRangeInsidePinnedSection_DoesNotIncludeRegularRows()
    {
        // Закреплённые строки идут в видимом порядке первыми; обычный список ниже.
        // Shift-диапазон в закреплениях должен пройти ТОЛЬКО по закреплённым строкам.
        var pinnedOrder = new[] { "pinned-1", "pinned-2", "pinned-3" };

        var result = BatchSelectionHelper.ApplyModifiedClick(
            currentIds: Array.Empty<string>(),
            currentSectionIsPinned: false,
            targetId: "pinned-3",
            targetSectionIsPinned: true,
            modifier: "Shift",
            visibleOrder: pinnedOrder,
            anchorId: "pinned-1");

        Assert.Equal(pinnedOrder, result.OrderBy(x => x));
    }

    [Fact]
    public void ApplyModifiedClick_ShiftClickFromOtherSectionAnchor_SelectsOnlyTarget()
    {
        // Якорь (обычная база) не принадлежит секции закреплений: диапазон строиться
        // не должен — в набор попадает только строка под Shift-кликом (#326).
        var current = new[] { "regular-anchor" };

        var result = BatchSelectionHelper.ApplyModifiedClick(
            current,
            currentSectionIsPinned: false,
            targetId: "pinned-5",
            targetSectionIsPinned: true,
            modifier: "Shift",
            visibleOrder: new[] { "pinned-1", "pinned-2", "pinned-3", "pinned-4", "pinned-5" },
            anchorId: "regular-anchor");

        Assert.Equal(new[] { "pinned-5" }, result.OrderBy(x => x));
    }

    [Fact]
    public void ApplyModifiedClick_ShiftRangeCrossesSections_ClearsOtherSectionFirst()
    {
        // Набор в закреплениях, Shift-клик по обычной строке: набор очищается,
        // диапазон строится по обычному порядку от якоря (если он в секции) до цели.
        var current = new[] { "pinned-1", "pinned-2" };

        var result = BatchSelectionHelper.ApplyModifiedClick(
            current,
            currentSectionIsPinned: true,
            targetId: "regular-3",
            targetSectionIsPinned: false,
            modifier: "Shift",
            visibleOrder: new[] { "regular-1", "regular-2", "regular-3" },
            anchorId: "regular-1");

        Assert.Equal(new[] { "regular-1", "regular-2", "regular-3" }, result.OrderBy(x => x));
    }

    [Fact]
    public void ApplyModifiedClick_NullOrEmptyTarget_ReturnsCurrentSet()
    {
        var current = new[] { "regular-1" };

        var noCtrlTarget = BatchSelectionHelper.ApplyModifiedClick(
            current, currentSectionIsPinned: false, null, targetSectionIsPinned: false, "Ctrl");
        Assert.Equal(current, noCtrlTarget);

        var noShiftTarget = BatchSelectionHelper.ApplyModifiedClick(
            current, currentSectionIsPinned: false, null, targetSectionIsPinned: false, "Shift");
        Assert.Equal(current, noShiftTarget);
    }

    // ======================= Issue #313: правый клик набор не меняет =======================

    [Fact]
    public void BuildRightClickSet_RowWasSimplyCurrent_NotAddedToSet()
    {
        // Сценарий из issue #313: первая строка была просто текущей (без Ctrl),
        // затем Ctrl-кликами добавлены другие. При правом клике по последней
        // Ctrl-строке «бывшая текущая» НЕ должна попадать в набор.
        var batch = new[] { "b", "c" };

        var result = BatchSelectionHelper.BuildRightClickSet(batch, rightClickedId: "c");

        Assert.Equal(new[] { "b", "c" }, result.OrderBy(x => x));
        Assert.DoesNotContain("a", result);
    }

    [Fact]
    public void BuildRightClickSet_RightClickOnUnmarkedRow_DoesNotAddIt()
    {
        var batch = new[] { "x", "y" };

        var result = BatchSelectionHelper.BuildRightClickSet(batch, rightClickedId: "z");

        Assert.Equal(new[] { "x", "y" }, result.OrderBy(x => x));
        Assert.DoesNotContain("z", result);
    }

    [Fact]
    public void BuildRightClickSet_RightClickOnMarkedRow_KeepsIt()
    {
        var batch = new[] { "x", "y" };

        var result = BatchSelectionHelper.BuildRightClickSet(batch, rightClickedId: "y");

        Assert.Equal(new[] { "x", "y" }, result.OrderBy(x => x));
    }

    [Fact]
    public void BuildRightClickSet_EmptySet_StaysEmpty()
    {
        var result = BatchSelectionHelper.BuildRightClickSet(Array.Empty<string>(), rightClickedId: "a");
        Assert.Empty(result);
    }
}