using System.Globalization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика задания по расписанию «Проверить наличие обновления платформы»
/// (issue #366): разбор и сопоставление выражения проверки («8.5.*», «8.3.27.*»)
/// и сравнение установленных версий с доступными в каталоге платформы.
/// Без UI, сети и файловой системы — используется планировщиком и покрывается юнит-тестами.
/// </summary>
public static class PlatformUpdateCheckLogic
{
    /// <summary>Максимальное число сегментов выражения (например, «8.3.27.1234»).</summary>
    private const int MaxSegments = 6;

    /// <summary>Число версий, выводимых в сводном сообщении до «…».</summary>
    private const int SummaryVersionsLimit = 5;

    /// <summary>
    /// Проверяет, что выражение проверки корректно: непустая последовательность сегментов,
    /// разделённых точкой; каждый сегмент — либо «*» (любое значение), либо целое число
    /// без знака. Примеры корректных выражений: «8.5.*», «8.3.27.*», «8.3», «*».
    /// </summary>
    public static bool IsValidExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var segments = expression.Trim().Split('.');
        if (segments.Length == 0 || segments.Length > MaxSegments)
            return false;

        foreach (var raw in segments)
        {
            var segment = raw.Trim();
            if (segment == "*")
                continue;
            if (segment.Length == 0 || !segment.All(c => c >= '0' && c <= '9'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Версия подходит под выражение: сегменты сравниваются слева направо, «*» совпадает
    /// с любым числовым значением; если в версии сегментов меньше, чем в выражении,
    /// недостающие считаются нулями («8.3.27» подходит под «8.3.27.*»). Не-числовой
    /// сегмент версии (например, неотброшенный суффикс «(64)») не подходит ни под
    /// какое значение, включая «*».
    /// </summary>
    public static bool MatchesExpression(string? version, string? expression)
    {
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(expression))
            return false;

        var versionSegments = version.Trim().Split('.');
        var expressionSegments = expression.Trim().Split('.');

        for (var i = 0; i < expressionSegments.Length; i++)
        {
            var expressionSegment = expressionSegments[i].Trim();
            var versionSegment = i < versionSegments.Length
                ? versionSegments[i].Trim()
                : "0";

            if (!int.TryParse(versionSegment, NumberStyles.None, CultureInfo.InvariantCulture, out var actual))
                return false;

            if (expressionSegment == "*")
                continue;

            if (!int.TryParse(expressionSegment, NumberStyles.None, CultureInfo.InvariantCulture, out var expected))
                return false;

            if (expected != actual)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Сравнивает установленные версии с доступными в каталоге: из доступных отбираются
    /// версии, подходящие под выражение, проходящие фильтр тестовых сборок и строго
    /// превосходящие максимальную установленную версию по выражению. Если установленных
    /// версий по выражению нет, все подходящие версии считаются доступными.
    /// </summary>
    /// <param name="installedVersions">Установленные версии (чистые номера без суффикса разрядности).</param>
    /// <param name="availableReleases">Релизы каталога платформы.</param>
    /// <param name="expression">Выражение проверки («8.5.*», «8.3.27.*»).</param>
    /// <param name="includeTestVersions">Учитывать ли тестовые (предрелизные) сборки.</param>
    public static PlatformUpdateCheckResult Check(
        IReadOnlyList<string>? installedVersions,
        IReadOnlyList<PlatformRelease>? availableReleases,
        string? expression,
        bool includeTestVersions)
    {
        var matchingInstalled = (installedVersions ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Where(v => MatchesExpression(v, expression))
            .ToList();

        string? maxInstalled = null;
        foreach (var version in matchingInstalled)
        {
            if (maxInstalled is null || OneCPlatformCatalogParser.CompareVersions(version, maxInstalled) > 0)
                maxInstalled = version;
        }

        var available = (availableReleases ?? Array.Empty<PlatformRelease>())
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Version))
            .Where(r => MatchesExpression(r.Version, expression))
            .Where(r => includeTestVersions || !r.IsTest)
            .Where(r => maxInstalled is null || OneCPlatformCatalogParser.CompareVersions(r.Version.Trim(), maxInstalled) > 0)
            .Select(r => r.Version.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        available.Sort((a, b) => OneCPlatformCatalogParser.CompareVersions(b, a));

        return new PlatformUpdateCheckResult
        {
            MaxInstalledVersion = maxInstalled,
            AvailableVersions = available
        };
    }

    /// <summary>
    /// Список версий для сводного сообщения: не более <paramref name="limit"/> значений,
    /// далее «… (+N)». Пустой список — пустая строка.
    /// </summary>
    public static string FormatVersions(IReadOnlyList<string>? versions, int limit = SummaryVersionsLimit)
    {
        var list = (versions ?? Array.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (list.Count == 0)
            return string.Empty;
        if (list.Count <= limit)
            return string.Join(", ", list);
        return string.Join(", ", list.Take(limit)) + $"… (+{list.Count - limit})";
    }
}

/// <summary>
/// Результат сравнения установленных и доступных версий платформы
/// (задание «Проверить наличие обновления платформы», issue #366).
/// </summary>
public sealed class PlatformUpdateCheckResult
{
    /// <summary>Максимальная установленная версия по выражению (null — установок по выражению нет).</summary>
    public string? MaxInstalledVersion { get; init; }

    /// <summary>Доступные версии строго новее установленных (по убыванию).</summary>
    public IReadOnlyList<string> AvailableVersions { get; init; } = Array.Empty<string>();

    /// <summary>True — найдены доступные версии.</summary>
    public bool HasUpdate => AvailableVersions.Count > 0;
}
