using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Признак, по которому конфигурация сопоставлена с именем конфигурации информационной базы
/// (issue #322): используется для пояснения в окне «Связать с конфигурацией», по какому полю
/// найдена запись.
/// </summary>
public enum ConfigMatchKind
{
    /// <summary>Совпадения нет.</summary>
    None = 0,

    /// <summary>Точное совпадение наименования конфигурации и имени базы.</summary>
    ExactName,

    /// <summary>Точное совпадение сегмента адреса обновлений (<see cref="OneCConfigType.EffectiveUrlCode"/>) с именем базы.</summary>
    ExactUrlCode,

    /// <summary>Наименование конфигурации входит в имя базы («…Зарплата и управление персоналом, ред. 3.1»).</summary>
    NameContainedInBaseName,

    /// <summary>Имя базы входит в наименование конфигурации.</summary>
    BaseNameContainedInConfigName,
}

/// <summary>
/// Результат сопоставления: найденная конфигурация и признак, по которому она найдена.
/// </summary>
public sealed class ConfigMatchResult
{
    /// <param name="config">Найденная конфигурация.</param>
    /// <param name="kind">Признак совпадения (см. <see cref="ConfigMatchKind"/>).</param>
    public ConfigMatchResult(OneCConfigType config, ConfigMatchKind kind)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Kind = kind;
    }

    /// <summary>Найденная конфигурация.</summary>
    public OneCConfigType Config { get; }

    /// <summary>Признак, по которому произошло совпадение.</summary>
    public ConfigMatchKind Kind { get; }
}

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
        return FindMatch(configs, configName)?.Config;
    }

    /// <summary>
    /// Находит типовую конфигурацию по имени конфигурации базы и возвращает результат с
    /// признаком совпадения (<see cref="ConfigMatchResult.Kind"/>) — для пояснения пользователю,
    /// по какому полю найдена запись (issue #322). Приоритет признаков тот же, что у
    /// <see cref="FindByInfobaseName"/>; поведение не менялось — добавлена только причина.
    /// </summary>
    public static ConfigMatchResult? FindMatch(IReadOnlyList<OneCConfigType> configs, string? configName)
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
            return new ConfigMatchResult(exact, ConfigMatchKind.ExactName);

        // 2) Точное совпадение сегмента URL типовой конфигурации с именем базы.
        var urlExact = configs.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(c.EffectiveUrlCode) &&
            string.Equals(c.EffectiveUrlCode.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        if (urlExact is not null)
            return new ConfigMatchResult(urlExact, ConfigMatchKind.ExactUrlCode);

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
            return new ConfigMatchResult(bestContained, ConfigMatchKind.NameContainedInBaseName);

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
        if (bestContaining is not null)
            return new ConfigMatchResult(bestContaining, ConfigMatchKind.BaseNameContainedInConfigName);

        return null;
    }
}