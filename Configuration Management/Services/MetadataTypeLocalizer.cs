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
    /// Алиасы «каталог верхнего уровня в МНОЖЕСТВЕННОМ числе (формат выгрузки 8.3.24+:
    /// <c>Subsystems/</c>, <c>Catalogs/</c>, <c>Documents/</c>) → ключ локализации».
    /// Сверить с фактическими каталогами выгрузки 8.3.24+/8.3.27 (разведка этапа 1):
    /// к единственному числу добавляется «s» (Documents, Catalogs, Constants, Tasks, …),
    /// заканчивающиеся на «y» дают «ies» (Enums → Enums — без изменений: слово уже на «s»),
    /// ChartOf* / InformationRegister и подобные — без изменений (число совпадает).
    /// Словарь нужен для устойчивости UI: если имя каталога типа приходит как есть
    /// (без нормализации <see cref="MetadataXmlParser"/>), оно всё равно локализуется.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PluralTypeKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Documents"] = "ConfigDiff.Type.Document",
            ["DocumentJournals"] = "ConfigDiff.Type.DocumentJournal",
            ["Catalogs"] = "ConfigDiff.Type.Catalog",
            ["ChartOfCharacteristicTypes"] = "ConfigDiff.Type.ChartOfCharacteristicTypes",
            ["ChartOfAccounts"] = "ConfigDiff.Type.ChartOfAccounts",
            ["ChartOfCalculationTypes"] = "ConfigDiff.Type.ChartOfCalculationTypes",
            ["Constants"] = "ConfigDiff.Type.Constant",
            ["Enums"] = "ConfigDiff.Type.Enum",
            ["InformationRegisters"] = "ConfigDiff.Type.InformationRegister",
            ["AccumulationRegisters"] = "ConfigDiff.Type.AccumulationRegister",
            ["AccountingRegisters"] = "ConfigDiff.Type.AccountingRegister",
            ["CalculationRegisters"] = "ConfigDiff.Type.CalculationRegister",
            ["BusinessProcesses"] = "ConfigDiff.Type.BusinessProcess",
            ["Tasks"] = "ConfigDiff.Type.Task",
            ["DataProcessors"] = "ConfigDiff.Type.DataProcessor",
            ["Reports"] = "ConfigDiff.Type.Report",
            ["Roles"] = "ConfigDiff.Type.Role",
            ["CommonModules"] = "ConfigDiff.Type.CommonModule",
            ["SessionParameters"] = "ConfigDiff.Type.SessionParameter",
            ["FunctionalOptions"] = "ConfigDiff.Type.FunctionalOption",
            ["EventSubscriptions"] = "ConfigDiff.Type.EventSubscription",
            ["ScheduledJobs"] = "ConfigDiff.Type.ScheduledJob",
            ["FilterCriteria"] = "ConfigDiff.Type.FilterCriterion",
            ["WebServices"] = "ConfigDiff.Type.WebService",
            ["HttpServices"] = "ConfigDiff.Type.HttpService",
            ["ExternalDataSources"] = "ConfigDiff.Type.ExternalDataSource",
            ["SettingsStorages"] = "ConfigDiff.Type.SettingsStorage",
            ["Sequences"] = "ConfigDiff.Type.Sequence",
            ["CommonPictures"] = "ConfigDiff.Type.CommonPicture",
            ["Subsystems"] = "ConfigDiff.Type.Subsystem",
            ["Extensions"] = "ConfigDiff.Type.Extension"
        };

    /// <summary>
    /// Локализованное отображаемое имя типа по имени его каталога в выгрузке.
    /// Принимает как единственное число (исторический формат <c>Configuration/Catalog/</c>),
    /// так и множественное (формат 8.3.24+ <c>Catalogs/</c>, см. <see cref="PluralTypeKeys"/> —
    /// алиасы «множественное → ключ локализации», чтобы имя каталога локализовалось даже
    /// без нормализации парсером). Неизвестный каталог возвращается как есть (fallback).
    /// </summary>
    public static string GetDisplayName(string typeDir, Func<string, string> t)
    {
        if (!string.IsNullOrWhiteSpace(typeDir) &&
            (TypeKeys.TryGetValue(typeDir, out var key) ||
             PluralTypeKeys.TryGetValue(typeDir, out key)))
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