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

    // ======================= Регрессия полного сценария #313 =======================

    [Fact]
    public void Regression313_PlainClickThenCtrlClicksThenRightClick_KeepsCtrlMarkedSet()
    {
        // Сценарий из комментария пользователя к issue #313:
        // строка "a" была просто текущей (обычный клик, набор пуст),
        // затем Ctrl-кликами добавлены "b" и "c"; правый клик по "c"
        // не должен терять "a" — потому что "a" в набор и не входила,
        // а "b"/"c" (отмеченные Ctrl) обязаны остаться.
        var set = new HashSet<string>(StringComparer.Ordinal);

        // Обычный клик по "a" в UI вызывает ClearBatchSelection — набор остаётся пустым.
        Assert.Empty(set);

        set = BatchSelectionHelper.ApplyModifiedClick(set, currentSectionIsPinned: false, "b", false, "Ctrl");
        set = BatchSelectionHelper.ApplyModifiedClick(set, currentSectionIsPinned: false, "c", false, "Ctrl");

        var rightClick = BatchSelectionHelper.BuildRightClickSet(set, rightClickedId: "c");

        Assert.Equal(new[] { "b", "c" }, rightClick.OrderBy(x => x));
        Assert.DoesNotContain("a", rightClick);
    }

    [Fact]
    public void Regression313_FirstRowMarkedWithCtrl_SurvivesRightClick()
    {
        // Когда первую строку сразу пометили Ctrl (как описывает пользователь),
        // после правого клика она не должна пропадать.
        var set = new HashSet<string>(StringComparer.Ordinal);
        set = BatchSelectionHelper.ApplyModifiedClick(set, currentSectionIsPinned: false, "a", false, "Ctrl");
        set = BatchSelectionHelper.ApplyModifiedClick(set, currentSectionIsPinned: false, "b", false, "Ctrl");

        var rightClick = BatchSelectionHelper.BuildRightClickSet(set, rightClickedId: "b");

        Assert.Equal(new[] { "a", "b" }, rightClick.OrderBy(x => x));
    }

    [Fact]
    public void Regression313_ShiftRangeThenRightClick_KeepsRange()
    {
        // Shift-диапазон от "a" до "c" (якорь — последняя обычная строка);
        // правый клик по границе диапазона не снимает строки.
        var set = new HashSet<string>(StringComparer.Ordinal);
        set = BatchSelectionHelper.ApplyModifiedClick(
            set,
            currentSectionIsPinned: false,
            targetId: "c",
            targetSectionIsPinned: false,
            modifier: "Shift",
            visibleOrder: new[] { "a", "b", "c" },
            anchorId: "a");

        var rightClick = BatchSelectionHelper.BuildRightClickSet(set, rightClickedId: "c");

        Assert.Equal(new[] { "a", "b", "c" }, rightClick.OrderBy(x => x));
    }
}