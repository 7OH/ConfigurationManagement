using Configuration_Management.ViewModels;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика мультивыделения «для выделенных» (0.3.9.90): правило секций
/// дерева («Закреплённые» vs обычный список, issue #326) и построение набора
/// при правом клике (issue #313). Без платформенных зависимостей — используется
/// обеими платформами (Windows/WPF и Linux/Avalonia).
/// </summary>
public static class BatchSelectionHelper
{
    /// <summary>
    /// Строка узла «Закреплённые» несёт обёртку <see cref="PinnedInfobaseItem"/>
    /// (уникальные данные строки, issues #301/#314), обычная строка базы — саму
    /// модель <see cref="Infobase"/>. По типу данных контейнера определяется
    /// принадлежность строки к секции.
    /// </summary>
    public static bool IsPinnedSection(object? rowData) => rowData is PinnedInfobaseItem;

    /// <summary>
    /// Применяет модифицированный (Ctrl/Shift) клик к набору мультивыделения с
    /// правилом секций (#326): клик в другой секции («Закреплённые» vs обычный
    /// список) не смешивает строки — текущий набор очищается, и клик начинает
    /// новый набор в своей секции. Внутри секции поведение прежнее: Ctrl —
    /// точечное переключение, Shift — диапазон от якоря до цели по видимому
    /// порядку (порядок должен быть построен в пределах той же секции).
    /// Возвращает новый набор идентификаторов баз.
    /// </summary>
    /// <param name="currentIds">Текущий набор мультивыделения.</param>
    /// <param name="currentSectionIsPinned">
    /// Секция текущего набора: true — «Закреплённые». Значение игнорируется при пустом наборе.
    /// </param>
    /// <param name="targetId">Идентификатор базы под кликом.</param>
    /// <param name="targetSectionIsPinned">Секция строки под кликом (из данных контейнера).</param>
    /// <param name="modifier">"Ctrl" или "Shift" (регистр не важен).</param>
    /// <param name="visibleOrder">Видимый порядок строк в пределах секции цели (для Shift).</param>
    /// <param name="anchorId">Якорь диапазона (обычно последняя строка, выбранная без Ctrl).</param>
    public static HashSet<string> ApplyModifiedClick(
        IReadOnlyCollection<string> currentIds,
        bool currentSectionIsPinned,
        string? targetId,
        bool targetSectionIsPinned,
        string? modifier,
        IReadOnlyList<string>? visibleOrder = null,
        string? anchorId = null)
    {
        var result = new HashSet<string>(currentIds ?? Array.Empty<string>(), StringComparer.Ordinal);

        // Клик в другой секции не смешивает строки (#326): начинаем новый набор.
        if (result.Count > 0 && currentSectionIsPinned != targetSectionIsPinned)
            result.Clear();

        if (string.Equals(modifier, "Shift", StringComparison.OrdinalIgnoreCase))
        {
            // Диапазон строится только если якорь и цель есть в порядке секции;
            // иначе — только цель (как проводник при Shift-клике без активного якоря).
            if (targetId is null)
                return result;
            if (visibleOrder is null || visibleOrder.Count == 0)
            {
                result.Add(targetId);
                return result;
            }

            var iFrom = anchorId is null ? -1 : IndexOf(visibleOrder, anchorId);
            var iTo = IndexOf(visibleOrder, targetId);
            if (iFrom < 0 || iTo < 0)
            {
                result.Add(targetId);
                return result;
            }

            var lo = Math.Min(iFrom, iTo);
            var hi = Math.Max(iFrom, iTo);
            for (var i = lo; i <= hi; i++)
                result.Add(visibleOrder[i]);
            return result;
        }

        // Ctrl: точечное переключение (toggle).
        if (targetId is null)
            return result;
        if (!result.Add(targetId))
            result.Remove(targetId);
        return result;
    }

    /// <summary>
    /// Набор «для выделенных» на момент правого клика (issue #313). Правый клик
    /// набор НЕ меняет: строка под курсором входит в него только после
    /// Ctrl/Shift-клика по ней, а «бывшая текущая» (выбранная без Ctrl) не
    /// добавляется и toggle не выполняется. Метод фиксирует семантику и
    /// возвращает фактический набор; его используют тесты регрессии сценария.
    /// </summary>
    public static IReadOnlyCollection<string> BuildRightClickSet(
        IReadOnlyCollection<string> batchIds, string? rightClickedId)
    {
        // Параметр rightClickedId принимается намеренно: он документирует, что
        // строка под правым кликом НЕ добавляется и не снимается, даже если она
        // была «просто текущей» (без Ctrl).
        _ = rightClickedId;
        return new HashSet<string>(batchIds ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
            if (string.Equals(list[i], value, StringComparison.Ordinal))
                return i;
        return -1;
    }
}