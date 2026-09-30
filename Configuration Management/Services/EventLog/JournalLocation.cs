using System;
using System.Collections.Generic;

namespace Configuration_Management.Services.EventLog;

/// <summary>
/// Результат поиска журнала регистрации: каталог, определённый формат и файлы
/// данных. Заполняется <see cref="LgdJournalLocator"/>.
/// </summary>
public sealed class JournalLocation
{
    /// <summary>
    /// Каталог журнала (подкаталог 1Cv8Log каталога файловой базы).
    /// Может быть пустым, если журнал не найден.
    /// </summary>
    public string LogDir { get; init; } = string.Empty;

    /// <summary>Определённый формат журнала.</summary>
    public LgdFormat Format { get; init; }

    /// <summary>
    /// Основной файл описания: 1Cv8.lgd (SQLite) или 1Cv8.lgf (последовательный).
    /// </summary>
    public string MainFile { get; init; } = string.Empty;

    /// <summary>
    /// Файлы данных: [1Cv8.lgd] для SQLite или отсортированные по имени *.lgp
    /// для последовательного формата (имя фрагмента = начало периода).
    /// </summary>
    public IReadOnlyList<string> DataFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Текст ошибки, если журнал не найден или не читается; иначе null.
    /// (Локализация текстов — этап 0.3.9.164.)
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>True — журнал найден и доступен для чтения.</summary>
    public bool IsValid => Format != LgdFormat.Unknown
        && ErrorMessage is null
        && DataFiles.Count > 0
        && !string.IsNullOrEmpty(MainFile);

    /// <summary>Краткое название формата для отображения в окне.</summary>
    public string FormatDisplay => Format switch
    {
        LgdFormat.Sqlite => "SQLite (.lgd)",
        LgdFormat.Sequential => "Последовательный (.lgf/.lgp)",
        _ => "—",
    };
}