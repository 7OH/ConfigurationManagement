using System;
using System.Collections.Generic;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Фильтр «Только в базах» окна «Актуальные релизы» (issue #375): определяет, какие
/// типовые конфигурации присутствуют в списке информационных баз — как по явной
/// привязке (<see cref="Infobase.UpdateConfigCode"/>), так и по эвристике
/// (<see cref="Infobase.ConfigurationName"/>), а также какие редакции (Red) встречаются
/// в версиях баз (<see cref="Infobase.ConfigurationVersion"/>). Чистая функция без UI —
/// покрыта тестами.
/// </summary>
public static class ActualReleasesBaseFilter
{
    /// <summary>
    /// Возвращает коды типовых конфигураций, присутствующих в списке баз. Конфигурация
    /// считается присутствующей, если хотя бы одна база ссылается на неё по привязке
    /// (<see cref="Infobase.UpdateConfigCode"/>, приоритет) либо её имя конфигурации
    /// сопоставляется с типовой по эвристике (<see cref="ConfigTypeMatcher.FindByInfobaseName"/>).
    /// Пустой список конфигураций/баз или null — пустой результат.
    /// </summary>
    public static HashSet<string> CollectPresentConfigCodes(
        IReadOnlyList<OneCConfigType>? configs, IReadOnlyList<Infobase>? bases)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (configs is null || configs.Count == 0 || bases is null || bases.Count == 0)
            return result;

        foreach (var infobase in bases)
        {
            if (infobase is null)
                continue;
            var config = FindConfig(configs, infobase);
            if (config is not null && !string.IsNullOrWhiteSpace(config.Code))
                result.Add(config.Code);
        }

        return result;
    }

    /// <summary>
    /// Возвращает для каждой присутствующей конфигурации набор редакций (Red), встречающихся
    /// в версиях баз (issue #375). Ключ — код конфигурации, значение — множество значений
    /// Red (например «1.3»), извлечённых из <see cref="Infobase.ConfigurationVersion"/>
    /// (например «1.3.285.1»). Пустое множество — версии баз не сопоставлены, фильтр по
    /// редакциям не применяется. Пустой список конфигураций/баз или null — пустой результат.
    /// </summary>
    public static Dictionary<string, HashSet<string>> CollectPresentEditionReds(
        IReadOnlyList<OneCConfigType>? configs, IReadOnlyList<Infobase>? bases)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (configs is null || configs.Count == 0 || bases is null || bases.Count == 0)
            return result;

        foreach (var infobase in bases)
        {
            if (infobase is null)
                continue;
            var config = FindConfig(configs, infobase);
            if (config is null || string.IsNullOrWhiteSpace(config.Code))
                continue;

            var red = ExtractRed(infobase.ConfigurationVersion);
            if (red is null)
                continue; // версия базы не задана — редакцию не сопоставляем

            if (!result.TryGetValue(config.Code, out var reds))
            {
                reds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[config.Code] = reds;
            }
            reds.Add(red);
        }

        return result;
    }

    /// <summary>
    /// True, если конфигурация присутствует в списке баз (её код есть в
    /// <see cref="CollectPresentConfigCodes"/>). Конфигурация без кода присутствующей
    /// не считается.
    /// </summary>
    public static bool IsPresent(OneCConfigType? config, HashSet<string> presentCodes)
        => config is not null
           && !string.IsNullOrWhiteSpace(config.Code)
           && presentCodes.Contains(config.Code);

    /// <summary>
    /// True, если редакция должна быть показана при фильтре «Только в базах» (issue #375):
    /// редакция без Red, либо отсутствие сопоставленных редакций (нет данных о версиях баз),
    /// либо Red редакции есть в наборе сопоставленных редакций.
    /// </summary>
    public static bool IsEditionPresent(OneCConfigEdition? edition, HashSet<string>? presentReds)
    {
        if (edition is null)
            return false;
        if (string.IsNullOrWhiteSpace(edition.Red))
            return true; // редакция без версии — фильтровать нечем
        if (presentReds is null || presentReds.Count == 0)
            return true; // нет данных о версиях баз — показываем все редакции
        return presentReds.Contains(edition.Red);
    }

    /// <summary>Находит типовую конфигурацию для базы: по привязке (приоритет), иначе по эвристике имени.</summary>
    private static OneCConfigType? FindConfig(IReadOnlyList<OneCConfigType> configs, Infobase infobase)
    {
        var byCode = ConfigTypeMatcher.FindByCode(configs, infobase.UpdateConfigCode);
        if (byCode is not null)
            return byCode;
        if (string.IsNullOrWhiteSpace(infobase.ConfigurationName))
            return null;
        return ConfigTypeMatcher.FindByInfobaseName(configs, infobase.ConfigurationName);
    }

    /// <summary>
    /// Извлекает «Ред» из версии конфигурации базы (например «1.3» из «1.3.285.1»):
    /// первые два сегмента. null, если версия не задана или содержит меньше двух сегментов.
    /// </summary>
    private static string? ExtractRed(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        var parts = version.Split('.');
        if (parts.Length < 2)
            return null;
        return parts[0] + "." + parts[1];
    }
}
