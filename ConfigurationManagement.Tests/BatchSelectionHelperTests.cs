using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
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
        // Правый клик сам по себе набор не меняет: «бывшая текущая» (выбранная
        // без Ctrl) попадает в набор НЕ здесь, а на этапе Ctrl-клика (issue #313,
        // см. ApplyModifiedClick + includeId). Для BuildRightClickSet действует
        // прежнее правило: строка, которой нет в наборе, не добавляется.
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

    // ======================= Issue #313: первый Ctrl-клик добавляет «текущую» =======================

    [Fact]
    public void ApplyModifiedClick_FirstCtrlClickOnOtherRow_AddsCurrentAndTarget()
    {
        // Правило 7OH (issue #313): «при клике с контролом не на текущей строке,
        // ставить внутреннюю галку выделения текущей строке тоже». Первый Ctrl-клик
        // по строке, отличной от «текущей» ("a"), добавляет в набор и "a", и цель "b".
        var set = new HashSet<string>(StringComparer.Ordinal);

        var result = BatchSelectionHelper.ApplyModifiedClick(
            set,
            currentSectionIsPinned: false,
            targetId: "b",
            targetSectionIsPinned: false,
            modifier: "Ctrl",
            includeId: "a",
            includeSectionIsPinned: false);

        Assert.Equal(new[] { "a", "b" }, result.OrderBy(x => x));
    }

    [Fact]
    public void ApplyModifiedClick_FirstCtrlClickOnCurrentRow_TogglesOnlyCurrent()
    {
        // Ctrl-клик по самой «текущей» строке — как раньше: строка просто входит
        // в набор (toggle), ничего лишнего не добавляется.
        var result = BatchSelectionHelper.ApplyModifiedClick(
            Array.Empty<string>(),
            currentSectionIsPinned: false,
            targetId: "a",
            targetSectionIsPinned: false,
            modifier: "Ctrl",
            includeId: "a",
            includeSectionIsPinned: false);

        Assert.Equal(new[] { "a" }, result.OrderBy(x => x));
    }

    [Fact]
    public void ApplyModifiedClick_CtrlClickWithNonEmptySet_KeepsToggleOnly()
    {
        // Набор уже не пуст — повторные Ctrl-клики остаются точечным toggle:
        // «текущая» не «допрыгивает» в набор на каждом клике.
        var current = new[] { "a", "b" };

        // "a" (текущая) уже в наборе — ничего не меняется кроме toggle цели "c".
        var added = BatchSelectionHelper.ApplyModifiedClick(
            current, currentSectionIsPinned: false, "c", targetSectionIsPinned: false, "Ctrl",
            includeId: "a", includeSectionIsPinned: false);
        Assert.Equal(new[] { "a", "b", "c" }, added.OrderBy(x => x));

        // Текущая "z" вне набора при непустом наборе НЕ добавляется (только toggle цели).
        var detached = BatchSelectionHelper.ApplyModifiedClick(
            current, currentSectionIsPinned: false, "c", targetSectionIsPinned: false, "Ctrl",
            includeId: "z", includeSectionIsPinned: false);
        Assert.Equal(new[] { "a", "b", "c" }, detached.OrderBy(x => x));
    }

    [Fact]
    public void ApplyModifiedClick_FirstCtrlClick_CurrentInOtherSection_NotAdded()
    {
        // Правило секций (#326): «текущая» из обычного списка не добавляется в набор,
        // который начинается Ctrl-кликом в «Закреплённых» (и наоборот).
        var result = BatchSelectionHelper.ApplyModifiedClick(
            Array.Empty<string>(),
            currentSectionIsPinned: false,
            targetId: "pinned-1",
            targetSectionIsPinned: true,
            modifier: "Ctrl",
            includeId: "regular-a",
            includeSectionIsPinned: false);

        Assert.Equal(new[] { "pinned-1" }, result.OrderBy(x => x));
        Assert.DoesNotContain("regular-a", result);
    }

    [Fact]
    public void ApplyModifiedClick_FirstCtrlClickInPinnedSection_AddsPinnedCurrentAndTarget()
    {
        // То же правило работает внутри «Закреплённых»: текущая закреплённая строка
        // добавляется вместе с целью Ctrl-клика (обе в секции закреплений).
        var result = BatchSelectionHelper.ApplyModifiedClick(
            Array.Empty<string>(),
            currentSectionIsPinned: false,
            targetId: "pinned-2",
            targetSectionIsPinned: true,
            modifier: "Ctrl",
            includeId: "pinned-1",
            includeSectionIsPinned: true);

        Assert.Equal(new[] { "pinned-1", "pinned-2" }, result.OrderBy(x => x));
    }

    // ======================= Регрессия полного сценария #313 =======================

    [Fact]
    public void Regression313_PlainClickThenCtrlClicksThenRightClick_KeepsFullSet()
    {
        // Сценарий из комментария пользователя к issue #313:
        // строка "a" была просто текущей (обычный клик, набор пуст), затем
        // Ctrl-кликами добавлены "b" и "c"; правый клик по "c" не должен терять
        // ни одной строки: "a" теперь входит в набор с первого Ctrl-клика
        // (как и просил пользователь), "b"/"c" — обычные Ctrl-toggle.
        var set = new HashSet<string>(StringComparer.Ordinal);

        // Обычный клик по "a" в UI вызывает ClearBatchSelection — набор остаётся пустым,
        // а UI передаёт "a" как includeId (текущую строку) при первом Ctrl-клике.
        Assert.Empty(set);

        set = BatchSelectionHelper.ApplyModifiedClick(set, currentSectionIsPinned: false, "b", false, "Ctrl",
            includeId: "a", includeSectionIsPinned: false);
        set = BatchSelectionHelper.ApplyModifiedClick(set, currentSectionIsPinned: false, "c", false, "Ctrl",
            includeId: "a", includeSectionIsPinned: false);

        var rightClick = BatchSelectionHelper.BuildRightClickSet(set, rightClickedId: "c");

        Assert.Equal(new[] { "a", "b", "c" }, rightClick.OrderBy(x => x));
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

    // ======================= Issue #326: порядок закреплённой секции по данным узла =======================

    [Fact]
    public void BuildPinnedSectionOrder_FromWrappedItems_ReturnsBasesInNodeOrder()
    {
        // Узел «Закреплённые» несёт обёртки PinnedInfobaseItem (issue #314): порядок
        // Shift-диапазона строится по данным узла, а не по контейнерам (виртуализация
        // может не реализовать контейнеры вне видимой области — обход вернул бы пустой
        // порядок, и диапазон уходил в общий список, issue #326).
        var first = new Infobase { Id = "pinned-1", Name = "Первая" };
        var second = new Infobase { Id = "pinned-2", Name = "Вторая" };
        var third = new Infobase { Id = "pinned-3", Name = "Третья" };

        var order = BatchSelectionHelper.BuildPinnedSectionOrder(new object[]
        {
            new PinnedInfobaseItem(first),
            new PinnedInfobaseItem(second),
            new PinnedInfobaseItem(third)
        });

        Assert.Equal(new[] { first, second, third }, order);
        Assert.Equal(new[] { "pinned-1", "pinned-2", "pinned-3" }, order.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void BuildPinnedSectionOrder_MixedRawAndWrapped_DeduplicatesSameBase()
    {
        // Допустимый резерв: узел может содержать и обёртки, и голые базы; одна и та же
        // база не должна повторяться в порядке секции.
        var only = new Infobase { Id = "pinned-1" };
        var other = new Infobase { Id = "pinned-2" };

        var order = BatchSelectionHelper.BuildPinnedSectionOrder(new object[]
        {
            new PinnedInfobaseItem(only),
            only, // дубль той же базы — пропускается
            new PinnedInfobaseItem(other)
        });

        Assert.Equal(new[] { only, other }, order);
    }

    [Fact]
    public void BuildPinnedSectionOrder_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(BatchSelectionHelper.BuildPinnedSectionOrder(null!));
        Assert.Empty(BatchSelectionHelper.BuildPinnedSectionOrder(Array.Empty<object>()));
    }

    [Fact]
    public void Unwrap_PinnedWrapper_ReturnsBase()
    {
        var ib = new Infobase { Id = "pinned-1" };
        var wrapped = new PinnedInfobaseItem(ib);

        Assert.Same(ib, BatchSelectionHelper.Unwrap(wrapped));
        Assert.Same(ib, BatchSelectionHelper.Unwrap(ib));
        Assert.Null(BatchSelectionHelper.Unwrap(null));
        Assert.Null(BatchSelectionHelper.Unwrap("не база"));
    }

    [Fact]
    public void ApplyModifiedClick_ShiftInPinnedSection_LongerRegularList_OnlyPinnedRange()
    {
        // Сценарий 7OH (issue #326): обычный список заметно длиннее закреплённого.
        // Shift-клик от одной закреплённой базы до другой должен выделить ТОЛЬКО
        // закреплённый диапазон, а не «полсписка обычного» (порядок передаётся уже
        // в пределах секции — обычные строки в него не попадают).
        var pinnedOrder = new[] { "pinned-1", "pinned-2", "pinned-3", "pinned-4" };

        var result = BatchSelectionHelper.ApplyModifiedClick(
            currentIds: Array.Empty<string>(),
            currentSectionIsPinned: false,
            targetId: "pinned-4",
            targetSectionIsPinned: true,
            modifier: "Shift",
            visibleOrder: pinnedOrder,
            anchorId: "pinned-2");

        Assert.Equal(new[] { "pinned-2", "pinned-3", "pinned-4" }, result.OrderBy(x => x));
        Assert.All(result, id => Assert.StartsWith("pinned-", id, StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyModifiedClick_ShiftInPinnedSection_EmptyOrder_SelectsOnlyTarget()
    {
        // Пустой порядок секции (например, узел «Закреплённые» отсутствует): Shift-клик
        // НЕ должен проваливаться в резерв по общему списку — выбирается только цель (#326).
        var result = BatchSelectionHelper.ApplyModifiedClick(
            currentIds: Array.Empty<string>(),
            currentSectionIsPinned: false,
            targetId: "pinned-1",
            targetSectionIsPinned: true,
            modifier: "Shift",
            visibleOrder: Array.Empty<string>(),
            anchorId: "pinned-1");

        Assert.Equal(new[] { "pinned-1" }, result.OrderBy(x => x));
    }

    [Fact]
    public void ApplyModifiedClick_CtrlClicksInsidePinnedSection_DoNotTouchRegularList()
    {
        // Ctrl-клики по закреплённым строкам остаются в секции закреплений:
        // обычные строки в набор не попадают ни первым кликом (текущая из той же
        // секции добавляется вместе с целью — issue #313), ни последующими (#326).
        var set = new HashSet<string>(StringComparer.Ordinal);
        set = BatchSelectionHelper.ApplyModifiedClick(
            set, currentSectionIsPinned: false, "pinned-1", targetSectionIsPinned: true, "Ctrl",
            includeId: "pinned-0", includeSectionIsPinned: true);
        set = BatchSelectionHelper.ApplyModifiedClick(
            set, currentSectionIsPinned: true, "pinned-2", targetSectionIsPinned: true, "Ctrl");

        Assert.Equal(new[] { "pinned-0", "pinned-1", "pinned-2" }, set.OrderBy(x => x));
        Assert.All(set, id => Assert.StartsWith("pinned-", id, StringComparison.Ordinal));
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

    // ======================= Issue #340: клик, закрывший контекстное меню =======================

    [Fact]
    public void DecideAfterMenuCloseClick_TargetAlreadyCurrentBatchEmpty_DoesNothing()
    {
        // Штатная обработка уже применила клик: цель — текущая база, набор снят.
        // Повторное применение только «перевыбрало» бы строку — ничего не делаем.
        var action = BatchSelectionHelper.DecideAfterMenuCloseClick(
            isTargetRowCurrent: true, hasBatchSelection: false);

        Assert.Equal(BatchSelectionHelper.TreeMenuCloseClickAction.None, action);
    }

    [Fact]
    public void DecideAfterMenuCloseClick_TargetAlreadyCurrentBatchHangs_ClearsBatchOnly()
    {
        // Цель уже текущая, но мультивыделение ещё висит (клик дошёл до дерева,
        // а снятие набора — нет): снимаем только набор, выбор не трогаем.
        var action = BatchSelectionHelper.DecideAfterMenuCloseClick(
            isTargetRowCurrent: true, hasBatchSelection: true);

        Assert.Equal(BatchSelectionHelper.TreeMenuCloseClickAction.ClearBatchOnly, action);
    }

    [Fact]
    public void DecideAfterMenuCloseClick_TargetNotCurrentWithBatch_SelectsTargetAndClearsBatch()
    {
        // Клик по строке «мимо» мультивыделения: строка под курсором становится
        // ЕДИНСТВЕННОЙ текущей, набор снимается (основной сценарий issue #340).
        var action = BatchSelectionHelper.DecideAfterMenuCloseClick(
            isTargetRowCurrent: false, hasBatchSelection: true);

        Assert.Equal(BatchSelectionHelper.TreeMenuCloseClickAction.SelectTargetAndClearBatch, action);
    }

    [Fact]
    public void DecideAfterMenuCloseClick_TargetNotCurrentWithoutBatch_SelectsTarget()
    {
        // Мультивыделения нет, но штатная обработка клика не выполнилась (клик съеден
        // попапом): применяем выбор цели сами.
        var action = BatchSelectionHelper.DecideAfterMenuCloseClick(
            isTargetRowCurrent: false, hasBatchSelection: false);

        Assert.Equal(BatchSelectionHelper.TreeMenuCloseClickAction.SelectTargetAndClearBatch, action);
    }
}