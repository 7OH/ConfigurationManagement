using System.IO;

namespace Configuration_Management.Models;

/// <summary>
/// Модели «Обозревателя метаданных конфигурации» (цикл 0.3.9.132–0.3.9.136).
/// Чистые записи без зависимостей от UI и платформы: обозреватель читает XML-выгрузку
/// конфигурации, полученную ключом <c>/DumpConfigToFiles</c> (см.
/// <see cref="Services.MetadataExplorerService"/> и <see cref="Services.MetadataXmlParser"/>).
/// ВНИМАНИЕ: это НЕ модель <see cref="Infobase.MetadataRoot"/> (та заполняется из COM-диалога
/// свойств базы и имеет структуру «типы → объекты»); здесь источник — каталог выгрузки
/// с иерархией «подсистемы → типы → объекты».
/// </summary>
public sealed record MetadataObjectSummary
{
    /// <summary>Имя каталога типа в выгрузке (например <c>Catalog</c> для справочников).</summary>
    public required string TypeDir { get; init; }

    /// <summary>Имя объекта метаданных (файл <c><Имя>.xml</c> или каталог <c><Имя>/</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Есть ли у объекта подчинённые объекты (каталог-объект, а не файловый).</summary>
    public required bool HasNested { get; init; }

    /// <summary>Число файлов объекта (1 для файлового; сумма по подкаталогам для каталога-объекта).</summary>
    public required int FileCount { get; init; }

    /// <summary>Суммарный размер файлов объекта в байтах.</summary>
    public required long TotalBytes { get; init; }

    /// <summary>Относительный путь объекта в выгрузке (<c>Configuration/<Тип>/<Имя></c> или <c><Тип>s/<Имя></c>).</summary>
    public required string RelPath { get; init; }
}

/// <summary>
/// Детали объекта метаданных (панель деталей обозревателя): свойства головного XML
/// (имя, синоним, комментарий, иерархичность) и счётчики подчинённых объектов
/// (реквизиты, табличные части, формы, команды, шаблоны — по именам файлов
/// <c>*.xml</c> в подкаталогах объекта). Модули и макеты НЕ читаются — только факт
/// существования.
/// </summary>
public sealed record MetadataObjectDetails
{
    /// <summary>Имя объекта метаданных (из Properties/Name головного XML).</summary>
    public required string Name { get; init; }

    /// <summary>Синоним объекта: первый <c>v8:item/v8:content</c> с учётом <c>v8:lang</c>; пусто, если не задан.</summary>
    public required string Synonym { get; init; }

    /// <summary>Комментарий к объекту (Properties/Comment); пусто, если не задан.</summary>
    public required string Comment { get; init; }

    /// <summary>Признак иерархичности (Properties/Hierarchical — справочники и т.п.).</summary>
    public required bool IsHierarchical { get; init; }

    /// <summary>Признак упорядоченной иерархичности (Properties/OrderedHierarchical).</summary>
    public required bool IsOrderedHierarchical { get; init; }

    /// <summary>Число реквизитов (файлы <c>*.xml</c> в подкаталоге Attributes/).</summary>
    public required int AttributeCount { get; init; }

    /// <summary>Число табличных частей (файлы <c>*.xml</c> в подкаталоге TabularSections/).</summary>
    public required int TabularSectionCount { get; init; }

    /// <summary>Число форм (файлы <c>*.xml</c> в подкаталоге Forms/).</summary>
    public required int FormCount { get; init; }

    /// <summary>Число команд (файлы <c>*.xml</c> в подкаталоге Commands/).</summary>
    public required int CommandCount { get; init; }

    /// <summary>Число шаблонов (файлы <c>*.xml</c> в подкаталоге Templates/).</summary>
    public required int TemplateCount { get; init; }

    /// <summary>Суммарный размер файлов объекта в байтах.</summary>
    public required long TotalBytes { get; init; }

    /// <summary>Относительный путь объекта в выгрузке.</summary>
    public required string RelPath { get; init; }
}

/// <summary>
/// Подсистема конфигурации: свойства (имя, синоним, комментарий), состав
/// (полные имена объектов вида <c>Catalog.Контрагенты</c> из Content/v8:item/v8:content)
/// и вложенные подсистемы (ChildObjects/Subsystem). <see cref="Key"/> —
/// уникальный ключ дерева <c>Subsystem.<ПолноеИмя></c>.
/// </summary>
public sealed record MetadataSubsystem
{
    /// <summary>Имя подсистемы (Properties/Name).</summary>
    public required string Name { get; init; }

    /// <summary>Синоним подсистемы (Properties/Synonym, первый item).</summary>
    public required string Synonym { get; init; }

    /// <summary>Комментарий (Properties/Comment).</summary>
    public required string Comment { get; init; }

    /// <summary>Ключ дерева: <c>Subsystem.<ПолноеИмя></c> (например <c>Subsystem.Основное</c>).</summary>
    public required string Key { get; init; }

    /// <summary>Полные имена объектов состава: <c>Catalog.Контрагенты</c>, <c>Document.ЗаказКлиента</c>.</summary>
    public required IReadOnlyList<string> ContentKeys { get; init; }

    /// <summary>Вложенные подсистемы (пустой список, если нет).</summary>
    public required IReadOnlyList<MetadataSubsystem> Children { get; init; }
}

/// <summary>
/// Результат выгрузки конфигурации: путь к каталогу XML-выгрузки и заголовок
/// (имя/версия конфигурации из корневого <c>Configuration.xml</c>). Каталог НЕ удаляется
/// автоматически — владелец (окно обозревателя) вызывает <see cref="Delete"/> при закрытии.
/// </summary>
public sealed class MetadataDump
{
    public MetadataDump(string rootPath, string configurationName, string configurationVersion)
    {
        RootPath = rootPath;
        ConfigurationName = configurationName;
        ConfigurationVersion = configurationVersion;
    }

    /// <summary>Путь к корню выгрузки (<c>%TEMP%\cm_metaeplorer_<guid></c>).</summary>
    public string RootPath { get; }

    /// <summary>Имя конфигурации (Properties/Name корневого Configuration.xml).</summary>
    public string ConfigurationName { get; }

    /// <summary>Версия конфигурации (Properties/Version корневого Configuration.xml).</summary>
    public string ConfigurationVersion { get; }

    /// <summary>
    /// Рекурсивно удаляет каталог выгрузки; ошибки игнорируются (образец
    /// <c>ConfigurationDiffService.TryDeleteDirectory</c>: лучше осиротевший каталог в %TEMP%,
    /// чем падение приложения; занятые процессы/антивирус помешают — попытка при следующем
    /// запуске очистит сама).
    /// </summary>
    public void Delete()
    {
        try
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // Занят процессом/антивирусом — ошибки игнорируем.
        }
    }
}

/// <summary>
/// Ошибка операции «Обозревателя метаданных» с человекочитаемым сообщением
/// (образец <c>ConfigurationDiffException</c>): недоступная база, отсутствующий/битый .cf,
/// таймаут выгрузки, текст лога 1С из <c>DesignerBatchInfo.ErrorMessage</c>.
/// </summary>
public sealed class MetadataExplorerException : Exception
{
    public MetadataExplorerException(string message) : base(message) { }
}