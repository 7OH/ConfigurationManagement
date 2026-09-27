using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>Строка статистики использования одной базы (чистые данные, без локализации).</summary>
public sealed class UsageStatisticsRow
{
    /// <summary>Имя базы.</summary>
    public string BaseName { get; init; } = string.Empty;

    /// <summary>Число записей истории запусков (всех, включая служебные режимы).</summary>
    public int LaunchCount { get; init; }

    /// <summary>Самый ранний запуск из истории; null — история пуста.</summary>
    public DateTime? FirstLaunch { get; init; }

    /// <summary>Последний запуск из истории; null — история пуста.</summary>
    public DateTime? LastLaunch { get; init; }

    /// <summary>Целых дней с последнего запуска на момент расчёта; null — история пуста.</summary>
    public int? DaysSinceLastLaunch { get; init; }

    /// <summary>База запускалась хотя бы раз (есть записи истории).</summary>
    public bool HasHistory => LaunchCount > 0;
}

/// <summary>Сводка по всем базам списка.</summary>
public sealed class UsageStatisticsSummary
{
    /// <summary>Всего баз в списке.</summary>
    public int TotalBases { get; init; }

    /// <summary>Баз с хотя бы одним запуском.</summary>
    public int UsedBases { get; init; }

    /// <summary>Суммарное число запусков по всем базам.</summary>
    public int TotalLaunches { get; init; }

    /// <summary>Среднее число запусков на одну базу (на все базы списка).</summary>
    public double AveragePerBase { get; init; }

    /// <summary>Имя самой запускаемой базы; null — запусков не было ни у одной базы.</summary>
    public string? TopBaseName { get; init; }

    /// <summary>Число запусков самой запускаемой базы.</summary>
    public int TopBaseLaunches { get; init; }
}

/// <summary>Результат агрегации истории запусков в статистику использования.</summary>
public sealed class UsageStatisticsAggregate
{
    /// <summary>Строки по базам: сортировка по числу запусков (убывание), затем по имени.</summary>
    public IReadOnlyList<UsageStatisticsRow> Rows { get; init; } = Array.Empty<UsageStatisticsRow>();

    /// <summary>Сводка по всем базам.</summary>
    public UsageStatisticsSummary Summary { get; init; } = new();

    /// <summary>
    /// Распределение запусков по дням недели: 7 элементов, индекс 0 = понедельник,
    /// 6 = воскресенье.
    /// </summary>
    public IReadOnlyList<int> DayOfWeekCounts { get; init; } = new int[7];
}

/// <summary>
/// Чистая агрегация истории запусков баз (<see cref="Infobase.LaunchHistory"/>) в статистику
/// использования (0.3.9.95): строки по базам (число запусков, первый/последний запуск, дней
/// с последнего запуска), сводка и распределение по дням недели. Без платформенных
/// зависимостей и локализации — покрывается unit-тестами
/// (<c>ConfigurationManagement.Tests/UsageStatisticsTests.cs</c>).
/// </summary>
public static class UsageStatisticsAggregator
{
    /// <summary>
    /// Считает статистику по парам «имя базы + её история запусков». История суммируется
    /// полностью — каждая запись <see cref="LaunchHistoryEntry"/> учитывается отдельно.
    /// </summary>
    /// <param name="sources">Пары «имя базы — список записей истории» (без учёта LastLaunchDate).</param>
    /// <param name="now">Момент «сейчас» для расчёта дней с последнего запуска (детерминизм в тестах).</param>
    public static UsageStatisticsAggregate Aggregate(
        IEnumerable<(string BaseName, IReadOnlyList<LaunchHistoryEntry> History)> sources,
        DateTime now)
    {
        var rows = new List<UsageStatisticsRow>();
        var dayOfWeek = new int[7];
        var totalLaunches = 0;
        var usedBases = 0;

        foreach (var source in sources)
        {
            var history = source.History ?? Array.Empty<LaunchHistoryEntry>();
            var timestamps = history
                .Where(h => h is not null && h.Timestamp != default)
                .Select(h => h.Timestamp)
                .ToList();

            var count = timestamps.Count;
            DateTime? first = count > 0 ? timestamps.Min() : null;
            DateTime? last = count > 0 ? timestamps.Max() : null;
            int? daysSince = last.HasValue ? (int)(now.Date - last.Value.Date).TotalDays : null;

            foreach (var ts in timestamps)
                dayOfWeek[ToDayOfWeekIndex(ts)]++;

            if (count > 0)
                usedBases++;
            totalLaunches += count;

            rows.Add(new UsageStatisticsRow
            {
                BaseName = source.BaseName ?? string.Empty,
                LaunchCount = count,
                FirstLaunch = first,
                LastLaunch = last,
                DaysSinceLastLaunch = daysSince
            });
        }

        // Сортировка по умолчанию: по числу запусков (убывание), при равенстве — по имени.
        rows.Sort((a, b) =>
        {
            var byCount = b.LaunchCount.CompareTo(a.LaunchCount);
            return byCount != 0 ? byCount : string.CompareOrdinal(a.BaseName, b.BaseName);
        });

        UsageStatisticsSummary summary;
        if (totalLaunches == 0)
        {
            summary = new UsageStatisticsSummary
            {
                TotalBases = rows.Count,
                UsedBases = 0,
                TotalLaunches = 0,
                AveragePerBase = 0,
                TopBaseName = null,
                TopBaseLaunches = 0
            };
        }
        else
        {
            var top = rows[0];
            summary = new UsageStatisticsSummary
            {
                TotalBases = rows.Count,
                UsedBases = usedBases,
                TotalLaunches = totalLaunches,
                AveragePerBase = rows.Count == 0 ? 0 : (double)totalLaunches / rows.Count,
                TopBaseName = top.BaseName,
                TopBaseLaunches = top.LaunchCount
            };
        }

        return new UsageStatisticsAggregate
        {
            Rows = rows,
            Summary = summary,
            DayOfWeekCounts = dayOfWeek
        };
    }

    /// <summary>Преобразует DayOfWeek в индекс Пн=0 … Вс=6.</summary>
    private static int ToDayOfWeekIndex(DateTime timestamp) =>
        ((int)timestamp.DayOfWeek + 6) % 7;
}