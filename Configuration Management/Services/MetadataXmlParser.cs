using System.IO;
using System.Xml.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистый ленивый парсер XML-выгрузки конфигурации (<c>/DumpConfigToFiles</c>)
/// для «Обозревателя метаданных» (цикл 0.3.9.132–0.3.9.136). Никаких операций 1С —
/// работает только с файлами на диске, поэтому покрыт юнит-тестами.
///
/// РАЗВЕДКА ФОРМАТА (выполнена 2026-09-29 на реальных платформах 8.3.24.1586 и 8.3.27.2325):
/// <list type="bullet">
/// <item>Пустая конфигурация выгружается как <c>ConfigDumpInfo.xml</c> + <c>Configuration.xml</c>
/// + <c>Languages/<Язык>.xml</c>; каталога <c>Configuration/</c> в ней НЕТ.</item>
/// <item>Объекты (8.3.24+) лежат НА ВЕРХНЕМ УРОВНЕ в каталогах МНОЖЕСТВЕННОГО числа
/// (цикл выгрузка→загрузка для этой структуры подтверждён): <c>Subsystems/Основное.xml</c>
/// (плоский головной файл), <c>Catalogs/Контрагенты.xml</c> + подчинённые в
/// <c>Catalogs/Контрагенты/Attributes/*.xml</c>, вложенные подсистемы —
/// <c>Subsystems/Основное/Subsystems/ПодсистемаВложенная.xml</c>. Головной XML каталога-объекта
/// в новом формате лежит как <c><Тип>s/<Имя>.xml</c> (а НЕ <c><Имя>/<Имя>.xml</c>).</item>
/// <item>Исторический формат (в т.ч. формат, на который опирается
/// <see cref="ConfigurationDiffEngine"/>): каталог <c>Configuration/<Тип>/</c> с
/// файлом-объектом <c><Имя>.xml</c> или каталогом-объектом <c><Имя>/<Имя>.xml</c>.</item>
/// <item><c>Configuration.xml</c>: корень <c>MetaDataObject version="2.17"/"2.20"</c>,
/// <c>Configuration → InternalInfo → Properties (Name/Synonym/Comment/Version) → ChildObjects</c>;
/// пустой синоним — <c><Synonym/></c>.</item>
/// <item>Парсер поддерживает ОБЕ структуры (legacy <c>Configuration/<Тип>/</c> и modern
/// верхнего уровня) и устойчив к битому XML/отсутствующим файлам — возвращает default.</item>
/// </list>
/// Ограничение разведки: <c>LoadConfigFromFiles</c> требует у объектов узел InternalInfo;
/// точная XDTO-схема GeneratedType версий 8.3.24+ (атрибут/элемент category) вручную не
/// восстановлена — на чтение выгрузки это не влияет.
/// </summary>
public static class MetadataXmlParser
{
    /// <summary>Корневой файл конфигурации (имя и версия из Properties).</summary>
    public const string RootConfigurationFileName = "Configuration.xml";

    /// <summary>Каталог объектов в историческом формате выгрузки.</summary>
    private const string LegacyObjectsDirName = "Configuration";

    /// <summary>Каталог языков (верхний уровень, служебный).</summary>
    private const string LanguagesDirName = "Languages";

    /// <summary>Имена подкаталогов подчинённых объектов, по которым считаются счётчики деталей.</summary>
    private static readonly string[] DetailSubDirs =
    {
        "Attributes", "TabularSections", "Forms", "Commands", "Templates"
    };

    /// <summary>Соответствие «тип (единственное число) → каталог верхнего уровня (множественное)»
    /// для современного формата выгрузки 8.3.24+ (разведка: Subsystems, Catalogs, Documents, …).
    /// Для неизвестных типов каталог считается по имени как есть.</summary>
    private static readonly IReadOnlyDictionary<string, string> PluralDirOf =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Document"] = "Documents",
            ["DocumentJournal"] = "DocumentJournals",
            ["Catalog"] = "Catalogs",
            ["ChartOfCharacteristicTypes"] = "ChartOfCharacteristicTypes",
            ["ChartOfAccounts"] = "ChartOfAccounts",
            ["ChartOfCalculationTypes"] = "ChartOfCalculationTypes",
            ["Constant"] = "Constants",
            ["Enum"] = "Enums",
            ["InformationRegister"] = "InformationRegisters",
            ["AccumulationRegister"] = "AccumulationRegisters",
            ["AccountingRegister"] = "AccountingRegisters",
            ["CalculationRegister"] = "CalculationRegisters",
            ["BusinessProcess"] = "BusinessProcesses",
            ["Task"] = "Tasks",
            ["DataProcessor"] = "DataProcessors",
            ["Report"] = "Reports",
            ["Role"] = "Roles",
            ["CommonModule"] = "CommonModules",
            ["SessionParameter"] = "SessionParameters",
            ["FunctionalOption"] = "FunctionalOptions",
            ["EventSubscription"] = "EventSubscriptions",
            ["ScheduledJob"] = "ScheduledJobs",
            ["FilterCriterion"] = "FilterCriteria",
            ["WebService"] = "WebServices",
            ["HttpService"] = "HttpServices",
            ["ExternalDataSource"] = "ExternalDataSources",
            ["SettingsStorage"] = "SettingsStorages",
            ["Sequence"] = "Sequences",
            ["CommonPicture"] = "CommonPictures",
            ["Subsystem"] = "Subsystems",
            ["Extension"] = "Extensions"
        };

    /// <summary>Обратный словарь: каталог верхнего уровня → имя типа (единственное число).</summary>
    private static readonly IReadOnlyDictionary<string, string> SingularDirOf =
        PluralDirOf.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    /// <summary>Служебные каталоги верхнего уровня, не являющиеся типами метаданных.</summary>
    private static readonly string[] NonTypeRootDirs = { LanguagesDirName, "DT-INF", ".settings", "Configuration" };

    /// <summary>Вариант структуры выгрузки (см. разведку в шапке класса).</summary>
    internal enum DumpLayout
    {
        /// <summary>Исторический: <c>Configuration/<Тип>/<Имя></c>.</summary>
        Legacy,

        /// <summary>Современный (8.3.24+): каталоги типов верхнего уровня во множественном числе.</summary>
        Modern
    }

    /// <summary>Заголовок конфигурации: имя и версия (пустые строки, если нет файла/данных).</summary>
    public static (string Name, string Version) ParseConfigurationHeader(string dumpRoot)
    {
        var doc = LoadXmlSafe(Path.Combine(dumpRoot ?? string.Empty, RootConfigurationFileName));
        if (doc is null)
            return (string.Empty, string.Empty);

        var properties = FindChild(doc.Root, "Configuration") is { } configuration
            ? FindChild(configuration, "Properties")
            : null;
        if (properties is null)
            return (string.Empty, string.Empty);

        return (
            TextOf(FindChild(properties, "Name")),
            TextOf(FindChild(properties, "Version")));
    }

    /// <summary>
    /// Возвращает имена типов метаданных в выгрузке (каталоги первого уровня под
    /// <c>Configuration/</c> в историческом формате либо каталоги типов верхнего уровня
    /// в современном), нормализованные к единственному числу и упорядоченные
    /// <see cref="MetadataTypeLocalizer.SortTypes"/> (известные — в фиксированном порядке,
    /// неизвестные — в конце). Пустой список, если выгрузка пустая/не найдена.
    /// </summary>
    public static IReadOnlyList<string> EnumerateTypeDirs(string dumpRoot)
    {
        if (string.IsNullOrWhiteSpace(dumpRoot) || !Directory.Exists(dumpRoot))
            return Array.Empty<string>();

        var layout = DetectLayout(dumpRoot);
        IEnumerable<string?> dirs;

        if (layout == DumpLayout.Legacy)
        {
            var objectsRoot = Path.Combine(dumpRoot, LegacyObjectsDirName);
            dirs = Directory.Exists(objectsRoot)
                ? Directory.EnumerateDirectories(objectsRoot).Select(Path.GetFileName)
                : Enumerable.Empty<string>();
        }
        else
        {
            dirs = Directory.EnumerateDirectories(dumpRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name) &&
                               !NonTypeRootDirs.Contains(name!, StringComparer.Ordinal) &&
                               !IsSystemFileLike(name!))
                .Select(name => NormalizeTypeDir(name!));
        }

        // SortTypes упорядочивает только «известные первыми, неизвестные последними»;
        // сам порядок известных сохраняет порядок входа — сортируем по алфавиту заранее,
        // чтобы результат был детерминирован независимо от порядка каталогов файловой системы.
        return MetadataTypeLocalizer.SortTypes(
                dirs.Where(d => !string.IsNullOrWhiteSpace(d))
                    .Select(d => d!)
                    .OrderBy(d => d, StringComparer.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Обходит записи каталога типа (как <c>ConfigurationDiffEngine.BuildSnapshot</c>):
    /// файловый объект <c><Имя>.xml</c> или каталог-объект <c><Имя>/</c> с суммой
    /// размеров и числом файлов. Современный формат: головной файл <c><Имя>.xml</c>;
    /// каталог <c><Имя>/</c> содержит подчинённых (<see cref="MetadataObjectSummary.HasNested"/>).
    /// </summary>
    public static IReadOnlyList<MetadataObjectSummary> EnumerateObjects(string dumpRoot, string typeDir)
    {
        if (string.IsNullOrWhiteSpace(dumpRoot) || string.IsNullOrWhiteSpace(typeDir))
            return Array.Empty<MetadataObjectSummary>();

        var layout = DetectLayout(dumpRoot);
        var typeDirPath = GetTypeDirPath(dumpRoot, layout, typeDir);
        if (!Directory.Exists(typeDirPath))
            return Array.Empty<MetadataObjectSummary>();

        var result = new List<MetadataObjectSummary>();
        var fileNames = new HashSet<string>(StringComparer.Ordinal);

        // Файловые объекты (<Имя>.xml) — в обоих форматах запись верхнего уровня.
        foreach (var file in Directory.EnumerateFiles(typeDirPath, "*.xml", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(name) || IsSystemFileLike(name))
                continue;
            fileNames.Add(name);
            var info = new FileInfo(file);
            result.Add(new MetadataObjectSummary
            {
                TypeDir = typeDir,
                Name = name,
                HasNested = Directory.Exists(Path.Combine(typeDirPath, name)),
                FileCount = 1,
                TotalBytes = info.Exists ? info.Length : 0,
                RelPath = BuildRelPath(dumpRoot, layout, typeDir, name)
            });
        }

        // Каталоги-объекты (исторический формат); в современном — только те, у которых нет
        // головного файла (чтобы объект не дублировался).
        foreach (var dir in Directory.EnumerateDirectories(typeDirPath))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(name) || fileNames.Contains(name) || IsSystemFileLike(name))
                continue;

            var (fileCount, totalBytes) = MeasureDirectory(dir);
            result.Add(new MetadataObjectSummary
            {
                TypeDir = typeDir,
                Name = name,
                HasNested = true,
                FileCount = fileCount,
                TotalBytes = totalBytes,
                RelPath = BuildRelPath(dumpRoot, layout, typeDir, name)
            });
        }

        return result
            .OrderBy(o => o.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Детали объекта: свойства головного XML (имя, синоним — первый <c>v8:item</c>
    /// с учётом <c>v8:lang</c> (приоритет <c>ru</c>), комментарий, иерархичность)
    /// и счётчики подчинённых по подкаталогам <c>Attributes/</c>, <c>TabularSections/</c>,
    /// <c>Forms/</c>, <c>Commands/</c>, <c>Templates/</c> (число файлов <c>*.xml</c>);
    /// для файлового объекта исторического формата — по <c>ChildObjects</c> головного XML.
    /// Тела модулей (<c>Ext/Modules</c>) НЕ читаются. Битый XML/отсутствующие данные → default.
    /// </summary>
    public static MetadataObjectDetails ReadObjectDetails(string dumpRoot, string typeDir, string name)
    {
        name = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(dumpRoot) || string.IsNullOrWhiteSpace(typeDir) || string.IsNullOrWhiteSpace(name))
            return CreateEmptyDetails(name, dumpRoot, typeDir);

        var layout = DetectLayout(dumpRoot);
        var typeDirPath = GetTypeDirPath(dumpRoot, layout, typeDir);
        if (!Directory.Exists(typeDirPath))
            return CreateEmptyDetails(name, dumpRoot, typeDir);

        // Головной XML: исторический каталог-объект — <TypeDir>/<Имя>/<Имя>.xml (разведка:
        // в современном формате головной XML лежит как <Тип>s/<Имя>.xml, каталог <Имя>/
        // содержит только подчинённых).
        var headPath = Path.Combine(typeDirPath, name + ".xml");
        var nestedDir = Path.Combine(typeDirPath, name);
        if (!File.Exists(headPath))
            headPath = Path.Combine(nestedDir, name + ".xml");

        var doc = LoadXmlSafe(headPath);
        // Головной XML: корень MetaDataObject → узел объекта (Catalog/Document/…) → Properties.
        var objectNode = doc?.Root?.Elements().FirstOrDefault();
        var properties = FindChild(objectNode, "Properties");
        var details = new MetadataObjectDetails
        {
            Name = TextOf(FindChild(properties, "Name")).Length > 0 ? TextOf(FindChild(properties, "Name")) : name,
            Synonym = SelectSynonym(FindChild(properties, "Synonym")),
            Comment = TextOf(FindChild(properties, "Comment")),
            IsHierarchical = BoolOf(FindChild(properties, "Hierarchical")),
            IsOrderedHierarchical = BoolOf(FindChild(properties, "OrderedHierarchical")),
            AttributeCount = 0,
            TabularSectionCount = 0,
            FormCount = 0,
            CommandCount = 0,
            TemplateCount = 0,
            TotalBytes = MeasureEntry(headPath, nestedDir),
            RelPath = BuildRelPath(dumpRoot, layout, typeDir, name)
        };

        if (Directory.Exists(nestedDir))
        {
            // Каталог-объект (или современный контейнер подчинённых): счётчики по файлам.
            details = details with
            {
                AttributeCount = CountXmlFiles(Path.Combine(nestedDir, "Attributes")),
                TabularSectionCount = CountXmlFiles(Path.Combine(nestedDir, "TabularSections")),
                FormCount = CountXmlFiles(Path.Combine(nestedDir, "Forms")),
                CommandCount = CountXmlFiles(Path.Combine(nestedDir, "Commands")),
                TemplateCount = CountXmlFiles(Path.Combine(nestedDir, "Templates")),
                TotalBytes = MeasureEntry(headPath, nestedDir)
            };
        }
        else if (objectNode is not null)
        {
        	// Файловый объект исторического формата: подчинённые перечислены в ChildObjects.
        	var childObjects = FindChild(objectNode, "ChildObjects");
            details = details with
            {
                AttributeCount = CountChildObjects(childObjects, "Attribute"),
                TabularSectionCount = CountChildObjects(childObjects, "TabularSection"),
                FormCount = CountChildObjects(childObjects, "Form"),
                CommandCount = CountChildObjects(childObjects, "Command"),
                TemplateCount = CountChildObjects(childObjects, "Template")
            };
        }

        return details;
    }

    /// <summary>
    /// Дерево подсистем конфигурации: верхний уровень — подсистемы из каталога подсистем
    /// (исторический <c>Configuration/Subsystem/</c> или современный <c>Subsystems/</c>),
    /// вложенность — по <c>ChildObjects/Subsystem</c> головного XML, состав —
    /// <c>Content/v8:item/v8:content</c> (полные имена объектов вида <c>Catalog.Контрагенты</c>).
    /// В современном формате вложенная подсистема лежит в подкаталоге родителя
    /// (<c>Subsystems/<Родитель>/Subsystems/<Имя>.xml</c> — разведка). Пустой список,
    /// если подсистем нет.
    /// </summary>
    public static IReadOnlyList<MetadataSubsystem> ReadSubsystems(string dumpRoot)
    {
        if (string.IsNullOrWhiteSpace(dumpRoot) || !Directory.Exists(dumpRoot))
            return Array.Empty<MetadataSubsystem>();

        var layout = DetectLayout(dumpRoot);
        var subDir = layout == DumpLayout.Legacy
            ? Path.Combine(dumpRoot, LegacyObjectsDirName, "Subsystem")
            : Path.Combine(dumpRoot, PluralDirOf["Subsystem"]);
        if (!Directory.Exists(subDir))
            return Array.Empty<MetadataSubsystem>();

        // Все файлы подсистем: имя файла → путь (вложенные могут лежать в подкаталогах).
        var filesByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(subDir, "*.xml", SearchOption.AllDirectories))
        {
            var baseName = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(baseName) && !filesByName.ContainsKey(baseName))
                filesByName[baseName] = file;
        }

        // Верхний уровень — файлы прямо в каталоге подсистем; вложенные исключаются из корня.
        var usedAsChild = new HashSet<string>(StringComparer.Ordinal);
        var roots = new List<MetadataSubsystem>();
        foreach (var file in Directory.EnumerateFiles(subDir, "*.xml", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(name) || IsSystemFileLike(name))
                continue;
            var node = BuildSubsystemNode(name, filesByName, usedAsChild, prefix: null);
            if (node is not null)
                roots.Add(node);
        }

        // Legacy: вложенные лежат плоско — отсекаем тех, кто оказался дочерним.
        return roots.Where(r => !usedAsChild.Contains(r.Name)).ToList();
    }

    // ------------------------------------------------------------------
    // Внутренние помощники
    // ------------------------------------------------------------------

    /// <summary>Определяет вариант структуры выгрузки по наличию каталога Configuration/.</summary>
    private static DumpLayout DetectLayout(string dumpRoot)
    {
        var legacy = Path.Combine(dumpRoot, LegacyObjectsDirName);
        return Directory.Exists(legacy)
               && Directory.EnumerateDirectories(legacy).Any()
            ? DumpLayout.Legacy
            : DumpLayout.Modern;
    }

    /// <summary>Каталог типа: исторический <c>Configuration/<Тип></c> либо <c><Тип>s</c> верхнего уровня.</summary>
    private static string GetTypeDirPath(string dumpRoot, DumpLayout layout, string typeDir)
        => layout == DumpLayout.Legacy
            ? Path.Combine(dumpRoot, LegacyObjectsDirName, typeDir)
            : Path.Combine(dumpRoot, PluralDir(typeDir));

    /// <summary>Относительный путь объекта для панели деталей (без расширения).</summary>
    private static string BuildRelPath(string dumpRoot, DumpLayout layout, string typeDir, string name)
        => layout == DumpLayout.Legacy
            ? $"{LegacyObjectsDirName}/{typeDir}/{name}"
            : $"{PluralDir(typeDir)}/{name}";

    /// <summary>Имя каталога верхнего уровня для типа (множественное число; fallback — как есть).</summary>
    private static string PluralDir(string typeDir)
        => PluralDirOf.TryGetValue(typeDir, out var plural) ? plural : typeDir;

    /// <summary>Нормализует имя каталога верхнего уровня к единственному числу типа.</summary>
    private static string NormalizeTypeDir(string dirName)
        => SingularDirOf.TryGetValue(dirName, out var singular) ? singular : dirName;

    /// <summary>Нормализует имя объекта: без расширения файла.</summary>
    private static string NormalizeName(string name)
        => Path.GetFileNameWithoutExtension(name ?? string.Empty);

    /// <summary>Число файлов *.xml в каталоге (без подкаталогов; 0, если каталога нет).</summary>
    private static int CountXmlFiles(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.xml", SearchOption.TopDirectoryOnly).Count() : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Число дочерних элементов с заданным локальным именем (ChildObjects файлового объекта).</summary>
    private static int CountChildObjects(XElement? childObjects, string localName)
        => childObjects?.Elements().Count(e => e.Name.LocalName == localName) ?? 0;

    /// <summary>Синоним: первый v8:item/v8:content, приоритет item с v8:lang == ru.</summary>
    private static string SelectSynonym(XElement? synonym)
    {
        var items = synonym?.Elements().Where(e => e.Name.LocalName == "item").ToList();
        if (items is null || items.Count == 0)
            return string.Empty;

        var preferred = items.FirstOrDefault(i =>
            string.Equals(TextOf(FindChild(i, "lang")), "ru", StringComparison.OrdinalIgnoreCase));
        var chosen = preferred ?? items[0];
        return TextOf(FindChild(chosen, "content"));
    }

    /// <summary>Сумма числа файлов и размера каталога (рекурсивно, *.xml; для измерений объекта).</summary>
    private static (int FileCount, long TotalBytes) MeasureDirectory(string dir)
    {
        var count = 0;
        long bytes = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
            {
                count++;
                var info = new FileInfo(file);
                if (info.Exists)
                    bytes += info.Length;
            }
        }
        catch
        {
            // Игнорируем частичные ошибки доступа — считаем то, что удалось.
        }
        return (count, bytes);
    }

    /// <summary>Размер «входа» объекта: файл или сумма по каталогу (для TotalBytes деталей).</summary>
    private static long MeasureEntry(string headPath, string nestedDir)
    {
        try
        {
            if (File.Exists(headPath))
                return new FileInfo(headPath).Length;
            if (Directory.Exists(nestedDir))
                return MeasureDirectory(nestedDir).TotalBytes;
        }
        catch
        {
            // Игнорируем.
        }
        return 0;
    }

    /// <summary>Загружает XML-документ; битый/отсутствующий файл → null (не роняет).</summary>
    private static XDocument? LoadXmlSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            return XDocument.Load(stream, LoadOptions.None);
        }
        catch
        {
            // Битый XML (незакрытые теги, неверная кодировка и т.п.) → default.
            return null;
        }
    }

    /// <summary>Дочерний элемент по локальному имени (без учёта пространства имён).</summary>
    private static XElement? FindChild(XElement? parent, string localName)
        => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    /// <summary>Текст элемента (trimmed; пусто, если элемента нет).</summary>
    private static string TextOf(XElement? element)
        => element?.Value?.Trim() ?? string.Empty;

    /// <summary>Логическое значение элемента (false, если отсутствует/не парсится).</summary>
    private static bool BoolOf(XElement? element)
        => bool.TryParse(TextOf(element), out var value) && value;

    /// <summary>Истина для служебных имён выгрузки (ConfigDumpInfo и подобные).</summary>
    private static bool IsSystemFileLike(string name)
        => name.Equals(ConfigurationDiffEngine.ConfigDumpInfoFileName, StringComparison.OrdinalIgnoreCase)
           || name.Equals(RootConfigurationFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Строит узел подсистемы: головной XML ищется по имени среди всех файлов каталога
    /// подсистем (плоско в историческом формате, в подкаталоге родителя — в современном);
    /// дети — по ChildObjects/Subsystem. <paramref name="usedAsChild"/> собирает имена,
    /// ставшие дочерними, чтобы исключить их из верхнего уровня (актуально для legacy-плоскости).
    /// </summary>
    private static MetadataSubsystem? BuildSubsystemNode(
        string name,
        IReadOnlyDictionary<string, string> filesByName,
        ISet<string> usedAsChild,
        string? prefix)
    {
        if (!filesByName.TryGetValue(name, out var headPath))
            return null;

        var doc = LoadXmlSafe(headPath);
        var objectNode = doc?.Root?.Elements().FirstOrDefault();
        var properties = FindChild(objectNode, "Properties");
        var synonym = SelectSynonym(FindChild(properties, "Synonym"));
        var comment = TextOf(FindChild(properties, "Comment"));

        var key = string.IsNullOrWhiteSpace(prefix)
            ? $"Subsystem.{name}"
            : $"{prefix}.{name}";

        var contentKeys = new List<string>();
        var content = FindChild(properties, "Content");
        if (content is not null)
        {
            foreach (var item in content.Elements().Where(e => e.Name.LocalName == "item"))
            {
                var value = TextOf(FindChild(item, "content"));
                if (value.Length > 0)
                    contentKeys.Add(value);
            }
        }

        var children = new List<MetadataSubsystem>();
        var childNames = FindChild(objectNode, "ChildObjects")?
            .Elements()
            .Where(e => e.Name.LocalName == "Subsystem")
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .ToList() ?? new List<string>();
        foreach (var childName in childNames)
        {
            var child = BuildSubsystemNode(childName, filesByName, usedAsChild, prefix: key);
            if (child is not null)
            {
                children.Add(child);
                usedAsChild.Add(childName);
            }
        }

        return new MetadataSubsystem
        {
            Name = name,
            Synonym = synonym,
            Comment = comment,
            Key = key,
            ContentKeys = contentKeys,
            Children = children
        };
    }

    /// <summary>Пустые детали (битый XML/нет файла) с корректным путём.</summary>
    private static MetadataObjectDetails CreateEmptyDetails(string name, string dumpRoot, string typeDir)
    {
        var layout = DetectLayout(dumpRoot);
        return new MetadataObjectDetails
        {
            Name = name,
            Synonym = string.Empty,
            Comment = string.Empty,
            IsHierarchical = false,
            IsOrderedHierarchical = false,
            AttributeCount = 0,
            TabularSectionCount = 0,
            FormCount = 0,
            CommandCount = 0,
            TemplateCount = 0,
            TotalBytes = 0,
            RelPath = BuildRelPath(dumpRoot, layout, typeDir, name)
        };
    }
}