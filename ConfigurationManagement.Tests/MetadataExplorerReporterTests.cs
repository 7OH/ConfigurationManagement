using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистого репортёра «Обозревателя метаданных» (0.3.9.135, этап 4): CSV-заголовок
/// и строки (порядок колонок, локализация колбэком, экранирование спецсимволов через
/// <see cref="CsvExporter"/>), TXT-шапка и блочная группировка по типам, пустой список,
/// форматирование иерархического признака («Да»/«Нет»/пусто). Плюс интеграционные проверки
/// сборки строк экспорта из текущего представления (<see cref="MetadataExplorerViewModel.BuildExportRows"/>)
/// на временной выгрузке (образец <see cref="MetadataExplorerSearchTests"/>).
/// </summary>
public sealed class MetadataExplorerReporterTests : IDisposable
{
    private readonly string _tempRoot;

    public MetadataExplorerReporterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cm_metareporter_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>
    /// Фейк-локализатор (образец ConfigurationDiffTests.FakeT): ключи — с префиксом «T:»,
    /// шаблоны строк отчёта и «Да/Нет» — реальными значениями. Так проверяется, что все
    /// надписи проходят через колбэк (ни один ключ не утекает сырым).
    /// </summary>
    private static string FakeT(string key) => key switch
    {
        "MetadataExplorer.Report.TypeHeaderFormat" => "{0} — {1}",
        "MetadataExplorer.Report.ObjectLineFormat" => "    {0} | {1} | реквизитов: {2} | ТЧ: {3} | форм: {4} | размер: {5}",
        "Common.Yes" => "Да",
        "Common.No" => "Нет",
        _ => "T:" + key
    };

    private static MetadataExportRow Row(
        string typeDir,
        string name,
        string? synonym = null,
        string? comment = null,
        int attrs = 0,
        int tabularSections = 0,
        int forms = 0,
        int commands = 0,
        bool? hierarchical = null,
        int files = 1,
        long bytes = 0,
        string? relPath = null) => new()
    {
        TypeDir = typeDir,
        Name = name,
        Synonym = synonym ?? string.Empty,
        Comment = comment ?? string.Empty,
        AttributeCount = attrs,
        TabularSectionCount = tabularSections,
        FormCount = forms,
        CommandCount = commands,
        IsHierarchical = hierarchical,
        FileCount = files,
        TotalBytes = bytes,
        RelPath = relPath ?? $"Configuration/{typeDir}/{name}"
    };

    // ===================== CSV =====================

    [Fact]
    public void Csv_Header_ColumnOrder_LocalizedViaCallback()
    {
        var csv = MetadataExplorerReporter.BuildCsvRows(Array.Empty<MetadataExportRow>(), FakeT);

        Assert.Single(csv); // пустой список — только заголовок
        var header = csv[0];
        Assert.Equal(12, header.Count);
        Assert.Equal("T:MetadataExplorer.ColumnType", header[0]);
        Assert.Equal("T:MetadataExplorer.ColumnName", header[1]);
        Assert.Equal("T:MetadataExplorer.ColumnSynonym", header[2]);
        Assert.Equal("T:MetadataExplorer.ColumnComment", header[3]);
        Assert.Equal("T:MetadataExplorer.ColumnAttributes", header[4]);
        Assert.Equal("T:MetadataExplorer.ColumnTabularSections", header[5]);
        Assert.Equal("T:MetadataExplorer.ColumnForms", header[6]);
        Assert.Equal("T:MetadataExplorer.ColumnCommands", header[7]);
        Assert.Equal("T:MetadataExplorer.ColumnHierarchical", header[8]);
        Assert.Equal("T:MetadataExplorer.ColumnFiles", header[9]);
        Assert.Equal("T:MetadataExplorer.ColumnSize", header[10]);
        Assert.Equal("T:MetadataExplorer.ColumnRelPath", header[11]);
    }

    [Fact]
    public void Csv_DataRow_ValuesAndEscapingSpecialChars()
    {
        var rows = new[]
        {
            Row(
                "Catalog", "Контрагенты",
                synonym: "А;Б \"К\"",
                comment: "Справочник\nс переводом строки",
                attrs: 2, tabularSections: 1, forms: 3, commands: 4,
                hierarchical: true, files: 5, bytes: 1000,
                relPath: "Configuration/Catalog/Контрагенты")
        };
        var csv = MetadataExplorerReporter.BuildCsvRows(rows, FakeT);

        Assert.Equal(2, csv.Count);
        var data = csv[1];
        Assert.Equal("T:ConfigDiff.Type.Catalog", data[0]); // тип локализован через колбэк
        Assert.Equal("Контрагенты", data[1]);
        Assert.Equal("2", data[4]);
        Assert.Equal("1", data[5]);
        Assert.Equal("3", data[6]);
        Assert.Equal("4", data[7]);
        Assert.Equal("Да", data[8]);
        Assert.Equal("5", data[9]);
        Assert.Equal("1000", data[10]);
        Assert.Equal("Configuration/Catalog/Контрагенты", data[11]);

        // Экранирование через CsvExporter: «;» и кавычки в синониме/комментарии
        // заключаются в кавычки, внутренние кавычки удваиваются (RFC 4180).
        var joined = CsvExporter.JoinRow(data);
        Assert.Contains("\"А;Б \"\"К\"\"\"", joined);
        Assert.Contains("\"Справочник\nс переводом строки\"", joined);

        // Документ целиком собирается без ошибок и содержит строку объекта.
        var doc = CsvExporter.BuildDocument(csv);
        Assert.Contains("Контрагенты", doc);
    }

    [Fact]
    public void Csv_Hierarchical_FormatsYesNoEmpty()
    {
        var rows = new[]
        {
            Row("Catalog", "Иерархический", hierarchical: true),
            Row("Document", "Обычный", hierarchical: false),
            Row("Report", "Без деталей", hierarchical: null)
        };
        var csv = MetadataExplorerReporter.BuildCsvRows(rows, FakeT);

        Assert.Equal("Да", csv[1][8]);
        Assert.Equal("Нет", csv[2][8]);
        Assert.Equal(string.Empty, csv[3][8]);
    }

    // ===================== TXT =====================

    [Fact]
    public void Text_HeaderLineAndTypeBlocks()
    {
        var rows = new List<MetadataExportRow>
        {
            Row("Document", "ЗаказКлиента", "Заказ клиента", "Документ заказа",
                attrs: 5, tabularSections: 2, forms: 1, commands: 0,
                hierarchical: false, files: 3, bytes: 300),
            Row("Catalog", "Контрагенты", "Контрагенты", "Справочник контрагентов",
                attrs: 2, tabularSections: 1, forms: 2, commands: 1,
                hierarchical: true, files: 4, bytes: 400)
        };

        var text = MetadataExplorerReporter.BuildText("Источник: база \"Тест\"", rows, FakeT);

        // Шапка — первой строкой.
        Assert.StartsWith("Источник: база \"Тест\"", text);

        // Блочная группировка: заголовки «Тип — N» по порядку SortTypes.
        Assert.Contains("T:ConfigDiff.Type.Catalog — 1", text);
        Assert.Contains("T:ConfigDiff.Type.Document — 1", text);

        // Строки объектов: «Имя | Синоним | реквизитов: X | ТЧ: Y | форм: Z | размер: N».
        Assert.Contains("    Контрагенты | Контрагенты | реквизитов: 2 | ТЧ: 1 | форм: 2 | размер: 400", text);
        Assert.Contains("    ЗаказКлиента | Заказ клиента | реквизитов: 5 | ТЧ: 2 | форм: 1 | размер: 300", text);
    }

    [Fact]
    public void Text_EmptyRows_HeaderOnly()
    {
        var text = MetadataExplorerReporter.BuildText("Источник: файл C:\\test.cf", Array.Empty<MetadataExportRow>(), FakeT);

        Assert.Equal("Источник: файл C:\\test.cf", text); // без типовых блоков и хвостовых переводов строк
    }

    [Fact]
    public void Text_NoRawKeys_AllLabelsPassThroughCallback()
    {
        var rows = new[] { Row("Catalog", "Контрагенты", "Контрагенты") };

        var text = MetadataExplorerReporter.BuildText("Шапка", rows, FakeT);

        // Ни один ключ локализации не утёк сырым текстом — всё через FakeT («T:…»).
        Assert.DoesNotContain("MetadataExplorer.", text);
        Assert.Contains("T:ConfigDiff.Type.Catalog", text);
    }

    // ===================== Интеграция с ViewModel =====================

    [Fact]
    public async Task ExportRows_TypeFilter_AppliesToCurrentView()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        // Фильтр по типу «Документы» → в экспорт попадает только видимый тип.
        vm.TypeFilter = vm.TypeFilterOptions.First(o => o.Value == "Document");
        var rows = vm.BuildExportRows();

        var row = Assert.Single(rows);
        Assert.Equal("Document", row.TypeDir);
        Assert.Equal("ЗаказКлиента", row.Name);
        // Детали догружены парсером: синоним/комментарий из головного XML.
        Assert.Equal(string.Empty, row.Synonym);
        Assert.Equal(string.Empty, row.Comment);
    }

    [Fact]
    public async Task ExportRows_SearchActive_ReturnsHighlightedMatches()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        vm.SearchText = "заказ";
        vm.ApplySearch();
        Assert.True(vm.IsSearchActive);

        var rows = vm.BuildExportRows();

        var row = Assert.Single(rows);
        Assert.Equal("ЗаказКлиента", row.Name);
        Assert.Equal("Document", row.TypeDir);
    }

    [Fact]
    public async Task BuildTextReport_ContainsHeaderLines()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var vm = CreateVm(new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        });
        await vm.LoadAsync();

        var report = vm.BuildTextReport();

        // В тестах LocalizationManager не инициализирован — T возвращает ключи;
        // шапка отчёта обязана содержать строки источника, конфигурации и даты.
        Assert.Contains("MetadataExplorer.Report.SourceBaseFormat", report);
        Assert.Contains("MetadataExplorer.Report.ConfigFormat", report);
        Assert.Contains("MetadataExplorer.Report.DateFormat", report);
    }

    // ===================== Помощники и фикстуры =====================

    private static Infobase CreateBase(string name = "Бухгалтерия") => new()
    {
        Id = "test-id",
        Name = name,
        PlatformVersion = "8.3.27.1644"
    };

    private static MetadataExplorerViewModel CreateVm(IMetadataExplorerService fake) =>
        new(new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());

    private const string Ns =
        "<MetaDataObject xmlns=\"http://v8.1c.ru/8.3/MDClasses\" " +
        "xmlns:app=\"http://v8.1c.ru/8.2/managed-application/core\" " +
        "xmlns:v8=\"http://v8.1c.ru/8.1/data/core\" " +
        "xmlns:xr=\"http://v8.1c.ru/8.3/xcf/readable\" version=\"2.20\">";

    /// <summary>Выгрузка без подсистем: Catalogs/Контрагенты + Documents/ЗаказКлиента.</summary>
    private string CreateDumpWithoutSubsystems()
    {
        var dir = Path.Combine(_tempRoot, "dump_obj_" + Guid.NewGuid().ToString("N"));
        WriteFile(Path.Combine(dir, "Configuration.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Configuration><Properties><Name>Простая</Name><Synonym/><Comment/>" +
            "<Version>2.0.0.1</Version></Properties><ChildObjects/></Configuration></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Catalogs", "Контрагенты.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Catalog><Properties><Name>Контрагенты</Name>" +
            "<Synonym><v8:item><v8:content>Контрагенты</v8:content></v8:item></Synonym>" +
            "<Comment>Справочник контрагентов</Comment><OrderedHierarchical>true</OrderedHierarchical>" +
            "</Properties><ChildObjects/></Catalog></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Documents", "ЗаказКлиента.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Document><Properties><Name>ЗаказКлиента</Name><Synonym/><Comment/></Properties>" +
            "<ChildObjects/></Document></MetaDataObject>");
        return dir;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ===================== Fakes =====================

    /// <summary>Fake-сервис выгрузки (образец MetadataExplorerSearchTests).</summary>
    private sealed class FakeMetadataExplorerService : IMetadataExplorerService
    {
        public MetadataDump? BaseResult { get; set; }
        public MetadataDump? CfResult { get; set; }
        public Exception? BaseException { get; set; }
        public Exception? CfException { get; set; }

        public Task<MetadataDump> DumpFromBaseAsync(
            Infobase infobase, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            if (BaseException is not null)
                throw BaseException;
            progress?.Report("dump");
            return Task.FromResult(BaseResult!);
        }

        public Task<MetadataDump> DumpFromCfAsync(
            string cfPath, string platformVersion, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            if (CfException is not null)
                throw CfException;
            progress?.Report("dump");
            return Task.FromResult(CfResult!);
        }
    }

    /// <summary>Запись диалогов для тестов (образец MetadataExplorerSearchTests).</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public string? SaveFileResult { get; set; }

        public void ShowInfo(string message, string title = "") { }
        public void ShowWarning(string message, string title = "") { }
        public void ShowError(string message, string title = "") { }
        public bool Confirm(string message, string title = "") => true;
        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;
        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => SaveFileResult;
        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}