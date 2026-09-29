using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сервисного слоя «Обозревателя метаданных» (0.3.9.132): чистого парсера
/// XML-выгрузки <c>/DumpConfigToFiles</c> (<see cref="MetadataXmlParser"/>) и модели
/// <see cref="MetadataDump"/>. Без реальных операций 1С — выгрузки строятся на лету
/// во временной папке теста в ОБОИХ фактических форматах (см. разведку 8.3.24/8.3.27):
/// <list type="bullet">
/// <item>современный (верхний уровень): <c>Catalogs/Контрагенты.xml</c> + подчинённые в
/// <c>Catalogs/Контрагенты/Attributes/*.xml</c>, вложенные подсистемы —
/// <c>Subsystems/<Родитель>/Subsystems/<Имя>.xml</c>;</item>
/// <item>исторический: <c>Configuration/<Тип>/<Имя>.xml</c> или
/// <c>Configuration/<Тип>/<Имя>/<Имя>.xml</c>.</item>
/// </list>
/// </summary>
public sealed class MetadataExplorerTests : IDisposable
{
    private readonly string _tempRoot;

    public MetadataExplorerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cm_meta_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>Шапка XML-файла объекта выгрузки (namespace из реальной выгрузки 8.3.27).</summary>
    private const string Ns =
        "<MetaDataObject xmlns=\"http://v8.1c.ru/8.3/MDClasses\" " +
        "xmlns:app=\"http://v8.1c.ru/8.2/managed-application/core\" " +
        "xmlns:v8=\"http://v8.1c.ru/8.1/data/core\" " +
        "xmlns:xr=\"http://v8.1c.ru/8.3/xcf/readable\" version=\"2.20\">";

    // ------------------------------------------------------------------
    // ParseConfigurationHeader
    // ------------------------------------------------------------------

    [Fact]
    public void ParseConfigurationHeader_ReadsNameAndVersion()
    {
        WriteFile(Path.Combine(_tempRoot, "Configuration.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Configuration>" +
            "<Properties><Name>Разведка</Name><Synonym/><Comment/>" +
            "<Version>1.2.3.4</Version></Properties>" +
            "<ChildObjects/></Configuration></MetaDataObject>");

        var (name, version) = MetadataXmlParser.ParseConfigurationHeader(_tempRoot);

        Assert.Equal("Разведка", name);
        Assert.Equal("1.2.3.4", version);
    }

    [Fact]
    public void ParseConfigurationHeader_MissingFile_ReturnsDefaults()
    {
        var (name, version) = MetadataXmlParser.ParseConfigurationHeader(_tempRoot);

        Assert.Equal(string.Empty, name);
        Assert.Equal(string.Empty, version);
    }

    [Fact]
    public void ParseConfigurationHeader_BrokenXml_ReturnsDefaults()
    {
        WriteFile(Path.Combine(_tempRoot, "Configuration.xml"), "<?xml version=\"1.0\"?><Configuration><Properties><Name>");

        var (name, version) = MetadataXmlParser.ParseConfigurationHeader(_tempRoot);

        Assert.Equal(string.Empty, name);
        Assert.Equal(string.Empty, version);
    }

    // ------------------------------------------------------------------
    // EnumerateTypeDirs
    // ------------------------------------------------------------------

    [Fact]
    public void EnumerateTypeDirs_Modern_SortsKnownTypesAndSkipsLanguages()
    {
        // Современный формат: каталоги типов верхнего уровня во множественном числе.
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Documents"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Catalogs"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Reports"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Languages"));

        var types = MetadataXmlParser.EnumerateTypeDirs(_tempRoot);

        // Известные типы упорядочены по алфавиту, служебный Languages исключён.
        Assert.Equal(new[] { "Catalog", "Document", "Report" }, types);
    }

    [Fact]
    public void EnumerateTypeDirs_Modern_UnknownTypesAtTheEnd()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Documents"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "FutureType"));

        var types = MetadataXmlParser.EnumerateTypeDirs(_tempRoot);

        Assert.Equal(2, types.Count);
        Assert.Equal("Document", types[0]);   // известный — первым
        Assert.Equal("FutureType", types[1]); // неизвестный — в конце
    }

    [Fact]
    public void EnumerateTypeDirs_Legacy_ReadsConfigurationSubdirs()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Configuration", "Document"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Configuration", "Catalog"));

        var types = MetadataXmlParser.EnumerateTypeDirs(_tempRoot);

        Assert.Equal(new[] { "Catalog", "Document" }, types);
    }

    [Fact]
    public void EnumerateTypeDirs_MissingRoot_ReturnsEmpty()
    {
        Assert.Empty(MetadataXmlParser.EnumerateTypeDirs(Path.Combine(_tempRoot, "missing")));
    }

    // ------------------------------------------------------------------
    // EnumerateObjects
    // ------------------------------------------------------------------

    [Fact]
    public void EnumerateObjects_Modern_FileAndNestedContainer()
    {
        // Файловый объект документа + каталог-объект справочника с подчинёнными.
        WriteFile(Path.Combine(_tempRoot, "Documents", "ЗаказКлиента.xml"), "<Doc/>");
        WriteFile(Path.Combine(_tempRoot, "Catalogs", "Контрагенты.xml"), "<Cat/>");
        WriteFile(Path.Combine(_tempRoot, "Catalogs", "Контрагенты", "Attributes", "Наименование.xml"), "<Attr/>");

        var docs = MetadataXmlParser.EnumerateObjects(_tempRoot, "Document");
        var cats = MetadataXmlParser.EnumerateObjects(_tempRoot, "Catalog");

        var doc = Assert.Single(docs);
        Assert.Equal("ЗаказКлиента", doc.Name);
        Assert.False(doc.HasNested);
        Assert.Equal(1, doc.FileCount);
        Assert.True(doc.TotalBytes > 0);
        Assert.Equal("Documents/ЗаказКлиента", doc.RelPath);

        var cat = Assert.Single(cats);
        Assert.Equal("Контрагенты", cat.Name);
        Assert.True(cat.HasNested);
        Assert.Equal(1, cat.FileCount);
        Assert.Equal("Catalogs/Контрагенты", cat.RelPath);
    }

    [Fact]
    public void EnumerateObjects_Legacy_FileVsDirectory()
    {
        // Файловый объект (Document/ЗаказКлиента.xml) и каталог-объект
        // (Catalog/Контрагенты/Контрагенты.xml + подчинённый).
        WriteFile(Path.Combine(_tempRoot, "Configuration", "Document", "ЗаказКлиента.xml"), "<Doc/>");
        WriteFile(Path.Combine(_tempRoot, "Configuration", "Catalog", "Контрагенты", "Контрагенты.xml"), "<Head/>");
        WriteFile(Path.Combine(_tempRoot, "Configuration", "Catalog", "Контрагенты", "Attributes", "Наименование.xml"), "<Attr/>");

        var docs = MetadataXmlParser.EnumerateObjects(_tempRoot, "Document");
        var cats = MetadataXmlParser.EnumerateObjects(_tempRoot, "Catalog");

        var doc = Assert.Single(docs);
        Assert.False(doc.HasNested);
        Assert.Equal(1, doc.FileCount);
        Assert.Equal("Configuration/Document/ЗаказКлиента", doc.RelPath);

        var cat = Assert.Single(cats);
        Assert.True(cat.HasNested);
        Assert.Equal(2, cat.FileCount);
        Assert.Equal("Configuration/Catalog/Контрагенты", cat.RelPath);
    }

    [Fact]
    public void EnumerateObjects_UnknownTypeDir_ReturnsEmpty()
    {
        Assert.Empty(MetadataXmlParser.EnumerateObjects(_tempRoot, "NoSuchType"));
    }

    // ------------------------------------------------------------------
    // ReadObjectDetails
    // ------------------------------------------------------------------

    [Fact]
    public void ReadObjectDetails_Modern_SynonymCommentHierarchyAndCounters()
    {
        var catalogDir = Path.Combine(_tempRoot, "Catalogs", "Контрагенты");
        WriteFile(Path.Combine(_tempRoot, "Catalogs", "Контрагенты.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Catalog>" +
            "<Properties>" +
            "<Name>Контрагенты</Name>" +
            "<Synonym><v8:item><v8:lang>en</v8:lang><v8:content>Counterparties</v8:content></v8:item>" +
            "<v8:item><v8:lang>ru</v8:lang><v8:content>Контрагенты</v8:content></v8:item></Synonym>" +
            "<Comment>Справочник контрагентов</Comment>" +
            "<Hierarchical>true</Hierarchical><OrderedHierarchical>true</OrderedHierarchical>" +
            "</Properties><ChildObjects/></Catalog></MetaDataObject>");
        WriteFile(Path.Combine(catalogDir, "Attributes", "Наименование.xml"), "<A/>");
        WriteFile(Path.Combine(catalogDir, "TabularSections", "ТЧ.xml"), "<T/>");
        WriteFile(Path.Combine(catalogDir, "Forms", "ФормаЭлемента.xml"), "<F/>");
        WriteFile(Path.Combine(catalogDir, "Commands", "Команда1.xml"), "<C/>");
        WriteFile(Path.Combine(catalogDir, "Templates", "ШаблонПечати.xml"), "<P/>");

        var details = MetadataXmlParser.ReadObjectDetails(_tempRoot, "Catalog", "Контрагенты");

        Assert.Equal("Контрагенты", details.Name);
        Assert.Equal("Контрагенты", details.Synonym);   // предпочтителен item с lang=ru
        Assert.Equal("Справочник контрагентов", details.Comment);
        Assert.True(details.IsHierarchical);
        Assert.True(details.IsOrderedHierarchical);
        Assert.Equal(1, details.AttributeCount);
        Assert.Equal(1, details.TabularSectionCount);
        Assert.Equal(1, details.FormCount);
        Assert.Equal(1, details.CommandCount);
        Assert.Equal(1, details.TemplateCount);
        Assert.True(details.TotalBytes > 0);
        Assert.Equal("Catalogs/Контрагенты", details.RelPath);
    }

    [Fact]
    public void ReadObjectDetails_Synonym_FirstItemWithoutLangIsUsed()
    {
        WriteFile(Path.Combine(_tempRoot, "Catalogs", "Простой.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Catalog><Properties><Name>Простой</Name>" +
            "<Synonym><v8:item><v8:content>Первый</v8:content></v8:item></Synonym>" +
            "</Properties><ChildObjects/></Catalog></MetaDataObject>");

        var details = MetadataXmlParser.ReadObjectDetails(_tempRoot, "Catalog", "Простой");

        Assert.Equal("Первый", details.Synonym);
    }

    [Fact]
    public void ReadObjectDetails_LegacyFileObject_CountersFromChildObjects()
    {
        // Файловый объект: подчинённые перечислены в ChildObjects головного XML.
        WriteFile(Path.Combine(_tempRoot, "Configuration", "Document", "ЗаказКлиента.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Document><Properties><Name>ЗаказКлиента</Name><Synonym/>" +
            "<Comment>Документ заказа клиента</Comment></Properties>" +
            "<ChildObjects><Attribute>ДатаОтгрузки</Attribute><Attribute>Сумма</Attribute>" +
            "<Form>ФормаДокумента</Form></ChildObjects></Document></MetaDataObject>");

        var details = MetadataXmlParser.ReadObjectDetails(_tempRoot, "Document", "ЗаказКлиента");

        Assert.Equal("ЗаказКлиента", details.Name);
        Assert.Equal("Документ заказа клиента", details.Comment);
        Assert.False(details.IsHierarchical);
        Assert.Equal(2, details.AttributeCount);
        Assert.Equal(1, details.FormCount);
        Assert.Equal(0, details.TabularSectionCount);
    }

    [Fact]
    public void ReadObjectDetails_BrokenXml_ReturnsDefaultsWithoutException()
    {
        WriteFile(Path.Combine(_tempRoot, "Catalogs", "Битый.xml"), "<Catalog><Properties><Name>Битый");

        var details = MetadataXmlParser.ReadObjectDetails(_tempRoot, "Catalog", "Битый");

        Assert.Equal("Битый", details.Name);
        Assert.Equal(string.Empty, details.Synonym);
        Assert.False(details.IsHierarchical);
        Assert.Equal(0, details.AttributeCount);
    }

    [Fact]
    public void ReadObjectDetails_MissingObject_ReturnsDefaults()
    {
        var details = MetadataXmlParser.ReadObjectDetails(_tempRoot, "Catalog", "НетТакого");

        Assert.Equal("НетТакого", details.Name);
        Assert.Equal(0, details.AttributeCount);
    }

    // ------------------------------------------------------------------
    // ReadSubsystems
    // ------------------------------------------------------------------

    [Fact]
    public void ReadSubsystems_Modern_NestedAndContentKeys()
    {
        // Современный формат: головной файл Subsystems/Основное.xml, вложенная —
        // Subsystems/Основное/Subsystems/ПодсистемаВложенная.xml (разведка 8.3.24/8.3.27).
        WriteFile(Path.Combine(_tempRoot, "Subsystems", "Основное.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Subsystem><Properties><Name>Основное</Name>" +
            "<Synonym><v8:item><v8:content>Основное</v8:content></v8:item></Synonym>" +
            "<Comment>Основная подсистема</Comment>" +
            "<Content><v8:item><v8:content>Catalog.Контрагенты</v8:content></v8:item>" +
            "<v8:item><v8:content>Document.ЗаказКлиента</v8:content></v8:item></Content>" +
            "</Properties><ChildObjects><Subsystem>ПодсистемаВложенная</Subsystem></ChildObjects>" +
            "</Subsystem></MetaDataObject>");
        WriteFile(Path.Combine(_tempRoot, "Subsystems", "Основное", "Subsystems", "ПодсистемаВложенная.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Subsystem><Properties><Name>ПодсистемаВложенная</Name><Synonym/><Comment/>" +
            "<Content/></Properties><ChildObjects/></Subsystem></MetaDataObject>");

        var roots = MetadataXmlParser.ReadSubsystems(_tempRoot);

        var root = Assert.Single(roots);
        Assert.Equal("Основное", root.Name);
        Assert.Equal("Основное", root.Synonym);
        Assert.Equal("Основная подсистема", root.Comment);
        Assert.Equal("Subsystem.Основное", root.Key);
        Assert.Equal(new[] { "Catalog.Контрагенты", "Document.ЗаказКлиента" }, root.ContentKeys);

        var child = Assert.Single(root.Children);
        Assert.Equal("ПодсистемаВложенная", child.Name);
        Assert.Equal("Subsystem.Основное.ПодсистемаВложенная", child.Key);
        Assert.Empty(child.ContentKeys);
        Assert.Empty(child.Children);
    }

    [Fact]
    public void ReadSubsystems_Legacy_NestedFlatFilesExcludedFromRoot()
    {
        // Исторический формат: все файлы лежат плоско в Configuration/Subsystem/,
        // вложенность — только по ChildObjects.
        WriteFile(Path.Combine(_tempRoot, "Configuration", "Subsystem", "Основное.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Subsystem><Properties><Name>Основное</Name><Synonym/>" +
            "<Content><v8:item><v8:content>Catalog.Контрагенты</v8:content></v8:item></Content>" +
            "</Properties><ChildObjects><Subsystem>ПодсистемаВложенная</Subsystem></ChildObjects>" +
            "</Subsystem></MetaDataObject>");
        WriteFile(Path.Combine(_tempRoot, "Configuration", "Subsystem", "ПодсистемаВложенная.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Subsystem><Properties><Name>ПодсистемаВложенная</Name><Synonym/>" +
            "<Content/></Properties><ChildObjects/></Subsystem></MetaDataObject>");

        var roots = MetadataXmlParser.ReadSubsystems(_tempRoot);

        var root = Assert.Single(roots);
        Assert.Equal("Основное", root.Name);
        Assert.Equal("Subsystem.Основное.ПодсистемаВложенная", Assert.Single(root.Children).Key);
    }

    [Fact]
    public void ReadSubsystems_NoSubsystems_ReturnsEmpty()
    {
        Assert.Empty(MetadataXmlParser.ReadSubsystems(_tempRoot));
    }

    // ------------------------------------------------------------------
    // MetadataDump
    // ------------------------------------------------------------------

    [Fact]
    public void MetadataDump_Delete_RemovesDirectory()
    {
        var dumpRoot = Path.Combine(_tempRoot, "cm_metaeplorer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dumpRoot, "Catalogs"));
        WriteFile(Path.Combine(dumpRoot, "Configuration.xml"), "<root/>");

        var dump = new MetadataDump(dumpRoot, "Тест", "1.0.0.1");
        Assert.True(Directory.Exists(dump.RootPath));
        Assert.Equal("Тест", dump.ConfigurationName);
        Assert.Equal("1.0.0.1", dump.ConfigurationVersion);

        dump.Delete();

        Assert.False(Directory.Exists(dump.RootPath));
    }

    [Fact]
    public void MetadataDump_Delete_MissingDirectory_DoesNotThrow()
    {
        var dump = new MetadataDump(Path.Combine(_tempRoot, "нет_такого_каталога"), "X", "1");
        dump.Delete(); // не должно падать
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}