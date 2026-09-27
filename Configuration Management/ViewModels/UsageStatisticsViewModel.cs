using System.Collections.ObjectModel;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel окна «Статистика использования» (0.3.9.95): аналитика по истории запусков
/// всех баз — таблица (число запусков, последний/первый запуск, дней с последнего запуска),
/// фильтр-переключатель «Только используемые», сводка и распределение по дням недели.
/// Чистый .NET, обе платформы (WPF и Avalonia); окна только привязываются к коллекциям
/// и вызывают методы. Данные небольшие — агрегация выполняется синхронно.
/// </summary>
public sealed class UsageStatisticsViewModel : ViewModelBase
{
    /// <summary>Максимальная ширина текстового столбца гистограммы (в символах «█»).</summary>
    public const int MaxBarSymbols = 20;

    private static readonly string[] DayKeys =
    {
        "Stats.Monday", "Stats.Tuesday", "Stats.Wednesday",
        "Stats.Thursday", "Stats.Friday", "Stats.Saturday", "Stats.Sunday"
    };

    private readonly IReadOnlyList<Infobase> _infobases;
    private bool _usedOnly;

    /// <param name="infobases">Все базы списка (с их <see cref="Infobase.LaunchHistory"/>).</param>
    public UsageStatisticsViewModel(IEnumerable<Infobase> infobases)
    {
        _infobases = infobases?.Where(ib => ib is not null).ToList()
            ?? new List<Infobase>();
        Refresh();
    }

    /// <summary>Строки всех баз (до применения фильтра «Только используемые»).</summary>
    public ObservableCollection<UsageStatisticsRowViewModel> AllRows { get; } = new();

    /// <summary>Строки, видимые в таблице (с учётом фильтра).</summary>
    public ObservableCollection<UsageStatisticsRowViewModel> Rows { get; } = new();

    /// <summary>Строки гистограммы «Пн … Вс» (текст готов для показа: «Пн ██████ 12»).</summary>
    public ObservableCollection<string> DayOfWeekBars { get; } = new();

    /// <summary>Фильтр-переключатель «Только используемые» (запускавшиеся хотя бы раз).</summary>
    public bool UsedOnly
    {
        get => _usedOnly;
        set
        {
            if (SetProperty(ref _usedOnly, value))
                ApplyFilter();
        }
    }

    /// <summary>Сводка: всего баз, с запусками, всего запусков, в среднем на базу.</summary>
    public string SummaryText { get; private set; } = string.Empty;

    /// <summary>Самая запускаемая база (или «запусков ещё не было»).</summary>
    public string TopBaseText { get; private set; } = string.Empty;

    /// <summary>Есть ли хоть одна база в списке.</summary>
    public bool HasBases => AllRows.Count > 0;

    /// <summary>Повторная агрегация (обычно достаточно конструктора).</summary>
    public void Refresh()
    {
        var aggregate = UsageStatisticsAggregator.Aggregate(
            _infobases.Select(ib =>
                (ib.Name, (IReadOnlyList<LaunchHistoryEntry>)(ib.LaunchHistory ?? new List<LaunchHistoryEntry>()))),
            DateTime.Now);

        AllRows.Clear();
        foreach (var row in aggregate.Rows)
            AllRows.Add(new UsageStatisticsRowViewModel(row));

        BuildDayOfWeekBars(aggregate.DayOfWeekCounts);
        BuildSummary(aggregate.Summary);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in AllRows)
        {
            if (!_usedOnly || row.HasHistory)
                Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasBases));
    }

    private void BuildDayOfWeekBars(IReadOnlyList<int> counts)
    {
        DayOfWeekBars.Clear();
        var max = counts.Count == 0 ? 0 : counts.Max();
        for (var i = 0; i < DayKeys.Length; i++)
        {
            var count = i < counts.Count ? counts[i] : 0;
            var barLength = max <= 0 ? 0 : Math.Max(1, (int)Math.Round(count * (double)MaxBarSymbols / max));
            var label = LocalizationManager.T(DayKeys[i]);
            DayOfWeekBars.Add($"{label}  {new string('█', barLength)}  {count}");
        }
    }

    private void BuildSummary(UsageStatisticsSummary summary)
    {
        SummaryText = string.Format(
            LocalizationManager.T("Stats.Summary"),
            summary.TotalBases,
            summary.UsedBases,
            summary.TotalLaunches,
            summary.AveragePerBase);
        TopBaseText = string.IsNullOrEmpty(summary.TopBaseName)
            ? LocalizationManager.T("Stats.NoTopBase")
            : string.Format(
                LocalizationManager.T("Stats.TopBase"),
                summary.TopBaseName,
                summary.TopBaseLaunches);

        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(TopBaseText));
    }
}