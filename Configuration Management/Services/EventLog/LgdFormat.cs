namespace Configuration_Management.Services.EventLog;

/// <summary>
/// Физический формат журнала регистрации информационной базы 1С.
/// Платформа поддерживает два формата: последовательный (файлы .lgf/.lgp/.lgx)
/// и SQLite (файл .lgd, платформы 8.3.5–8.3.21; единственный последовательный
/// с 8.3.22).
/// </summary>
public enum LgdFormat
{
    /// <summary>Формат не определён (нет файлов журнала).</summary>
    Unknown = 0,

    /// <summary>SQLite: файл 1Cv8.lgd (новый формат, 8.3.5–8.3.21).</summary>
    Sqlite = 1,

    /// <summary>
    /// Последовательный: 1Cv8.lgf (описание) + *.lgp (фрагменты, ZIP) + *.lgx
    /// (индексы). До 8.3.5 и по умолчанию с 8.3.12; единственный с 8.3.22.
    /// </summary>
    Sequential = 2,
}