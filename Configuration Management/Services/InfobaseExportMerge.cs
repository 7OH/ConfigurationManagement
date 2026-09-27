using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Результат слияния импортируемых баз с текущим списком.
/// </summary>
/// <param name="Added">Базы, добавленные в список.</param>
/// <param name="Skipped">Импортируемые базы, пропущенные: база с тем же Id уже есть.</param>
public sealed record InfobaseMergeResult(List<Infobase> Added, List<Infobase> Skipped);

/// <summary>
/// Чистая логика добавляющего импорта (выборочный импорт баз из файла):
/// база считается дубликатом по Id, при отсутствии Id — по паре
/// «имя + строка подключения». Не зависит от платформы и UI — покрыта тестами.
/// </summary>
public static class InfobaseExportMerge
{
    /// <summary>
    /// Отбирает из <paramref name="incoming"/> базы, которых ещё нет в
    /// <paramref name="existing"/> (дубликаты идут в Skipped). Порядок входного
    /// списка сохраняется.
    /// </summary>
    public static InfobaseMergeResult SelectNew(IEnumerable<Infobase> existing, IEnumerable<Infobase> incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        var existingKeys = new HashSet<string>(
            existing.Select(DuplicateKey),
            StringComparer.OrdinalIgnoreCase);

        var added = new List<Infobase>();
        var skipped = new List<Infobase>();

        foreach (var candidate in incoming)
        {
            if (existingKeys.Contains(DuplicateKey(candidate)))
                skipped.Add(candidate);
            else
            {
                added.Add(candidate);
                existingKeys.Add(DuplicateKey(candidate));
            }
        }

        return new InfobaseMergeResult(added, skipped);
    }

    /// <summary>
    /// Ключ дубликата: Id, а при его отсутствии — «имя|строка подключения»
    /// (без учёта регистра).
    /// </summary>
    public static string DuplicateKey(Infobase ib)
    {
        if (!string.IsNullOrWhiteSpace(ib.Id))
            return "id:" + ib.Id.Trim();

        var connection = ib.Connection?.ToConnectionString() ?? "";
        return "conn:" + (ib.Name ?? "").Trim() + "|" + connection.Trim();
    }

    /// <summary>
    /// Группы, в которых лежат выбранные базы: имена непустых групп из списка
    /// (для добавления недостающих групп при импорте).
    /// </summary>
    public static List<string> CollectGroupNames(IEnumerable<Infobase> bases) =>
        bases.Select(b => (b.Group ?? "").Trim())
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
