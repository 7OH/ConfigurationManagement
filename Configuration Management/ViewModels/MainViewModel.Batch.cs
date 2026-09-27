using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Мультивыделение баз (0.3.9.90): общая логика для обеих платформ.
/// Хранит идентификаторы баз, помеченных как «для выделенных» (Ctrl/Shift-клик
/// в дереве), синхронизирует служебный флаг <see cref="Infobase.IsBatchSelected"/>
/// (фон строки вторичной выделенности) и публикует событие
/// <see cref="BatchSelectionChanged"/> для обновления блока
/// «Для выделенных (N)…» в контекстном меню.
/// <para>
/// <see cref="SelectedInfobase"/> остаётся «якорем» диапазона: последний клик без
/// Ctrl. При Shift-клике диапазон строится от него до цели по видимому порядку
/// строк (порядок передаёт UI — только он знает свёрнутые группы и вкладки).
/// </para>
/// </summary>
public partial class MainViewModel
{
    private readonly HashSet<string> _batchSelectedIds = new(StringComparer.Ordinal);

    /// <summary>
    /// Событие изменения набора «для выделенных». Поднимается после любого
    /// изменения состава; UI пересчитывает заголовок и видимость пакетного
    /// блока контекстного меню.
    /// </summary>
    public event EventHandler? BatchSelectionChanged;

    /// <summary>Идентификаторы баз в мультивыделении (для пакетных операций).</summary>
    public IReadOnlyCollection<string> BatchSelectedIds => _batchSelectedIds;

    /// <summary>Число баз в мультивыделении.</summary>
    public int BatchSelectedCount => _batchSelectedIds.Count;

    /// <summary>
    /// Базы в мультивыделении в порядке списка <see cref="Infobases"/>.
    /// Служебная выборка для пакетных операций; закреплённые базы присутствуют
    /// в списке один раз.
    /// </summary>
    public IReadOnlyList<Infobase> BatchSelectedInfobases =>
        Infobases.Where(ib => ib.Id is { Length: > 0 } && _batchSelectedIds.Contains(ib.Id)).ToList();

    /// <summary>
    /// Переключает вхождение базы в мультивыделение.
    /// </summary>
    /// <param name="ib">База под курсором.</param>
    /// <param name="modifier">
    /// Модификатор клика: "Ctrl" — точечное переключение (toggle);
    /// "Shift" — диапазон от «якоря» (<see cref="SelectedInfobase"/>) до цели.
    /// Пустая строка/null — как Ctrl (точечное переключение).
    /// </param>
    /// <param name="visibleOrder">
    /// Видимый порядок строк дерева (для Shift-диапазона). Передаёт UI.
    /// </param>
    public void ToggleBatchSelection(Infobase? ib, string? modifier, IReadOnlyList<Infobase>? visibleOrder = null)
    {
        if (ib is null)
            return;

        if (string.Equals(modifier, "Shift", StringComparison.OrdinalIgnoreCase))
        {
            SelectRange(SelectedInfobase, ib, visibleOrder);
            return;
        }

        SetBatchSelected(ib, !_batchSelectedIds.Contains(ib.Id));
        RaiseBatchSelectionChanged();
    }

    /// <summary>
    /// Выделяет диапазон баз от «якоря» до цели по видимому порядку строк.
    /// Если якорь не задан или не найден в порядке — выбирается только цель.
    /// </summary>
    /// <param name="from">Якорь (последний клик без Ctrl) или null.</param>
    /// <param name="to">Цель (база под Shift-кликом).</param>
    /// <param name="visibleOrder">Видимый порядок строк дерева (передаёт UI).</param>
    public void SelectRange(Infobase? from, Infobase? to, IReadOnlyList<Infobase>? visibleOrder = null)
    {
        if (to is null)
            return;

        var order = visibleOrder;
        if (order is null || order.Count == 0)
            order = Infobases.ToList();

        var iFrom = from is null ? -1 : IndexOfReference(order, from);
        var iTo = IndexOfReference(order, to);
        if (iFrom < 0 || iTo < 0)
        {
            // Якоря нет или одна из границ вне списка: выбираем только цель,
            // как делает проводник при Shift-клике без активного якоря.
            SetBatchSelected(to, true);
            RaiseBatchSelectionChanged();
            return;
        }

        var lo = Math.Min(iFrom, iTo);
        var hi = Math.Max(iFrom, iTo);
        for (var i = lo; i <= hi; i++)
            SetBatchSelected(order[i], true);
        RaiseBatchSelectionChanged();
    }

    /// <summary>Снимает мультивыделение со всех баз.</summary>
    public void ClearBatchSelection()
    {
        if (_batchSelectedIds.Count == 0)
            return;
        _batchSelectedIds.Clear();
        SyncBatchFlags();
        RaiseBatchSelectionChanged();
    }

    /// <summary>Есть ли хотя бы одна база в мультивыделении.</summary>
    public bool HasBatchSelection => _batchSelectedIds.Count > 0;

    private void SetBatchSelected(Infobase ib, bool selected)
    {
        if (selected)
            _batchSelectedIds.Add(ib.Id);
        else
            _batchSelectedIds.Remove(ib.Id);
        ib.IsBatchSelected = selected;
    }

    /// <summary>
    /// Приводит флаг <see cref="Infobase.IsBatchSelected"/> всех баз в соответствие
    /// с набором (используется при очистке и после внешних изменений списка).
    /// </summary>
    private void SyncBatchFlags()
    {
        foreach (var ib in Infobases)
            ib.IsBatchSelected = ib.Id is { Length: > 0 } && _batchSelectedIds.Contains(ib.Id);
    }

    private void RaiseBatchSelectionChanged()
    {
        BatchSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int IndexOfReference(IReadOnlyList<Infobase> list, Infobase target)
    {
        for (var i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], target))
                return i;
        return -1;
    }
}