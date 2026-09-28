namespace Configuration_Management.Models;

/// <summary>
/// Статус индикатора «база сейчас запущена» (точка у имени базы, issue #310).
/// Точка скрыта, пока база не запущена; запущенная база раскрашивается по
/// отклику её процесса: зелёная (отвечает), оранжевая (завис — не отвечает
/// при первом опросе), красная (не отвечает несколько опросов подряд).
/// </summary>
public enum RunningDotStatus
{
    /// <summary>База не запущена — точка скрыта.</summary>
    NotRunning,

    /// <summary>Процесс базы отвечает — зелёная точка (#22C55E).</summary>
    Responding,

    /// <summary>Процесс не отвечает при первом опросе — оранжевая точка (#F59E0B).</summary>
    Hung,

    /// <summary>Процесс не отвечает несколько опросов подряд — красная точка (#EF4444).</summary>
    Critical
}

/// <summary>
/// Чистая логика классификации статуса точки «база запущена». Не зависит от
/// платформы и UI — покрыта юнит-тестами.
/// </summary>
public static class RunningDotStatusClassifier
{
    /// <summary>
    /// Определяет статус по флагам запуска и отклика процесса.
    /// </summary>
    /// <param name="isRunning">Процесс базы обнаружен в списке процессов 1С.</param>
    /// <param name="isNotResponding">Обнаруженный процесс не отвечает (Windows:
    /// Process.Responding=false; Linux: state 'D' в /proc/<pid>/stat).</param>
    /// <param name="consecutiveNotResponding">Подряд идущие опросы, где процесс
    /// не отвечал (0 — процесс отвечает или база не запущена).</param>
    public static RunningDotStatus Classify(bool isRunning, bool isNotResponding, int consecutiveNotResponding)
    {
        if (!isRunning)
            return RunningDotStatus.NotRunning;
        if (!isNotResponding)
            return RunningDotStatus.Responding;
        return consecutiveNotResponding >= 2 ? RunningDotStatus.Critical : RunningDotStatus.Hung;
    }
}