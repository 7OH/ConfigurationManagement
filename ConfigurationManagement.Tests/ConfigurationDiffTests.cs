using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой логики сравнения конфигураций (0.3.9.99, функция №9): обход выгрузки
/// <c>/DumpConfigToFiles</c>, хэши SHA-256, классификация Добавлен/Изменён/Удалён/
/// Без изменений, локализация типов метаданных и форматы отчёта CSV/TXT.
/// Без реальных операций 1С — выгрузки строятся на лету во временной папке теста.
/// </summary>
public sealed class ConfigurationDiffTests : IDisposable
{
    private readonly string _tempRoot;

    public ConfigurationDiffTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cm_diff_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>Фейк-локализатор: ключи как есть, кроме строк-шаблонов с {0}.</summary>
    private static string FakeT(string key) => key switch
    {
        "ConfigDiff.SummaryFormat" => "Добавлено: {0} · Изменено: {1} · Удалено: {2} · Без изменений: {3}",
        "ConfigDiff.TypeHeaderFormat" => "{0} — {1}",
        "ConfigDiff.ObjectLineFormat" => "    {0} | {1} | файлов: {2} | размер: {3}",
        _ => key
    };

    // ------------------------------------------------------------------
    // BuildSnapshot
    // ------------------------------------------------------------------

    [Fact]
    public void BuildSnapshot_WalksFilesAndDirectories_IgnoresConfigDumpInfo()
    {
        var dump = CreateDump("A");
        // Файл-объект
        WriteFile(Path.Combine(dump, "Configuration", "Document", "ЗаказКлиента.xml"), "<Doc/>");
        // Каталог-объект с подфайлами
        var dirObj = Path.Combine(dump, "Configuration", "Catalog", "Контрагенты");
        WriteFile(Path.Combine(dirObj, "Заголовок.xml"), "<Header/>");
        WriteFile(Path.Combine(dirObj, "Ext", "Форма.xml"), "<Form/>");
        // Служебный файл корня — должен игнорироваться
        WriteFile(Path.Combine(dump, ConfigurationDiffEngine.ConfigDumpInfoFileName), "<info/>");
        // Корневой Configuration.xml
        WriteFile(Path.Combine(dump, ConfigurationDiffEngine.RootConfigurationFileName), "<root v=\"1\"/>");

        var snapshot = ConfigurationDiffEngine.BuildSnapshot(dump);

        Assert.Equal(2, snapshot.Objects.Count);
        Assert.True(snapshot.Objects.ContainsKey("Configuration/Document/ЗаказКлиента.xml"));
        Assert.True(snapshot.Objects.ContainsKey("Configuration/Catalog/Контрагенты"));

        var doc = snapshot.Objects["Configuration/Document/ЗаказКлиента.xml"];
        Assert.Equal(1, doc.FileCount);
        Assert.Equal(6, doc.TotalBytes); // "<Doc/>" = 6 байт

        var catalog = snapshot.Objects["Configuration/Catalog/Контрагенты"];
        Assert.Equal(2, catalog.FileCount);

        Assert.NotNull(snapshot.RootFileHash);
    }

    [Fact]
    public void BuildSnapshot_MissingRootFile_ReturnsNullRootHash()
    {
        var dump = CreateDump("A");
        WriteFile(Path.Combine(dump, "Configuration", "Document", "Doc.xml"), "<Doc/>");

        var snapshot = ConfigurationDiffEngine.BuildSnapshot(dump);

        Assert.Null(snapshot.RootFileHash);
    }

    [Fact]
    public void BuildSnapshot_Deterministic_HashIndependentOfDirectoryOrder()
    {
        var dump1 = CreateDump("A");
        var dirObj1 = Path.Combine(dump1, "Configuration", "Catalog", "Obj");
        WriteFile(Path.Combine(dirObj1, "b.xml"), "B");
        WriteFile(Path.Combine(dirObj1, "a.xml"), "A");
        WriteFile(Path.Combine(dirObj1, "c.xml"), "C");

        var dump2 = CreateDump("B");
        var dirObj2 = Path.Combine(dump2, "Configuration", "Catalog", "Obj");
        // Другой порядок создания файлов не должен менять агрегатный хэш.
        WriteFile(Path.Combine(dirObj2, "c.xml"), "C");
        WriteFile(Path.Combine(dirObj2, "a.xml"), "A");
        WriteFile(Path.Combine(dirObj2, "b.xml"), "B");

        var s1 = ConfigurationDiffEngine.BuildSnapshot(dump1);
        var s2 = ConfigurationDiffEngine.BuildSnapshot(dump2);

        Assert.Equal(
            s1.Objects["Configuration/Catalog/Obj"].Hash,
            s2.Objects["Configuration/Catalog/Obj"].Hash);
    }

    [Fact]
    public void BuildSnapshot_PathsAreCaseSensitive()
    {
        var dump = CreateDump("A");
        WriteFile(Path.Combine(dump, "Configuration", "Catalog", "Test.xml"), "1");

        var snapshot = ConfigurationDiffEngine.BuildSnapshot(dump);

        Assert.False(snapshot.Objects.ContainsKey("Configuration/Catalog/test.xml"));
        Assert.True(snapshot.Objects.ContainsKey("Configuration/Catalog/Test.xml"));
    }

    // ------------------------------------------------------------------
    // Compare
    // ------------------------------------------------------------------

    [Fact]
    public void Compare_ClassifiesAddedRemovedChangedUnchanged()
    {
        var left = CreateDump("L");
        WriteFile(Path.Combine(left, "Configuration", "Catalog", "Общий.xml"), "same");
        WriteFile(Path.Combine(left, "Configuration", "Catalog", "Удалён.xml"), "old");
        WriteFile(Path.Combine(left, "Configuration", "Document", "Изменён.xml"), "v1");

        var right = CreateDump("R");
        WriteFile(Path.Combine(right, "Configuration", "Catalog", "Общий.xml"), "same");
        WriteFile(Path.Combine(right, "Configuration", "Document", "Изменён.xml"), "v2");
        WriteFile(Path.Combine(right, "Configuration", "Report", "Добавлен.xml"), "new");

        var result = ConfigurationDiffEngine.Compare(
            ConfigurationDiffEngine.BuildSnapshot(left),
            ConfigurationDiffEngine.BuildSnapshot(right),
            "Left", "Right", TimeSpan.Zero);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.ChangedCount);
        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(4, result.Objects.Count);

        Assert.Contains(result.Objects, o => o.Kind == DiffChangeKind.Added && o.Name == "Добавлен.xml" && o.TypeDir == "Report");
        Assert.Contains(result.Objects, o => o.Kind == DiffChangeKind.Changed && o.Name == "Изменён.xml");
        Assert.Contains(result.Objects, o => o.Kind == DiffChangeKind.Removed && o.Name == "Удалён.xml");
        Assert.Contains(result.Objects, o => o.Kind == DiffChangeKind.Unchanged && o.Name == "Общий.xml");
    }

    [Fact]
    public void Compare_SubfileChangeMarksWholeDirectoryObjectChanged()
    {
        var left = CreateDump("L");
        var dirL = Path.Combine(left, "Configuration", "Catalog", "Контрагенты");
        WriteFile(Path.Combine(dirL, "Заголовок.xml"), "h1");
        WriteFile(Path.Combine(dirL, "Ext", "Форма.xml"), "f1");

        var right = CreateDump("R");
        var dirR = Path.Combine(right, "Configuration", "Catalog", "Контрагенты");
        WriteFile(Path.Combine(dirR, "Заголовок.xml"), "h1");
        WriteFile(Path.Combine(dirR, "Ext", "Форма.xml"), "f2"); // изменён один подфайл

        var result = ConfigurationDiffEngine.Compare(
            ConfigurationDiffEngine.BuildSnapshot(left),
            ConfigurationDiffEngine.BuildSnapshot(right),
            "L", "R", TimeSpan.Zero);

        var obj = Assert.Single(result.Objects);
        Assert.Equal(DiffChangeKind.Changed, obj.Kind);
        Assert.Equal("Контрагенты", obj.Name);
    }

    [Fact]
    public void Compare_RootConfigurationXmlDifference_SetsRootFileChanged()
    {
        var left = CreateDump("L");
        WriteFile(Path.Combine(left, ConfigurationDiffEngine.RootConfigurationFileName), "v1");

        var right = CreateDump("R");
        WriteFile(Path.Combine(right, ConfigurationDiffEngine.RootConfigurationFileName), "v2");

        var same = ConfigurationDiffEngine.Compare(
            ConfigurationDiffEngine.BuildSnapshot(left),
            ConfigurationDiffEngine.BuildSnapshot(right),
            "L", "R", TimeSpan.Zero);
        Assert.True(same.RootFileChanged);
    }

    // ------------------------------------------------------------------
    // MetadataTypeLocalizer
    // ------------------------------------------------------------------

    [Fact]
    public void MetadataTypeLocalizer_KnownType_LocalizedViaCallback()
    {
        // Фейк-локализатор: возвращает ключ с префиксом как «переведённое» значение.
        var display = MetadataTypeLocalizer.GetDisplayName("Document", key => "KEY:" + key);

        Assert.Equal("KEY:ConfigDiff.Type.Document", display);
    }

    [Fact]
    public void MetadataTypeLocalizer_UnknownType_FallbackToDirName()
    {
        var display = MetadataTypeLocalizer.GetDisplayName("SomeFutureType", key => key);

        Assert.Equal("SomeFutureType", display);
    }

    [Fact]
    public void MetadataTypeLocalizer_SortTypes_KnownFirstUnknownsLast()
    {
        var sorted = MetadataTypeLocalizer.SortTypes(new[] { "ZetaUnknown", "Document", "AlphaUnknown", "Catalog" }).ToList();

        Assert.Equal("Document", sorted[0]);
        Assert.Equal("Catalog", sorted[1]);
        Assert.Contains("AlphaUnknown", sorted);
        Assert.Contains("ZetaUnknown", sorted);
        Assert.True(sorted.IndexOf("AlphaUnknown") > sorted.IndexOf("Catalog"));
    }

    // ------------------------------------------------------------------
    // ConfigurationDiffReporter
    // ------------------------------------------------------------------

    [Fact]
    public void Reporter_BuildCsvRows_HasHeaderAndEscapesSeparators()
    {
        var result = BuildSampleResult();

        var rows = ConfigurationDiffReporter.BuildCsvRows(result, FakeT);

        Assert.Equal("ConfigDiff.ColumnType", rows[0][0]);
        Assert.Equal("ConfigDiff.ColumnName", rows[0][1]);
        Assert.Equal("ConfigDiff.ColumnStatus", rows[0][2]);

        // Экранирование «;» и кавычек выполняется на этапе сериализации
        // (CsvExporter.BuildDocument → Escape по RFC 4180).
        var doc = CsvExporter.BuildDocument(rows);
        Assert.Contains("\"Имя;особое\"", doc);
        Assert.Contains("ДокументСОсобымИменем", doc);
    }

    [Fact]
    public void Reporter_BuildText_ContainsSummaryAndObjects()
    {
        var result = BuildSampleResult();

        var text = ConfigurationDiffReporter.BuildText(result, FakeT);

        Assert.Contains("Добавлено: 1", text);
        Assert.Contains("ДокументСОсобымИменем", text);
        Assert.Contains("ConfigDiff.RootChanged", text); // фейк-локализатор вернул ключ как есть
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string CreateDump(string tag)
    {
        var dir = Path.Combine(_tempRoot, tag);
        Directory.CreateDirectory(Path.Combine(dir, "Configuration"));
        return dir;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static ConfigurationDiffResult BuildSampleResult()
    {
        var objects = new List<MetadataObject>
        {
            new("Document", "Имя;особое", DiffChangeKind.Added, 2, 100),
            new("Catalog", "ДокументСОсобымИменем", DiffChangeKind.Changed, 1, 50),
            new("Report", "Отчёт", DiffChangeKind.Removed, 3, 300)
        };
        return new ConfigurationDiffResult("L", "R", objects, RootFileChanged: true, TimeSpan.FromSeconds(5));
    }
}