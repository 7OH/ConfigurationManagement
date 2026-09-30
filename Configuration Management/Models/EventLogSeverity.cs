namespace Configuration_Management.Models;

/// <summary>
/// Важность события журнала регистрации. Соответствует значениям конфигуратора
/// 1С: информация, предупреждение, ошибка, примечание. Используется и в
/// SQLite-формате (.lgd, severityCode 0..3), и в последовательном
/// (.lgf/.lgp, буквы I/E/W/N).
/// </summary>
public enum EventLogSeverity
{
    /// <summary>Информация.</summary>
    Info = 0,

    /// <summary>Предупреждение.</summary>
    Warning = 1,

    /// <summary>Ошибка.</summary>
    Error = 2,

    /// <summary>Примечание.</summary>
    Note = 3,
}