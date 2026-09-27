using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика «догоняющего выполнения» пропущенных заданий по расписанию (функция №7):
/// определяет, было ли задание пропущено (приложение не работало в плановый момент) и
/// должно ли оно быть выполнено сразу при следующем старте. Вынесена в отдельный класс,
/// чтобы покрыть юнит-тестами без зависимостей от UI и файловой системы, как
/// <see cref="ScheduleCalculator"/>. Расчёт плановых моментов опирается на те же поля
/// расписания (время «HH:mm», дни недели), что и <see cref="ScheduleCalculator"/>.
/// </summary>
public static class ScheduleCatchUpCalculator
{
    /// <summary>
    /// Максимальный возраст пропуска в сутках: более старые пропуски не догоняются,
    /// чтобы после долгого отсутствия приложение не выполняло «древние» задания.
    /// </summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);

    /// <summary>
    /// Определяет, пропущено ли задание и подлежит ли оно догоняющему выполнению.
    /// Задание считается пропущенным, если: оно включено, тип допускает догоняние
    /// (кроме <see cref="ScheduledTaskKind.UpdateApp"/> — обновление найдёт себя при
    /// очередной авто-проверке), существует плановый момент (последний наступивший
    /// до <paramref name="now"/>) позже последнего фактического запуска, и с этого
    /// момента прошло не больше <paramref name="maxAge"/> (по умолчанию 7 суток).
    /// </summary>
    /// <param name="task">Задание по расписанию.</param>
    /// <param name="now">Текущее локальное время (как в планировщике).</param>
    /// <param name="maxAge">Максимальный возраст пропуска; null — <see cref="DefaultMaxAge"/>.</param>
    public static bool IsMissed(ScheduledTask task, DateTime now, TimeSpan? maxAge = null)
    {
        if (task is null || !task.Enabled)
            return false;
        // Обновление приложения не догоняется: найденное обновление установится
        // при следующей автоматической проверке, а самообновление с перезапуском
        // в момент старта приложения нежелательно.
        if (task.Kind == ScheduledTaskKind.UpdateApp)
            return false;

        var lastRun = LastRunLocal(task);
        if (lastRun.HasValue && lastRun.Value >= now)
            return false;

        var limit = maxAge ?? DefaultMaxAge;
        var maxDays = Math.Max(0, (int)limit.TotalDays);
        var time = task.GetTime();

        // Идём от сегодняшнего дня назад в пределах порога давности и ищем плановый
        // момент, который уже наступил, но позже последнего фактического запуска.
        // Первый же такой момент означает: задание пропущено (догоняем один раз).
        for (var i = 0; i <= maxDays; i++)
        {
            var day = now.Date.AddDays(-i);
            if (!task.MatchesDay(day.DayOfWeek))
                continue;

            var planned = day.Add(time);
            if (planned > now)
                continue; // плановое время ещё не наступило
            if (lastRun.HasValue && planned <= lastRun.Value)
                continue; // задание уже выполнялось после планового момента

            return true;
        }

        return false;
    }

    /// <summary>
    /// Момент последнего фактического запуска в локальном времени: предпочитается
    /// UTC-метка <see cref="ScheduledTask.LastRunUtc"/> (переведённая в локальное время),
    /// при её отсутствии (старые сохранённые файлы) — устаревшее локальное поле
    /// <see cref="ScheduledTask.LastRunAt"/>. null — задание ещё ни разу не выполнялось.
    /// </summary>
    public static DateTime? LastRunLocal(ScheduledTask task)
    {
        if (task is null)
            return null;
        if (task.LastRunUtc.HasValue)
            return task.LastRunUtc.Value.ToLocalTime();
        return task.LastRunAt;
    }
}