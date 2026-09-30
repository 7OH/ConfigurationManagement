using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистые хелперы сопоставления установленных и доступных версий технологической
/// платформы 1С (единый список строк окна) и совместимости выбранной версии
/// с информационными базами репозитория. Без сети и UI — покрывается тестами.
/// </summary>
public static class PlatformUpdateMatcher
{
    /// <summary>
    /// Объединяет установленные и доступные версии в единый список строк окна.
    /// Для установленной версии признак <see cref="PlatformUpdateMatch.HasUpdate"/>
    /// устанавливается, если в каталоге есть более новая версия, а
    /// <see cref="PlatformUpdateMatch.AvailableVersion"/> содержит новейшую из них.
    /// Версии, присутствующие только в каталоге, добавляются строками без признака
    /// установки. Сравнение версий — числовыми сегментами
    /// (<see cref="OneCPlatformCatalogParser.CompareVersions"/>), сортировка — по убыванию.
    /// </summary>
    /// <param name="installed">Установленные версии платформы.</param>
    /// <param name="available">Доступные версии из каталога портала.</param>
    /// <returns>Список строк окна, отсортированный по убыванию версии.</returns>
    public static IReadOnlyList<PlatformUpdateMatch> Merge(
        IReadOnlyList<string> installed,
        IReadOnlyList<PlatformRelease> available)
    {
        var installedVersions = (installed ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var availableReleases = (available ?? Array.Empty<PlatformRelease>()).ToList();

        var result = new List<PlatformUpdateMatch>(installedVersions.Count + availableReleases.Count);

        // Установленные версии: признак «есть обновление» + новейшая доступная версия.
        foreach (var version in installedVersions)
        {
            var newest = availableReleases
                .Where(r => OneCPlatformCatalogParser.CompareVersions(r.Version, version) > 0)
                .OrderByDescending(r => r.Version, VersionComparer.Instance)
                .Select(r => r.Version)
                .FirstOrDefault();

            result.Add(new PlatformUpdateMatch
            {
                Version = version,
                IsInstalled = true,
                HasUpdate = newest is not null,
                AvailableVersion = newest,
            });
        }

        // Только доступные версии (не установленные на этой машине).
        foreach (var release in availableReleases)
        {
            if (installedVersions.Any(v =>
                    string.Equals(v, release.Version, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(new PlatformUpdateMatch
            {
                Version = release.Version,
                IsInstalled = false,
                HasUpdate = false,
                AvailableVersion = null,
            });
        }

        result.Sort((a, b) => OneCPlatformCatalogParser.CompareVersions(b.Version, a.Version));
        return result;
    }

    /// <summary>
    /// Число баз репозитория, совместимых с выбранной версией платформы: база учитывается,
    /// если её <see cref="Infobase.PlatformVersion"/> начинается с префикса выбранной версии
    /// — точное совпадение 4 сегментов или префикс первых 3 сегментов
    /// (эвристика <c>MatchesVersionPrefix</c>, issue #142). Сравнение сегментов
    /// регистронезависимое; база с пустым <see cref="Infobase.PlatformVersion"/> не считается.
    /// </summary>
    /// <param name="version">Выбранная версия платформы, например «8.3.27.2214».</param>
    /// <param name="bases">Базы репозитория.</param>
    /// <returns>Число совместимых баз.</returns>
    public static int CountCompatibleBases(string version, IReadOnlyList<Infobase> bases)
        => GetCompatibleBaseNames(version, bases).Count;

    /// <summary>
    /// Имена баз репозитория, совместимых с выбранной версией платформы
    /// (та же эвристика, что в <see cref="CountCompatibleBases"/>): база учитывается,
    /// если её <see cref="Infobase.PlatformVersion"/> начинается с префикса выбранной
    /// версии — точное совпадение 4 сегментов или префикс первых 3 сегментов
    /// (<c>MatchesVersionPrefix</c>, issue #142). Пустая версия у базы не считается.
    /// Имя берётся из <see cref="Infobase.Name"/>; база без имени попадает списком
    /// своей версией. Порядок — как в репозитории.
    /// </summary>
    /// <param name="version">Выбранная версия платформы, например «8.3.27.2214».</param>
    /// <param name="bases">Базы репозитория.</param>
    /// <returns>Имена совместимых баз (для колонки/журнала окна «Совместимые базы»).</returns>
    public static IReadOnlyList<string> GetCompatibleBaseNames(string version, IReadOnlyList<Infobase> bases)
    {
        var prefix = FirstSegments(version, 3);
        if (string.IsNullOrEmpty(prefix))
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var infobase in bases ?? Array.Empty<Infobase>())
        {
            var baseVersion = (infobase?.PlatformVersion ?? string.Empty).Trim();
            if (baseVersion.Length == 0)
                continue;
            if (!MatchesVersionPrefix(baseVersion, prefix))
                continue;

            var name = (infobase?.Name ?? string.Empty).Trim();
            result.Add(name.Length == 0 ? baseVersion : name);
        }

        return result;
    }

    /// <summary>Первые <paramref name="count"/> сегментов версии через точку
    /// («8.3.27.2214», 3 → «8.3.27»); при меньшем числе сегментов — вся версия.</summary>
    private static string FirstSegments(string version, int count)
    {
        var parts = (version ?? string.Empty)
            .Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return string.Empty;

        if (parts.Length > count)
            Array.Resize(ref parts, count);
        return string.Join(".", parts);
    }

    /// <summary>Проверяет, начинается ли фактическая версия с указанного префикса
    /// по сегментам («8.3.27.1688» соответствует «8.3.27», но не «8.5» или «8.3.2»).
    /// Паттерн приватного хелпера <c>PlatformVersionService.MatchesVersionPrefix</c>.</summary>
    private static bool MatchesVersionPrefix(string version, string prefix)
    {
        var vParts = (version ?? string.Empty).Split('.');
        var pParts = (prefix ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (vParts.Length < pParts.Length)
            return false;

        for (var i = 0; i < pParts.Length; i++)
        {
            if (!string.Equals(vParts[i].Trim(), pParts[i].Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>Компаратор версий по убыванию числовыми сегментами
    /// (через <see cref="OneCPlatformCatalogParser.CompareVersions"/>).</summary>
    private sealed class VersionComparer : IComparer<string>
    {
        /// <summary>Единственный экземпляр компаратора.</summary>
        public static readonly VersionComparer Instance = new();

        /// <inheritdoc />
        public int Compare(string? x, string? y)
            => OneCPlatformCatalogParser.CompareVersions(x ?? string.Empty, y ?? string.Empty);
    }
}