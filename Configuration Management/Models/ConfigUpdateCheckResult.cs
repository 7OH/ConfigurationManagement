namespace Configuration_Management.Models;

/// <summary>
/// Статус результата проверки наличия обновлений конфигурации.
/// Честное описание итога сетевой проверки каталога релизов 1С.
/// </summary>
public enum ConfigUpdateStatus
{
    /// <summary>Проверка ещё не выполнялась либо результат неопределён.</summary>
    Unknown,

    /// <summary>Каталог доступен, последняя версия не новее текущей.</summary>
    UpToDate,

    /// <summary>Обнаружен релиз новее текущей версии.</summary>
    NewerAvailable,

    /// <summary>Каталог доступен, но точную последнюю версию распарсить не удалось.</summary>
    Unavailable,

    /// <summary>Ошибка сети/HTTP (в т.ч. 404 — каталог недоступен или неверная ссылка).</summary>
    Failed,
}

/// <summary>
/// Результат проверки наличия обновлений одной конфигурации / информационной базы:
/// текущая версия, последняя версия с сайта 1С, адрес каталога релизов, признак
/// наличия нового релиза и статус/текст ошибки.
/// </summary>
public class ConfigUpdateCheckResult
{
    /// <summary>Имя конфигурации, для которой выполнялась проверка.</summary>
    public string ConfigName { get; set; } = string.Empty;

    /// <summary>Текущая версия конфигурации (например «3.0.142.32»).</summary>
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>Последняя версия, обнаруженная в каталоге релизов (пусто — не определена).</summary>
    public string LatestVersion { get; set; } = string.Empty;

    /// <summary>Адрес каталога релизов (URL, по которому выполнялась проверка).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Итоговый статус проверки.</summary>
    public ConfigUpdateStatus Status { get; set; } = ConfigUpdateStatus.Unknown;

    /// <summary>Текст ошибки (при <see cref="Status"/> == <see cref="ConfigUpdateStatus.Failed"/>).</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>True — обнаружен релиз новее текущей версии (можно предлагать загрузку).</summary>
    public bool HasNewer => Status == ConfigUpdateStatus.NewerAvailable;

    /// <summary>True — проверка завершилась успешно (без ошибки сети/404).</summary>
    public bool Succeeded => Status is ConfigUpdateStatus.UpToDate
        or ConfigUpdateStatus.NewerAvailable
        or ConfigUpdateStatus.Unavailable;
}