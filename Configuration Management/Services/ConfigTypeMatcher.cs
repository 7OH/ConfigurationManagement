using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сопоставление имени конфигурации информационной базы с типовой конфигурацией 1С
/// (issue #322): используется кнопкой «Определить версию» окна «Связать с конфигурацией»
/// и автоматическим построением каталога релизов при проверке обновлений (issue #323).
/// Приоритет признаков: точное совпадение наименования → точное совпадение сегмента URL →
/// вхождение наименования типовой в имя базы → вхождение имени базы в наименование типовой.
/// При сопоставлении по вхождению выбирается кандидат с САМЫМ ДЛИННЫМ наименованием —
/// это исключает подмену «Зарплата и управление персоналом» (ЗУП) на более короткое
/// «Бухгалтерия предприятия» и прочие ложные срабатывания.
/// </summary>
public static class ConfigTypeMatcher
{
    /// <summary>
    /// Находит типовую конфигурацию по имени конфигурации базы (см. приоритет признаков выше).
    /// Возвращает null, если имя пусто или ни одна конфигурация не подошла.
    /// </summary>
    public static OneCConfigType? FindByInfobaseName(IReadOnlyList<OneCConfigType> configs, string? configName)
    {
        if (configs is null || configs.Count == 0)
            return null;

        var trimmed = configName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return null;

        // 1) Точное совпадение наименования (без учёта регистра).
        var exact = configs.FirstOrDefault(c =>
            string.Equals(c.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        // 2) Точное совпадение сегмента URL типовой конфигурации с именем базы.
        var urlExact = configs.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(c.EffectiveUrlCode) &&
            string.Equals(c.EffectiveUrlCode.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        if (urlExact is not null)
            return urlExact;

        // 3) Наименование типовой конфигурации содержится в имени базы
        //    («Бухгалтерия предприятия, ред. 3.0»): выбираем САМОЕ ДЛИННОЕ из совпавших имён.
        OneCConfigType? bestContained = null;
        foreach (var c in configs)
        {
            var name = c.Name.Trim();
            if (name.Length == 0)
                continue;
            if (!trimmed.Contains(name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (bestContained is null || name.Length > bestContained.Name.Trim().Length)
                bestContained = c;
        }
        if (bestContained is not null)
            return bestContained;

        // 4) Имя базы содержится в наименовании типовой конфигурации — также самое длинное.
        OneCConfigType? bestContaining = null;
        foreach (var c in configs)
        {
            var name = c.Name.Trim();
            if (name.Length == 0)
                continue;
            if (!name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                continue;
            if (bestContaining is null || name.Length > bestContaining.Name.Trim().Length)
                bestContaining = c;
        }
        return bestContaining;
    }
}