namespace Configuration_Management.Models;

/// <summary>
/// Режим копирования клиент-серверной ИБ (функция №10 «Клонирование
/// клиент-серверной ИБ», 0.3.9.100).
/// </summary>
public enum ServerCloneMode
{
    /// <summary>
    /// Полная копия: данные + конфигурация. Конвейер
    /// DumpIB (.dt) → CREATEINFOBASE (пустая база) → RestoreIB.
    /// </summary>
    FullCopy,

    /// <summary>
    /// Только конфигурация. Конвейер DumpCfg (.cf) → CREATEINFOBASE с /UseTemplate.
    /// </summary>
    ConfigurationOnly
}

/// <summary>
/// Параметры клонирования клиент-серверной ИБ. Собирается из полей окна
/// клонирования и передаётся в <c>ServerCloneService</c>. Все значения
/// примитивные (плюс ссылка на источник) — сервис не зависит от UI-контролов
/// WPF/Avalonia.
/// </summary>
public sealed class ServerCloneRequest
{
    /// <summary>Исходная база, из которой создаётся клон.</summary>
    public Infobase Source { get; set; } = new();

    /// <summary>Наименование клона в списке программы.</summary>
    public string CloneName { get; set; } = string.Empty;

    /// <summary>Режим копирования (полная копия / только конфигурация).</summary>
    public ServerCloneMode Mode { get; set; } = ServerCloneMode.FullCopy;

    /// <summary>Версия платформы 1С (может содержать суффикс разрядности « (32)/(64)»).</summary>
    public string PlatformVersion { get; set; } = string.Empty;

    /// <summary>Имя сервера 1С (Srvr), возможно с портом «host:port».</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>Имя базы на сервере (Ref).</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>Тип СУБД (например, MSSQLServer, PostgreSQL).</summary>
    public string Dbms { get; set; } = string.Empty;

    /// <summary>Сервер СУБД.</summary>
    public string DbServer { get; set; } = string.Empty;

    /// <summary>Имя базы данных на сервере СУБД.</summary>
    public string DbName { get; set; } = string.Empty;

    /// <summary>Пользователь СУБД.</summary>
    public string DbUser { get; set; } = string.Empty;

    /// <summary>Пароль пользователя СУБД.</summary>
    public string DbPassword { get; set; } = string.Empty;

    /// <summary>Создавать базу данных на сервере СУБД (CrSQLDB="Y").</summary>
    public bool CreateSqlDatabase { get; set; }

    /// <summary>Блокировать фоновые задания (SchJobDn="Y").</summary>
    public bool BlockScheduledJobs { get; set; }

    /// <summary>Путь группы, в которую добавляется клон (может быть пустым).</summary>
    public string GroupPath { get; set; } = string.Empty;
}