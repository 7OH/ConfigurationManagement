using System.Linq;

namespace Configuration_Management.Models;

/// <summary>
/// Тип задания по расписанию (issue #286).
/// </summary>
public enum ScheduledTaskKind
{
    /// <summary>Резервная копия ИБ по существующему сценарию резервирования.</summary>
    Backup,

    /// <summary>Обновление конфигурации ИБ из файла .cf (/LoadCfg + /UpdateDBCfg).</summary>
    UpdateConfig,

    /// <summary>Связка «Копия → обновление»: сначала резервная копия, затем обновление конфигурации.</summary>
    BackupThenUpdateConfig,

    /// <summary>Проверка целостности файловой ИБ (<c>chdbfl</c>), тихий запуск с ожиданием (0.3.9.87).</summary>
    CheckIntegrity,

    /// <summary>Обновление самого приложения через GitHub Releases (без диалога).</summary>
    UpdateApp,

    /// <summary>
    /// Проверка наличия обновления технологической платформы 1С в каталоге
    /// <c>releases.1c.ru</c> по выражению проверки («8.5.*», «8.3.27.*») — без
    /// установки, только уведомление о найденных версиях (issue #366).
    /// </summary>
    PlatformUpdateCheck,

    /// <summary>
    /// Проверка наличия обновления конфигурации 1С в каталоге
    /// <c>releases.1c.ru</c> для выбранной конфигурации: максимальная установленная
    /// версия по списку баз сравнивается с доступными релизами — без установки,
    /// только уведомление о версиях выше установленной (issue #367).
    /// </summary>
    ConfigurationUpdateCheck
}

/// <summary>
/// Канал уведомления заданий-проверок («Проверить наличие обновления платформы» —
/// issue #366, «Проверить наличие обновления конфигурации» — issue #367):
/// куда выводится сводка о найденных версиях.
/// </summary>
public enum PlatformUpdateNotifyChannel
{
    /// <summary>Сообщение в строке состояния главного окна приложения.</summary>
    MainWindow,

    /// <summary>Системное уведомление ОС (balloon трея на Windows, notify-send на Linux).</summary>
    SystemNotifications
}

/// <summary>
/// Задание по расписанию: тип действия, целевая ИБ, время и дни недели запуска.
/// Хранится в отдельном JSON-файле (см. Services.ScheduledTaskStore).
/// Выполняется фоновым планировщиком, пока приложение запущено.
/// </summary>
public class ScheduledTask
{
    /// <summary>Идентификатор задания (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Наименование задания.</summary>
    public string Name { get; set; } = "";

    /// <summary>Тип выполняемого действия.</summary>
    public ScheduledTaskKind Kind { get; set; } = ScheduledTaskKind.Backup;

    /// <summary>Включено ли задание (выключенные пропускаются планировщиком).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Время запуска в формате «HH:mm». Невалидное значение трактуется как 00:00.
    /// </summary>
    public string Time { get; set; } = "02:00";

    /// <summary>
    /// Дни недели выполнения. Пустой список означает «ежедневно».
    /// </summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    /// <summary>Идентификатор сценария резервирования (для Backup / BackupThenUpdateConfig).</summary>
    public string? ScenarioId { get; set; }

    /// <summary>Идентификатор целевой ИБ (для Backup / UpdateConfig / BackupThenUpdateConfig).</summary>
    public string? InfobaseId { get; set; }

    /// <summary>Путь к файлу .cf с новой конфигурацией (для UpdateConfig / BackupThenUpdateConfig).</summary>
    public string? ConfigFilePath { get; set; }

    /// <summary>
    /// Выражение проверки платформы (для PlatformUpdateCheck, issue #366): сегменты —
    /// числа или «*», например «8.5.*» или «8.3.27.*». Пусто/невалидно — задание
    /// завершается ошибкой валидации.
    /// </summary>
    public string? PlatformCheckExpression { get; set; }

    /// <summary>
    /// Учитывать ли тестовые (предрелизные) сборки платформы при проверке
    /// (для PlatformUpdateCheck, issue #366). По умолчанию false.
    /// </summary>
    public bool PlatformCheckIncludeTestVersions { get; set; }

    /// <summary>
    /// Канал уведомления о результате проверки платформы (для PlatformUpdateCheck,
    /// issue #366). По умолчанию — строка состояния главного окна.
    /// </summary>
    public PlatformUpdateNotifyChannel PlatformNotifyChannel { get; set; } = PlatformUpdateNotifyChannel.MainWindow;

    /// <summary>
    /// Проверяемая конфигурация (для ConfigurationUpdateCheck, issue #367): имя
    /// конфигурации из списка баз (или введённое вручную). Пусто — задание
    /// завершается ошибкой валидации.
    /// </summary>
    public string? ConfigCheckConfiguration { get; set; }

    /// <summary>
    /// Канал уведомления о результате проверки конфигурации (для
    /// ConfigurationUpdateCheck, issue #367): общий enum каналов из issue #366.
    /// По умолчанию — строка состояния главного окна.
    /// </summary>
    public PlatformUpdateNotifyChannel ConfigCheckNotifyChannel { get; set; } = PlatformUpdateNotifyChannel.MainWindow;

    /// <summary>
    /// Момент последнего запуска задания (null — ещё не выполнялось).
    /// Локальное время, используется для отображения в списке заданий.
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// Момент последнего запуска задания в UTC (функция №7 «догоняющее выполнение»).
    /// Дублирует <see cref="LastRunAt"/> в UTC, чтобы сравнивать с плановым временем
    /// без зависимости от часового пояса машины. null — ещё не выполнялось; у старых
    /// сохранённых заданий поле отсутствует, и догоняние опирается на LastRunAt.
    /// </summary>
    public DateTime? LastRunUtc { get; set; }

    /// <summary>Успешно ли завершился последний запуск.</summary>
    public bool LastRunSuccess { get; set; }

    /// <summary>Сообщение о результате последнего запуска (путь/ошибка).</summary>
    public string? LastRunMessage { get; set; }

    /// <summary>Подходит ли день недели под расписание (пустой список — любой день).</summary>
    public bool MatchesDay(DayOfWeek day) => DaysOfWeek.Count == 0 || DaysOfWeek.Contains(day);

    /// <summary>Время запуска как TimeSpan («HH:mm»), при ошибке парсинга — 00:00.</summary>
    public TimeSpan GetTime() => TimeSpan.TryParse(Time, out var t) ? t : TimeSpan.Zero;

    /// <summary>
    /// Создаёт копию задания с новым <see cref="Id"/> (issue #370): копируются все
    /// настройки задания, история последнего запуска (<see cref="LastRunAt"/>,
    /// <see cref="LastRunUtc"/>, <see cref="LastRunSuccess"/>,
    /// <see cref="LastRunMessage"/>) сбрасывается — копия ещё не выполнялась.
    /// Имя задаётся вызывающим (например, «{оригинал} — копия»).
    /// </summary>
    public ScheduledTask CreateCopy(string name)
    {
        return new ScheduledTask
        {
            Name = name,
            Enabled = Enabled,
            Kind = Kind,
            Time = Time,
            DaysOfWeek = new List<DayOfWeek>(DaysOfWeek),
            ScenarioId = ScenarioId,
            InfobaseId = InfobaseId,
            ConfigFilePath = ConfigFilePath,
            PlatformCheckExpression = PlatformCheckExpression,
            PlatformCheckIncludeTestVersions = PlatformCheckIncludeTestVersions,
            PlatformNotifyChannel = PlatformNotifyChannel,
            ConfigCheckConfiguration = ConfigCheckConfiguration,
            ConfigCheckNotifyChannel = ConfigCheckNotifyChannel
        };
    }
}