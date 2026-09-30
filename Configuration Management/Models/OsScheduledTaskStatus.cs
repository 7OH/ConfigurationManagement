namespace Configuration_Management.Models;

/// <summary>
/// Статус задания в планировщике ОС (функция №2 «выполнение заданий по расписанию без
/// запущенного приложения»): Windows — Task Scheduler через <c>schtasks.exe</c>,
/// Linux — <c>crontab</c>.
/// <para>
/// Заполняется парсером <see cref="Services.OsScheduleMapper"/> из XML-вывода
/// <c>schtasks /Query /XML</c> либо из управляемого блока crontab. null, возвращаемый
/// <see cref="Services.OsScheduleMapper.ParseWindowsTaskStatus(string)"/>, означает, что
/// запись в планировщике отсутствует либо её невозможно разобрать.
/// </para>
/// </summary>
public sealed class OsScheduledTaskStatus
{
    /// <summary>Задание зарегистрировано в планировщике ОС.</summary>
    public bool Registered { get; set; }

    /// <summary>Включено ли задание в планировщике ОС (не совпадает с <see cref="ScheduledTask.Enabled"/>
    /// только в переходных состояниях — обычно одно и то же).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Момент последнего запуска по данным планировщика (null — ещё не запускалось).</summary>
    public DateTime? LastRunTime { get; set; }

    /// <summary>Код результата последнего запуска (Windows Task Scheduler; null — недоступен).</summary>
    public int? LastTaskResult { get; set; }

    /// <summary>Ближайший плановый запуск по данным планировщика (null — неизвестен).</summary>
    public DateTime? NextRunTime { get; set; }
}