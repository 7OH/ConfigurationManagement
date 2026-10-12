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
    /// <para>Issue #361 (0.3.12.10): логика показа и признака «есть обновление» переработаны.</para>
    /// <list type="bullet">
    /// <item><description>В список попадают только <b>установленные</b> версии и
    /// <b>максимальная</b> версия каждой «линии» (по первым 3 сегментам), не ниже
    /// самой низкой установленной линии — исторические и более старые версии не
    /// показываются («Вопрос и про версии НИЖЕ. Их то зачем показывать?»).</description></item>
    /// <item><description>Если compatible-база закреплена за <b>4-сегментной</b> версией
    /// (напр. «8.3.26.11540») — проверка обновления для этой линии не выполняется
    /// (<see cref="PlatformUpdateMatch.HasUpdate"/> = false): база использует полную
    /// версию неспроста.</description></item>
    /// <item><description>Если compatible-база указывает <b>3 сегмента</b> (напр. «8.3.26») —
    /// обновлением считается только версия со строго большими первыми 3 сегментами
    /// (новые сборки в той же линии 8.3.26.x обновлением не считаются).</description></item>
    /// <item><description>Без compatible-баз — прежнее поведение: любая более новая версия
    /// (полное 4-сегментное сравнение).</description></item>
    /// <item><description>Issue #361 (0.3.12.11): уже установленные версии исключаются из
    /// кандидатов обновления — установленная версия (или её точный доступный дубль)
    /// показывается как «Установлена», а не «Есть обновление».</description></item>
    /// <item><description>Issue #361 (0.3.12.11): пины версий compatible-баз нормализуются
    /// (Trim, суффиксы «(32)»/«(64)», префикс «V8.») при определении режима проверки.</description></item>
    /// <item><description>Issue #364 (0.3.12.16): тестовые (предрелизные) сборки
    /// (<see cref="PlatformRelease.IsTest"/>) НИКОГДА не становятся «Есть обновление»
    /// и не участвуют в выборе «максимума линии» — даже при включённой опции
    /// «Учитывать тестовые». При <paramref name="includeTestVersions"/> = true они
    /// добавляются отдельными строками со статусом «Доступна»/«Тестовая версия»;
    /// при false тестовые сборки из списка исключаются полностью.</description></item>
    /// </list>
    /// </summary>
    /// <param name="installed">Установленные версии платформы.</param>
    /// <param name="available">Доступные версии из каталога портала.</param>
    /// <param name="bases">Базы репозитория (для учёта закреплений версии платформы).
    /// Может быть null — тогда применяется прежнее (полное) сравнение.</param>
    /// <param name="includeTestVersions">True — тестовые сборки платформы (issue #364)
    /// показываются отдельными строками; false (по умолчанию) — скрываются.</param>
    /// <returns>Список строк окна, отсортированный по убыванию версии.</returns>
    public static IReadOnlyList<PlatformUpdateMatch> Merge(
        IReadOnlyList<string> installed,
        IReadOnlyList<PlatformRelease> available,
        IReadOnlyList<Infobase>? bases = null,
        bool includeTestVersions = false)
    {
        var installedVersions = (installed ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var availableReleases = (available ?? Array.Empty<PlatformRelease>())
            .Where(r => !string.IsNullOrWhiteSpace(r?.Version))
            .ToList();
        // Issue #364: расчёт обновлений и «максимума линии» ведётся ТОЛЬКО по релизным
        // версиям; тестовые сборки обрабатываются отдельно в конце метода.
        var releaseReleases = availableReleases.Where(r => !r.IsTest).ToList();
        var testReleases = availableReleases.Where(r => r.IsTest).ToList();
        var allBases = bases ?? Array.Empty<Infobase>();

        var result = new List<PlatformUpdateMatch>(installedVersions.Count + availableReleases.Count);

        // Минимальная установленная линия (первые 3 сегмента) — ниже неё строки не показываем.
        var minInstalledLine = installedVersions.Count > 0
            ? installedVersions.Select(v => FirstSegments(v, 3)).Min(StringComparer.OrdinalIgnoreCase)
            : null;

        // Issue #361 (0.3.12.11): уже установленные версии исключаются из кандидатов
        // обновления — установленная версия (или её точный доступный дубль) не должна
        // получать «Есть обновление».
        var installedVersionSet = new HashSet<string>(
            installedVersions, StringComparer.OrdinalIgnoreCase);

        // Установленные версии: признак «есть обновление» с учётом закреплений баз (issue #361).
        foreach (var version in installedVersions)
        {
            var mode = ResolveUpdateMode(version, allBases);
            string? newest = null;

            if (mode != UpdateCheckMode.Disabled)
            {
                if (mode == UpdateCheckMode.ThreePart)
                {
                    // Issue #361: обновление — только в более новую ЛИНИЮ (первые 3 сегмента строго больше).
                    var installedLine = FirstSegments(version, 3);
                    newest = releaseReleases
                        .Where(r => IsLineStrictlyNewer(r.Version, installedLine)
                            && !installedVersionSet.Contains(r.Version))
                        .OrderByDescending(r => r.Version, VersionComparer.Instance)
                        .Select(r => r.Version)
                        .FirstOrDefault();
                }
                else
                {
                    // Без compatible-баз — прежнее полное сравнение (Full).
                    newest = releaseReleases
                        .Where(r => OneCPlatformCatalogParser.CompareVersions(r.Version, version) > 0
                            && !installedVersionSet.Contains(r.Version))
                        .OrderByDescending(r => r.Version, VersionComparer.Instance)
                        .Select(r => r.Version)
                        .FirstOrDefault();
                }
            }

            result.Add(new PlatformUpdateMatch
            {
                Version = version,
                IsInstalled = true,
                HasUpdate = newest is not null,
                AvailableVersion = newest,
            });
        }

        // Issue #361: только доступные версии — максимум каждой линии (первые 3 сегмента),
        // не ниже минимальной установленной линии; линии, чей максимум уже установлен, пропускаем.
        foreach (var group in releaseReleases
                     .GroupBy(r => FirstSegments(r.Version, 3), StringComparer.OrdinalIgnoreCase))
        {
            var line = group.Key;
            if (minInstalledLine is not null &&
                OneCPlatformCatalogParser.CompareVersions(line + ".0.0", minInstalledLine + ".0.0") < 0)
                continue; // линия ниже всех установленных — не показываем (issue #361)

            var maxOfLine = group
                .OrderByDescending(r => r.Version, VersionComparer.Instance)
                .First();

            if (installedVersionSet.Contains(maxOfLine.Version))
                continue; // максимум линии уже установлен — строка установлена выше

            result.Add(new PlatformUpdateMatch
            {
                Version = maxOfLine.Version,
                IsInstalled = false,
                HasUpdate = false,
                AvailableVersion = null,
            });
        }

        // Issue #364 (0.3.12.16): тестовые (предрелизные) сборки — отдельными строками
        // при включённой опции «Учитывать тестовые». Они НЕ помечаются «Есть обновление»
        // и не заменяют релизную максимальную версию линии; уже установленная версия
        // (в т.ч. тестовая) строкой дублируется.
        if (includeTestVersions)
        {
            var seen = new HashSet<string>(result.Select(m => m.Version), StringComparer.OrdinalIgnoreCase);
            foreach (var test in testReleases)
            {
                if (!seen.Add(test.Version))
                    continue;

                result.Add(new PlatformUpdateMatch
                {
                    Version = test.Version,
                    IsInstalled = false,
                    HasUpdate = false,
                    AvailableVersion = null,
                });
            }
        }

        result.Sort((a, b) => OneCPlatformCatalogParser.CompareVersions(b.Version, a.Version));
        return result;
    }

    /// <summary>Режим проверки обновления для установленной версии с учётом закреплений
    /// compatible-баз (issue #361).</summary>
    private enum UpdateCheckMode
    {
        /// <summary>Прежнее полное 4-сегментное сравнение (нет compatible-баз).</summary>
        Full,

        /// <summary>Только строго новая линия (первые 3 сегмента) — база с 3-сегментной версией.</summary>
        ThreePart,

        /// <summary>Проверка отключена — хотя бы одна compatible-база закреплена за 4-сегментной версией.</summary>
        Disabled,
    }

    /// <summary>Определяет режим проверки обновления для установленной версии:
    /// если хоть одна compatible-база (линия по 3 сегментам) закреплена за полной
    /// 4-сегментной версией — Disabled; иначе если есть база с 3-сегментной версией —
    /// ThreePart; иначе Full (баз нет).</summary>
    private static UpdateCheckMode ResolveUpdateMode(string installedVersion, IReadOnlyList<Infobase> bases)
    {
        var line = FirstSegments(installedVersion, 3);
        if (string.IsNullOrEmpty(line))
            return UpdateCheckMode.Full;

        var hasThreePartBase = false;
        foreach (var infobase in bases)
        {
            // Issue #361 (0.3.12.11): пин нормализуется — снимаются Trim, суффиксы
            // разрядности «(32)»/«(64)» и префикс «V8.» («8.3.26.1656 (64)» → «8.3.26.1656»),
            // иначе «1656 (64)» не считалось сегментом и закрепление 4 сегментов не виделось.
            var baseVersion = NormalizePinnedVersion(infobase?.PlatformVersion);
            if (baseVersion.Length == 0)
                continue;
            if (!MatchesVersionPrefix(baseVersion, line))
                continue;

            var segments = CountNumericSegments(baseVersion);
            if (segments >= 4)
                return UpdateCheckMode.Disabled; // закрепление за полной версией
            if (segments == 3)
                hasThreePartBase = true;
        }

        return hasThreePartBase ? UpdateCheckMode.ThreePart : UpdateCheckMode.Full;
    }

    /// <summary>Нормализует версию-пин из <see cref="Infobase.PlatformVersion"/> для
    /// определения режима проверки (issue #361): Trim, снятие суффикса разрядности
    /// «(32)»/«(64)» (и «(x86)»/«(x64)») через
    /// <see cref="PlatformVersionService.ParseVariantOptionalArch"/> и префикса «V8.»
    /// («8.3.26.1656 (64)» → «8.3.26.1656», «V8.3.26» → «8.3.26»).</summary>
    private static string NormalizePinnedVersion(string? version)
    {
        var normalized = (version ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return string.Empty;

        PlatformVersionService.ParseVariantOptionalArch(normalized, out var clean, out _);
        normalized = clean.Trim();

        if (normalized.StartsWith("V8.", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(3).Trim();

        return normalized;
    }

    /// <summary>Число числовых сегментов версии («8.3.26» → 3, «8.3.26.11540» → 4,
    /// нечисловые сегменты вроде «V» игнорируются).</summary>
    private static int CountNumericSegments(string version)
    {
        var parts = (version ?? string.Empty).Split('.');
        var count = 0;
        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), out _))
                count++;
        }
        return count;
    }

    /// <summary>True, если версия <paramref name="version"/> принадлежит линии со строго
    /// большими первыми 3 сегментами, чем <paramref name="line"/> («8.3.27.2214» для
    /// «8.3.26» → true; «8.3.26.1890» для «8.3.26» → false — та же линия).</summary>
    private static bool IsLineStrictlyNewer(string version, string line)
    {
        var versionLine = FirstSegments(version, 3);
        if (string.IsNullOrEmpty(versionLine) || string.IsNullOrEmpty(line))
            return false;
        // Сравниваем линии как версии с нулевыми хвостами: «8.3.27.0.0» > «8.3.26.0.0».
        return OneCPlatformCatalogParser.CompareVersions(versionLine + ".0.0", line + ".0.0") > 0;
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