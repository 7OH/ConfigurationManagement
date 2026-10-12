using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика задания по расписанию «Проверить наличие обновления конфигурации»
/// (issue #367): максимальная установленная версия выбранной конфигурации по списку
/// баз и сравнение её с каталогом релизов <c>releases.1c.ru</c>. Без UI, сети и
/// файловой системы — используется планировщиком и покрывается юнит-тестами.
/// Паттерн повторяет <see cref="PlatformUpdateCheckLogic"/> (issue #366).
/// </summary>
public static class ConfigurationUpdateCheckLogic
{
    /// <summary>Число версий, выводимых в сводном сообщении до «…».</summary>
    private const int SummaryVersionsLimit = 5;

    /// <summary>
    /// Имена конфигураций из списка баз (для выбора в форме задания, issue #367):
    /// непустые <see cref="Infobase.ConfigurationName"/> без дубликатов (различие
    /// регистра игнорируется), отсортированы по алфавиту.
    /// </summary>
    public static IReadOnlyList<string> ConfigurationNames(IEnumerable<Infobase>? bases)
    {
        return (bases ?? Array.Empty<Infobase>())
            .Where(b => b is not null)
            .Select(b => (b.ConfigurationName ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Установленные версии выбранной конфигурации по списку баз: базы с именем
    /// конфигурации <paramref name="configurationName"/> (без учёта регистра, с
    /// обрезкой пробелов) и непустыми <see cref="Infobase.ConfigurationVersion"/>.
    /// </summary>
    public static IReadOnlyList<string> InstalledVersions(
        IEnumerable<Infobase>? bases, string? configurationName)
    {
        var name = configurationName?.Trim() ?? string.Empty;
        if (name.Length == 0 || bases is null)
            return Array.Empty<string>();

        var versions = new List<string>();
        foreach (var infobase in bases)
        {
            if (infobase is null)
                continue;
            var baseConfigName = (infobase.ConfigurationName ?? string.Empty).Trim();
            if (!string.Equals(baseConfigName, name, StringComparison.OrdinalIgnoreCase))
                continue;

            var version = (infobase.ConfigurationVersion ?? string.Empty).Trim();
            if (version.Length > 0)
                versions.Add(version);
        }

        return versions;
    }

    /// <summary>
    /// Максимальная установленная версия конфигурации по списку баз (issue #367):
    /// максимум по <see cref="InstalledVersions"/>, сравнение — через
    /// <see cref="OneCPlatformCatalogParser.CompareVersions"/>. null — подходящих
    /// версий нет (имя конфигурации не встретилось либо версии не заполнены).
    /// </summary>
    public static string? MaxInstalledVersion(IEnumerable<Infobase>? bases, string? configurationName)
    {
        return MaxVersion(InstalledVersions(bases, configurationName));
    }

    /// <summary>
    /// База-представитель для построения адреса каталога релизов: первая база с
    /// выбранной конфигурацией, у которой задано собственное переопределение ссылки
    /// (<see cref="Infobase.UpdateUrlOverride"/>), затем — со связкой на типовую
    /// конфигурацию (<see cref="Infobase.UpdateConfigCode"/>), иначе первая
    /// подходящая. null — конфигурации в списке баз нет.
    /// </summary>
    public static Infobase? PickReferenceBase(IEnumerable<Infobase>? bases, string? configurationName)
    {
        var name = configurationName?.Trim() ?? string.Empty;
        if (name.Length == 0 || bases is null)
            return null;

        var matching = bases
            .Where(b => b is not null)
            .Where(b => string.Equals(
                (b.ConfigurationName ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matching.Count == 0)
            return null;

        return matching.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.UpdateUrlOverride))
            ?? matching.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.UpdateConfigCode))
            ?? matching[0];
    }

    /// <summary>
    /// Сравнивает максимальную установленную версию с доступными релизами каталога:
    /// в результат попадают версии строго новее установленной (по убыванию). Если
    /// установленной версии нет, все версии каталога считаются доступными.
    /// </summary>
    /// <param name="installedVersions">Установленные версии конфигурации по базам.</param>
    /// <param name="availableReleases">Релизы каталога конфигурации (releases.1c.ru).</param>
    public static ConfigurationUpdateCheckResult Check(
        IReadOnlyList<string>? installedVersions,
        IReadOnlyList<PlatformRelease>? availableReleases)
    {
        var maxInstalled = MaxVersion(installedVersions);

        var catalogVersions = (availableReleases ?? Array.Empty<PlatformRelease>())
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Version))
            .Select(r => r.Version.Trim())
            .ToList();

        var available = catalogVersions
            .Where(version => maxInstalled is null
                || OneCPlatformCatalogParser.CompareVersions(version, maxInstalled) > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        available.Sort((a, b) => OneCPlatformCatalogParser.CompareVersions(b, a));

        return new ConfigurationUpdateCheckResult
        {
            MaxInstalledVersion = maxInstalled,
            AvailableVersions = available,
            InstalledVersionInCatalog = maxInstalled is not null
                && catalogVersions.Contains(maxInstalled, StringComparer.Ordinal)
        };
    }

    /// <summary>
    /// Список версий для сводного сообщения: не более <paramref name="limit"/> значений,
    /// далее «… (+N)». Переиспользует форматирование проверки платформы (issue #366).
    /// </summary>
    public static string FormatVersions(IReadOnlyList<string>? versions, int limit = SummaryVersionsLimit)
        => PlatformUpdateCheckLogic.FormatVersions(versions, limit);

    /// <summary>Максимальная версия из списка (без учёта пустых значений), null — список пуст.</summary>
    private static string? MaxVersion(IReadOnlyList<string>? versions)
    {
        string? max = null;
        foreach (var raw in versions ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var version = raw.Trim();
            if (max is null || OneCPlatformCatalogParser.CompareVersions(version, max) > 0)
                max = version;
        }

        return max;
    }
}

/// <summary>
/// Результат сравнения установленной версии конфигурации с каталогом релизов
/// (задание «Проверить наличие обновления конфигурации», issue #367).
/// </summary>
public sealed class ConfigurationUpdateCheckResult
{
    /// <summary>Максимальная установленная версия по базам (null — версий нет).</summary>
    public string? MaxInstalledVersion { get; init; }

    /// <summary>Версии каталога строго новее установленной (по убыванию).</summary>
    public IReadOnlyList<string> AvailableVersions { get; init; } = Array.Empty<string>();

    /// <summary>True — найдены доступные версии.</summary>
    public bool HasUpdate => AvailableVersions.Count > 0;

    /// <summary>
    /// True — установленная версия найдена в каталоге релизов; false при
    /// <see cref="MaxInstalledVersion"/> == null не сообщает ничего полезного
    /// (упоминается в журнале, issue #367).
    /// </summary>
    public bool InstalledVersionInCatalog { get; init; }
}
