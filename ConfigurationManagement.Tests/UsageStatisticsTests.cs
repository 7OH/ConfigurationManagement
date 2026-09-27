using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты агрегатора статистики использования баз (<see cref="UsageStatisticsAggregator"/>):
/// число запусков, первый/последний запуск, дней с последнего запуска, сортировка,
/// сводка и распределение по дням недели.
/// </summary>
public sealed class UsageStatisticsTests
{
    // Опорные даты 2026 года: 01.01 — четверг (индекс 3), 05.01 — понедельник (0),
    // 06.01 — вторник (1), 07.01 — среда (2).
    private static readonly DateTime Mon = new(2026, 1, 5, 10, 0, 0);
    private static readonly DateTime Tue = new(2026, 1, 6, 11, 30, 0);
    private static readonly DateTime Wed = new(2026, 1, 7, 9, 15, 0);
    private static readonly DateTime Thu = new(2026, 1, 1, 12, 0, 0);
    private static readonly DateTime Fri = new(2026, 1, 2, 13, 45, 0);
    private static readonly DateTime Sat = new(2026, 1, 3, 14, 20, 0);
    private static readonly DateTime Sun = new(2026, 1, 4, 15, 5, 0);

    private static readonly DateTime Now = new(2026, 9, 27, 21, 0, 0);

    private static (string Name, IReadOnlyList<LaunchHistoryEntry> History) S(
        string name, params DateTime[] timestamps) =>
        (name, timestamps.Select(t => new LaunchHistoryEntry { Timestamp = t }).ToList());

    [Fact]
    public void Aggregate_NoBases_ReturnsEmptyRowsAndZeroSummary()
    {
        var result = UsageStatisticsAggregator.Aggregate(Array.Empty<(string, IReadOnlyList<LaunchHistoryEntry>)>(), Now);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.Summary.TotalBases);
        Assert.Equal(0, result.Summary.UsedBases);
        Assert.Equal(0, result.Summary.TotalLaunches);
        Assert.Equal(0, result.Summary.AveragePerBase);
        Assert.Null(result.Summary.TopBaseName);
        Assert.All(result.DayOfWeekCounts, c => Assert.Equal(0, c));
    }

    [Fact]
    public void Aggregate_BaseWithoutHistory_HasZeroLaunchesAndNoDates()
    {
        var result = UsageStatisticsAggregator.Aggregate(
            new[] { S("Бухгалтерия") }, Now);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Бухгалтерия", row.BaseName);
        Assert.Equal(0, row.LaunchCount);
        Assert.False(row.HasHistory);
        Assert.Null(row.FirstLaunch);
        Assert.Null(row.LastLaunch);
        Assert.Null(row.DaysSinceLastLaunch);

        Assert.Equal(1, result.Summary.TotalBases);
        Assert.Equal(0, result.Summary.UsedBases);
        Assert.Equal(0, result.Summary.TotalLaunches);
        Assert.Null(result.Summary.TopBaseName);
    }

    [Fact]
    public void Aggregate_NullHistory_TreatedAsEmpty()
    {
        var result = UsageStatisticsAggregator.Aggregate(
            new[] { ("База", (IReadOnlyList<LaunchHistoryEntry>)null!) }, Now);

        var row = Assert.Single(result.Rows);
        Assert.Equal(0, row.LaunchCount);
        Assert.False(row.HasHistory);
    }

    [Fact]
    public void Aggregate_SingleBase_CountsAllLaunchesAndDates()
    {
        var result = UsageStatisticsAggregator.Aggregate(
            new[] { S("Управление торговлей", Mon, Tue, Wed) }, Now);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Управление торговлей", row.BaseName);
        Assert.Equal(3, row.LaunchCount);
        Assert.Equal(Wed, row.LastLaunch);
        Assert.Equal(Mon, row.FirstLaunch);
        Assert.Equal((int)(Now.Date - Wed.Date).TotalDays, row.DaysSinceLastLaunch);

        Assert.Equal(1, result.Summary.UsedBases);
        Assert.Equal(3, result.Summary.TotalLaunches);
        Assert.Equal(3, result.Summary.AveragePerBase);
        Assert.Equal("Управление торговлей", result.Summary.TopBaseName);
        Assert.Equal(3, result.Summary.TopBaseLaunches);
    }

    [Fact]
    public void Aggregate_MultipleBases_SortedByLaunchCountDescending()
    {
        var result = UsageStatisticsAggregator.Aggregate(
            new[]
            {
                S("Частая", Mon, Tue, Wed, Thu),
                S("Редкая", Mon),
                S("Средняя", Mon, Tue)
            }, Now);

        Assert.Collection(result.Rows,
            r => Assert.Equal("Частая", r.BaseName),
            r => Assert.Equal("Средняя", r.BaseName),
            r => Assert.Equal("Редкая", r.BaseName));
    }

    [Fact]
    public void Aggregate_EqualLaunchCount_BreaksTieByName()
    {
        var result = UsageStatisticsAggregator.Aggregate(
            new[]
            {
                S("Зета", Mon),
                S("Альфа", Tue),
                S("Бета", Wed)
            }, Now);

        Assert.Collection(result.Rows,
            r => Assert.Equal("Альфа", r.BaseName),
            r => Assert.Equal("Бета", r.BaseName),
            r => Assert.Equal("Зета", r.BaseName));
    }

    [Fact]
    public void Aggregate_Summary_CountsUsedBasesAndTopBase()
    {
        var result = UsageStatisticsAggregator.Aggregate(
            new[]
            {
                S("Лидер", Mon, Tue, Wed, Thu, Fri),
                S("Средняя", Mon, Tue),
                S("Пустая")
            }, Now);

        var summary = result.Summary;
        Assert.Equal(3, summary.TotalBases);
        Assert.Equal(2, summary.UsedBases);
        Assert.Equal(7, summary.TotalLaunches);
        Assert.Equal(7d / 3, summary.AveragePerBase, 10);
        Assert.Equal("Лидер", summary.TopBaseName);
        Assert.Equal(5, summary.TopBaseLaunches);
    }

    [Fact]
    public void Aggregate_DayOfWeek_DistributesMondayFirstSundayLast()
    {
        var result = UsageStatisticsAggregator.Aggregate(
            new[] { S("Все дни", Mon, Tue, Wed, Thu, Fri, Sat, Sun) }, Now);

        // Индекс 0 = понедельник … 6 = воскресенье.
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 1, 1 }, result.DayOfWeekCounts);
    }

    [Fact]
    public void Aggregate_DayOfWeek_MultipleLaunchesSameDay()
    {
        var result = UsageStatisticsAggregator.Aggregate(
            new[] { S("База", Mon, Mon, Wed, Sun, Sun, Sun) }, Now);

        Assert.Equal(2, result.DayOfWeekCounts[0]); // понедельник
        Assert.Equal(1, result.DayOfWeekCounts[2]); // среда
        Assert.Equal(3, result.DayOfWeekCounts[6]); // воскресенье
    }

    [Fact]
    public void Aggregate_DaysSince_TodayIsZeroAndOlderIsPositive()
    {
        var today = new DateTime(2026, 9, 27, 8, 0, 0);
        var yesterday = new DateTime(2026, 9, 26, 18, 0, 0);
        var longAgo = new DateTime(2026, 1, 5, 10, 0, 0);

        var result = UsageStatisticsAggregator.Aggregate(
            new[] { S("Сегодняшняя", today), S("Вчерашняя", yesterday), S("Старая", longAgo) }, Now);

        // Все базы имеют по одному запуску — порядок строк определяется именем;
        // обращаемся по имени, чтобы не зависеть от порядка сортировки.
        var byName = result.Rows.ToDictionary(r => r.BaseName);
        Assert.Equal(0, byName["Сегодняшняя"].DaysSinceLastLaunch);
        Assert.Equal(1, byName["Вчерашняя"].DaysSinceLastLaunch);
        Assert.Equal((int)(Now.Date - longAgo.Date).TotalDays, byName["Старая"].DaysSinceLastLaunch);
    }

    [Fact]
    public void Aggregate_ZeroTimestampEntries_Ignored()
    {
        var result = UsageStatisticsAggregator.Aggregate(
            new[]
            {
                ("База", (IReadOnlyList<LaunchHistoryEntry>)new List<LaunchHistoryEntry>
                {
                    new LaunchHistoryEntry { Timestamp = default },
                    new LaunchHistoryEntry { Timestamp = Mon },
                    null!
                })
            }, Now);

        var row = Assert.Single(result.Rows);
        Assert.Equal(1, row.LaunchCount);
        Assert.Equal(Mon, row.FirstLaunch);
        Assert.Equal(Mon, row.LastLaunch);
    }
}