using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Фильтрация списка заданий по расписанию (issue #372, п. 1–2): отбор по типу
/// действия и поиск по всем полям (имя, тип, расписание, результат, Id) без учёта
/// регистра. Чистая логика без UI — переиспользуется WPF- и Avalonia-окнами и
/// покрывается юнит-тестами.
/// </summary>
public static class ScheduledTaskListFilter
{
    /// <summary>
    /// Применяет фильтр: при <paramref name="kind"/> != null — только задания этого
    /// типа; непустой <paramref name="search"/> — подстрока хотя бы в одном поле
    /// (name, KindText, ScheduleSummary, LastRunSummary, Id).
    /// </summary>
    public static List<ScheduledTaskItemViewModel> Apply(
        IEnumerable<ScheduledTaskItemViewModel>? items,
        ScheduledTaskKind? kind,
        string? search)
    {
        var query = (search ?? string.Empty).Trim();
        return (items ?? Enumerable.Empty<ScheduledTaskItemViewModel>())
            .Where(i => kind is null || i.Task.Kind == kind)
            .Where(i => query.Length == 0 || Matches(i, query))
            .ToList();
    }

    private static bool Matches(ScheduledTaskItemViewModel item, string query)
    {
        return Contains(item.Name, query)
            || Contains(item.KindText, query)
            || Contains(item.ScheduleSummary, query)
            || Contains(item.LastRunSummary, query)
            || Contains(item.Id, query);
    }

    private static bool Contains(string? value, string query)
        => value is not null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
}