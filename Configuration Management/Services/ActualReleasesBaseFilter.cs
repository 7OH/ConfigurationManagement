using System;
using System.Collections.Generic;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Фильтр «Только в базах» окна «Актуальные релизы» (issue #375): определяет, какие
/// типовые конфигурации присутствуют в списке информационных баз — как по явной
/// привязке (<see cref="Infobase.UpdateConfigCode"/>), так и по эвристике
/// (<see cref="Infobase.ConfigurationName"/>). Чистая функция без UI — покрыта тестами.
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

            // 1) Явная привязка: код типовой конфигурации базы (issue #322/#346).
            var byCode = ConfigTypeMatcher.FindByCode(configs, infobase.UpdateConfigCode);
            if (byCode is not null && !string.IsNullOrWhiteSpace(byCode.Code))
            {
                result.Add(byCode.Code);
                continue;
            }

            // 2) Эвристика: имя конфигурации базы (вкладка «Платформа»).
            if (string.IsNullOrWhiteSpace(infobase.ConfigurationName))
                continue;
            var byName = ConfigTypeMatcher.FindByInfobaseName(configs, infobase.ConfigurationName);
            if (byName is not null && !string.IsNullOrWhiteSpace(byName.Code))
                result.Add(byName.Code);
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
}
