using Configuration_Management.Localization;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка окна «Статистика использования» (0.3.9.95): данные одной базы с текстовыми
/// колонками, готовыми к привязке («Имя базы | Запусков | Последний запуск | Первый
/// запуск | Дней с последнего запуска»). Чистый .NET без платформенных зависимостей —
/// подключается в Linux-сборку явно.
/// </summary>
public sealed class UsageStatisticsRowViewModel
{
    /// <param name="row">Строка из агрегатора <see cref="UsageStatisticsAggregator"/>.</param>
    public UsageStatisticsRowViewModel(UsageStatisticsRow row)
    {
        Name = row.BaseName;
        LaunchCount = row.LaunchCount;
        HasHistory = row.HasHistory;
        LastLaunchText = row.LastLaunch?.ToString("dd.MM.yyyy HH:mm") ?? "—";
        FirstLaunchText = row.FirstLaunch?.ToString("dd.MM.yyyy HH:mm") ?? "—";
        DaysSinceText = FormatDaysSince(row.DaysSinceLastLaunch);
    }

    /// <summary>Имя базы.</summary>
    public string Name { get; }

    /// <summary>Число записей истории запусков.</summary>
    public int LaunchCount { get; }

    /// <summary>База запускалась хотя бы раз (отличает «0» от «нет истории»).</summary>
    public bool HasHistory { get; }

    /// <summary>Последний запуск (дата/время); «—», если история пуста.</summary>
    public string LastLaunchText { get; }

    /// <summary>Первый запуск (дата/время); «—», если история пуста.</summary>
    public string FirstLaunchText { get; }

    /// <summary>Дней с последнего запуска («Сегодня» / «N дн. назад»); «—», если история пуста.</summary>
    public string DaysSinceText { get; }

    private static string FormatDaysSince(int? days)
    {
        if (!days.HasValue)
            return "—";
        if (days.Value <= 0)
            return LocalizationManager.T("Stats.Today");
        return $"{days.Value} {LocalizationManager.T("Stats.DaysAgo")}";
    }
}