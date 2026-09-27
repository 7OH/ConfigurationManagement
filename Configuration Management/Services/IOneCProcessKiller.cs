namespace Configuration_Management.Services;

/// <summary>
/// Завершение запущенного процесса платформы 1С по PID (инспектор процессов).
/// Windows — Process.Kill вместе с деревом потомков; Linux — kill через
/// /proc с обязательной сверкой времени старта, чтобы PID не был переиспользован.
/// Обе реализации тихо возвращают false при отказе (нет прав и т.п.).
/// </summary>
public interface IOneCProcessKiller
{
    /// <summary>
    /// Завершает процесс 1С.
    /// </summary>
    /// <param name="pid">Идентификатор процесса.</param>
    /// <param name="startTimeToken">
    /// Сырое значение времени старта из опроса (Linux: /proc/<pid>/stat);
    /// на Windows null. Если токен задан и не совпадает с текущим — процесс уже
    /// завершился, а PID переиспользован другим процессом: тогда завершение не
    /// выполняется и возвращается false.
    /// </param>
    /// <returns>True — процесс завершён (или уже отсутствует); false — не удалось.</returns>
    bool Kill(int pid, string? startTimeToken);
}