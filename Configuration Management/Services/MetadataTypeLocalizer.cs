namespace Configuration_Management.Services;

/// <summary>
/// Сопоставление каталогов типов метаданных выгрузки <c>/DumpConfigToFiles</c>
/// (первый уровень внутри <c>Configuration/</c>) с локализованными именами.
/// Чистый класс без зависимости от <c>LocalizationManager</c>: локализатор передаётся
/// колбэком <c>Func<string,string></c> (как в <c>DiskFreeSpaceHelper</c>) —
/// это делает класс тестируемым без инициализации локализации.
/// Неизвестный каталог (новая версия платформы, расширение и т.п.) возвращается как есть.
/// </summary>
public static class MetadataTypeLocalizer
{
    /// <summary>Порядок типов в отчёте: порядок словаря; неизвестные — в конце.</summary>
    private static readonly IReadOnlyDictionary<string, string> TypeKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Document"] = "ConfigDiff.Type.Document",
            ["DocumentJournal"] = "ConfigDiff.Type.DocumentJournal",
            ["Catalog"] = "ConfigDiff.Type.Catalog",
            ["ChartOfCharacteristicTypes"] = "ConfigDiff.Type.ChartOfCharacteristicTypes",
            ["ChartOfAccounts"] = "ConfigDiff.Type.ChartOfAccounts",
            ["ChartOfCalculationTypes"] = "ConfigDiff.Type.ChartOfCalculationTypes",
            ["Constant"] = "ConfigDiff.Type.Constant",
            ["Enum"] = "ConfigDiff.Type.Enum",
            ["InformationRegister"] = "ConfigDiff.Type.InformationRegister",
            ["AccumulationRegister"] = "ConfigDiff.Type.AccumulationRegister",
            ["AccountingRegister"] = "ConfigDiff.Type.AccountingRegister",
            ["CalculationRegister"] = "ConfigDiff.Type.CalculationRegister",
            ["BusinessProcess"] = "ConfigDiff.Type.BusinessProcess",
            ["Task"] = "ConfigDiff.Type.Task",
            ["DataProcessor"] = "ConfigDiff.Type.DataProcessor",
            ["Report"] = "ConfigDiff.Type.Report",
            ["Role"] = "ConfigDiff.Type.Role",
            ["CommonModule"] = "ConfigDiff.Type.CommonModule",
            ["SessionParameter"] = "ConfigDiff.Type.SessionParameter",
            ["FunctionalOption"] = "ConfigDiff.Type.FunctionalOption",
            ["EventSubscription"] = "ConfigDiff.Type.EventSubscription",
            ["ScheduledJob"] = "ConfigDiff.Type.ScheduledJob",
            ["FilterCriterion"] = "ConfigDiff.Type.FilterCriterion",
            ["WebService"] = "ConfigDiff.Type.WebService",
            ["HttpService"] = "ConfigDiff.Type.HttpService",
            ["ExternalDataSource"] = "ConfigDiff.Type.ExternalDataSource",
            ["SettingsStorage"] = "ConfigDiff.Type.SettingsStorage",
            ["Sequence"] = "ConfigDiff.Type.Sequence",
            ["CommonPicture"] = "ConfigDiff.Type.CommonPicture",
            ["Extension"] = "ConfigDiff.Type.Extension"
        };

    /// <summary>
    /// Локализованное отображаемое имя типа по имени его каталога в выгрузке.
    /// Неизвестный каталог возвращается как есть (fallback).
    /// </summary>
    public static string GetDisplayName(string typeDir, Func<string, string> t)
    {
        if (!string.IsNullOrWhiteSpace(typeDir) &&
            TypeKeys.TryGetValue(typeDir, out var key))
        {
            var localized = t(key);
            if (!string.IsNullOrWhiteSpace(localized) && localized != key)
                return localized;
        }

        return typeDir ?? string.Empty;
    }

    /// <summary>
    /// Сортирует набор типов для отчёта: известные — в фиксированном порядке словаря,
    /// неизвестные — в конце (по алфавиту).
    /// </summary>
    public static IEnumerable<string> SortTypes(IEnumerable<string> typeDirs)
    {
        var known = new List<string>();
        var unknown = new List<string>();

        foreach (var typeDir in typeDirs)
        {
            if (TypeKeys.ContainsKey(typeDir))
                known.Add(typeDir);
            else
                unknown.Add(typeDir);
        }

        unknown.Sort(StringComparer.Ordinal);
        return known.Concat(unknown);
    }
}