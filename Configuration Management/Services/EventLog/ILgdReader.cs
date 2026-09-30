using System.Collections.Generic;
using System.Threading;
using Configuration_Management.Models;

namespace Configuration_Management.Services.EventLog;

/// <summary>
/// Ридер журнала регистрации информационной базы. Потоковое чтение без
/// материализации всего файла; фильтры и постраничная выборка с учётом
/// условий добавляются на этапе 0.3.9.163 (сейчас страница — просто срез
/// skip/take по порядку записей).
/// </summary>
public interface ILgdReader
{
    /// <summary>Формат, который умеет читать ридер.</summary>
    LgdFormat Format { get; }

    /// <summary>True, если ридер поддерживает указанное расположение журнала.</summary>
    bool CanRead(JournalLocation location);

    /// <summary>Полное число записей в журнале (без фильтров).</summary>
    long CountRows(JournalLocation location, CancellationToken ct = default);

    /// <summary>
    /// Читает страницу записей: начиная с <paramref name="skip"/>, не более
    /// <paramref name="take"/> штук, в порядке возрастания
    /// <see cref="EventLogEntry.RowId"/>.
    /// Прогресс <paramref name="progress"/> — доля обработанных данных страницы
    /// от 0 до 1.
    /// </summary>
    IEnumerable<EventLogEntry> ReadPage(
        JournalLocation location,
        int skip,
        int take,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}