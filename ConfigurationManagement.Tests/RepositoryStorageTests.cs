using System;
using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сервисного слоя «Обозревателя хранилища конфигурации» (0.3.9.127, этап 1):
/// сборка аргументов repository-операций DESIGNER (включая грамматику -v/-NBegin/-NEnd/-objects),
/// маскирование пароля хранилища, чистый парсер отчёта по истории и полный обход выгрузки
/// <see cref="ConfigurationDiffEngine.BuildObjectList"/>. Без реальных операций 1С:
/// выгрузки строятся на лету во временной папке теста.
///
/// РАЗВЕДКА ЭТАПА 1: грамматика ключей /ConfigurationRepositoryF/N/P + Report подтверждена на
/// реальной платформе 8.3.27.2325 (см. комментарий в <see cref="RepositoryHistoryParser"/>),
/// но фактический формат файла отчёта и XML-файла -objects не проверяем (создание хранилища
/// на этой машине блокируется политикой записи) — поэтому тесты парсера используют
/// документированный TAB-формат строк, а XML-образец -objects не фиксируется.
/// </summary>
public sealed class RepositoryStorageTests : IDisposable
{
    private readonly string _tempRoot;

    public RepositoryStorageTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cm_repo_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>База с заполненным адресом хранилища (логин/пароль — русские, с пробелами).</summary>
    private static Infobase CreateBaseWithRepository()
        => new()
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\test" },
            Repository = new RepositorySettings
            {
                Server = "tcp://localhost:1542",
                RepositoryName = "TestRepo",
                User = "Администратор хранилища",
                Password = "пароль с пробелом"
            }
        };

    /// <summary>База без адреса хранилища.</summary>
    private static Infobase CreateBaseWithoutRepository()
        => new()
        {
            Name = "База без хранилища",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\plain" },
            Repository = new RepositorySettings()
        };

    // ------------------------------------------------------------------
    // Сборка аргументов
    // ------------------------------------------------------------------

    [Fact]
    public void BuildRepositoryDumpCfgArgument_WithVersion_HasDashVParameter()
    {
        var ib = CreateBaseWithRepository();
        var arg = OneCLauncher.BuildRepositoryDumpCfgArgument(ib, @"C:\repo\out\v5.cf", 5);

        Assert.Contains(" /ConfigurationRepositoryF \"tcp://localhost:1542/TestRepo\"", arg);
        Assert.Contains(" /ConfigurationRepositoryN \"Администратор хранилища\"", arg);
        Assert.Contains(" /ConfigurationRepositoryP \"пароль с пробелом\"", arg);
        // Ключ операции — как /DumpIB"path" (значение сразу в кавычках)…
        Assert.Contains("/ConfigurationRepositoryDumpCfg\"C:\\repo\\out\\v5.cf\"", arg);
        // …а параметр -v — с дефисом и пробелом перед значением (уникальная грамматика repository).
        Assert.Contains(" -v 5", arg);
    }

    [Fact]
    public void BuildRepositoryDumpCfgArgument_WithoutVersion_NoDashV()
    {
        var arg = OneCLauncher.BuildRepositoryDumpCfgArgument(CreateBaseWithRepository(), @"C:\repo\cur.cf", null);

        Assert.Contains("/ConfigurationRepositoryDumpCfg\"C:\\repo\\cur.cf\"", arg);
        Assert.DoesNotContain("-v", arg);
    }

    [Fact]
    public void BuildRepositoryDumpCfgArgument_NoRepositoryAddress_ReturnsEmpty()
    {
        var arg = OneCLauncher.BuildRepositoryDumpCfgArgument(CreateBaseWithoutRepository(), @"C:\x.cf", 1);

        Assert.Equal(string.Empty, arg);
    }

    [Fact]
    public void BuildRepositoryReportArgument_WithBounds_HasNBeginNEnd()
    {
        var arg = OneCLauncher.BuildRepositoryReportArgument(CreateBaseWithRepository(), @"C:\repo\hist.txt", 3, 9);

        Assert.Contains("/ConfigurationRepositoryReport\"C:\\repo\\hist.txt\"", arg);
        Assert.Contains(" -NBegin 3", arg);
        Assert.Contains(" -NEnd 9", arg);
    }

    [Fact]
    public void BuildRepositoryReportArgument_WithoutBounds_NoRangeParameters()
    {
        var arg = OneCLauncher.BuildRepositoryReportArgument(CreateBaseWithRepository(), @"C:\repo\hist.mxl", null, null);

        Assert.DoesNotContain("NBegin", arg);
        Assert.DoesNotContain("NEnd", arg);
    }

    [Fact]
    public void BuildRepositoryLockArgument_WithObjectsXml_HasObjectsParameter()
    {
        var arg = OneCLauncher.BuildRepositoryLockArgument(CreateBaseWithRepository(), @"C:\repo\objects.xml");

        Assert.Contains(" /ConfigurationRepositoryLock", arg);
        Assert.Contains(" -objects\"C:\\repo\\objects.xml\"", arg);
    }

    [Fact]
    public void BuildRepositoryLockArgument_WithoutObjectsXml_LocksAll()
    {
        var arg = OneCLauncher.BuildRepositoryLockArgument(CreateBaseWithRepository(), null);

        Assert.Contains(" /ConfigurationRepositoryLock", arg);
        Assert.DoesNotContain("-objects", arg);
    }

    [Fact]
    public void BuildRepositoryUnlockArgument_WithObjectsXml_HasObjectsParameter()
    {
        var arg = OneCLauncher.BuildRepositoryUnlockArgument(CreateBaseWithRepository(), @"C:\repo\objects.xml");

        Assert.Contains(" /ConfigurationRepositoryUnlock", arg);
        Assert.Contains(" -objects\"C:\\repo\\objects.xml\"", arg);
    }

    [Fact]
    public void BuildRepositoryArguments_UnsafePasswordIsNotSubstituted()
    {
        // Пароль с кавычкой нельзя безопасно представить по грамматике ключа — /P-ключ опускается.
        var ib = CreateBaseWithRepository();
        ib.Repository.Password = "pa\"ss";

        var arg = OneCLauncher.BuildRepositoryDumpCfgArgument(ib, @"C:\x.cf", 1);

        Assert.DoesNotContain("/ConfigurationRepositoryP", arg);
        Assert.Contains("/ConfigurationRepositoryN \"Администратор хранилища\"", arg);
    }

    [Fact]
    public void RepositoryCommandLine_AfterMasking_NoOpenPassword()
    {
        var ib = CreateBaseWithRepository();
        var arg = OneCLauncher.BuildRepositoryDumpCfgArgument(ib, @"C:\repo\out.cf", 7);

        // Командная строка формируется как в RunDesignerBatch (exePath + arguments) и маскируется
        // ДО попадания в DesignerBatchInfo.CommandLine (пароль не должен утекать в ErrorMessage).
        var commandLine = SensitiveDataMasker.MaskRepositoryPassword(
            @"C:\Program Files\1cv8\8.3.27.2325\bin\1cv8.exe DESIGNER /F""C:\bases\test""" + arg);

        Assert.DoesNotContain("пароль с пробелом", commandLine);
        Assert.Contains("/ConfigurationRepositoryP \"***\"", commandLine);
        // Пути и имя пользователя не искажаются.
        Assert.Contains("tcp://localhost:1542/TestRepo", commandLine);
        Assert.Contains("Администратор хранилища", commandLine);
        Assert.Contains(@"C:\repo\out.cf", commandLine);
    }

    // ------------------------------------------------------------------
    // MaskRepositoryPassword
    // ------------------------------------------------------------------

    [Fact]
    public void MaskRepositoryPassword_ReplacesValueAndKeepsPath()
    {
        const string text =
            "1cv8.exe DESIGNER /F\"C:\\base\" /ConfigurationRepositoryF \"tcp://host/repo\" " +
            "/ConfigurationRepositoryN \"User\" /ConfigurationRepositoryP \"супер-секрет\"";

        var masked = SensitiveDataMasker.MaskRepositoryPassword(text);

        Assert.DoesNotContain("супер-секрет", masked);
        Assert.Contains("/ConfigurationRepositoryP \"***\"", masked);
        Assert.Contains("tcp://host/repo", masked);
        Assert.Contains("/ConfigurationRepositoryN \"User\"", masked);
    }

    [Fact]
    public void MaskRepositoryPassword_MasksPwdStyleKeyToo()
    {
        var masked = SensitiveDataMasker.MaskRepositoryPassword("DESIGNER ... -Pwd\"тоже-секрет\"");

        Assert.DoesNotContain("тоже-секрет", masked);
        Assert.Contains("-Pwd\"***\"", masked);
    }

    [Fact]
    public void MaskRepositoryPassword_NullOrEmpty_ReturnsAsIs()
    {
        Assert.Null(SensitiveDataMasker.MaskRepositoryPassword(null));
        Assert.Equal(string.Empty, SensitiveDataMasker.MaskRepositoryPassword(string.Empty));
    }

    [Fact]
    public void MaskRepositoryPassword_MultipleOccurrences_AllMasked()
    {
        var masked = SensitiveDataMasker.MaskRepositoryPassword(
            "/ConfigurationRepositoryP \"one\" /ConfigurationRepositoryP \"two\"");

        Assert.DoesNotContain("one", masked);
        Assert.DoesNotContain("two", masked);
    }

    // ------------------------------------------------------------------
    // RepositoryHistoryParser.ParseReport
    // ------------------------------------------------------------------

    [Fact]
    public void ParseReport_ParsesRows_WithRussianNamesAndSpaces()
    {
        // TAB-разделитель (экспорт табличного документа): №, дата-время, автор, комментарий.
        const string report =
            "Версия\tДата\tАвтор\tКомментарий\n" +
            "3\t2026-09-28 19:30:00\tИванов Иван\tИсправление отчёта \"Продажи\"\n" +
            "2\t2026-09-27 12:00:00\tПетрова Анна\tДобавлен справочник Контрагенты\n" +
            "1\t2026-09-26 10:15:00\tСидоров\t\n";

        var versions = RepositoryHistoryParser.ParseReport(report);

        Assert.Equal(3, versions.Count);

        var v3 = versions[0];
        Assert.Equal(3, v3.Number);
        Assert.Equal(new DateTime(2026, 9, 28, 19, 30, 0), v3.Date);
        Assert.Equal("Иванов Иван", v3.Author);
        Assert.Equal("Исправление отчёта \"Продажи\"", v3.Comment);

        // Пустой комментарий (пустое поле) — пустая строка.
        Assert.Equal(string.Empty, versions[2].Comment);
    }

    [Fact]
    public void ParseReport_CurrentVersion_IsMaxNumber()
    {
        // Отчёт может быть отсортирован по возрастанию — актуальной всё равно считается max номер.
        const string report = "1\t2026-09-26\tАвтор1\tпервая\n2\t2026-09-27\tАвтор2\tвторая";

        var versions = RepositoryHistoryParser.ParseReport(report);

        // Актуальной помечается ровно одна запись — с наибольшим номером.
        Assert.Single(versions, v => v.IsCurrent);
        Assert.True(versions.First(v => v.Number == 2).IsCurrent);
        Assert.False(versions.First(v => v.Number == 1).IsCurrent);
    }

    [Fact]
    public void ParseReport_CrookedLinesAreSkipped_EmptyFieldsUseDefaults()
    {
        const string report =
            "Период: 01.09.2026 — 28.09.2026\n" +       // служебная строка (не число)
            "\n" +                                       // пустая строка
            "не-число\t2026-09-28\tАвтор\tкомм\n" +      // «кривая» строка
            "7\t\t\tкомментарий без автора\n";           // пустые поля

        var versions = RepositoryHistoryParser.ParseReport(report);

        var only = Assert.Single(versions);
        Assert.Equal(7, only.Number);
        Assert.Equal(default(DateTime), only.Date);      // пустая дата → default
        Assert.Equal(string.Empty, only.Author);         // пустой автор → пустая строка
        Assert.Equal("комментарий без автора", only.Comment);
    }

    [Fact]
    public void ParseReport_NullOrWhitespace_ReturnsEmptyList()
    {
        Assert.Empty(RepositoryHistoryParser.ParseReport(null));
        Assert.Empty(RepositoryHistoryParser.ParseReport("   \n\t "));
    }

    [Fact]
    public void ParseReport_WhitespaceSeparatedFallback_JoinsComment()
    {
        // Без табуляций (запасной вариант): разбиение по пробелам, комментарий склеивается.
        const string report = "4 2026-09-28 19:00:00 Автор Иванов новый реквизит";

        var versions = RepositoryHistoryParser.ParseReport(report);

        var only = Assert.Single(versions);
        Assert.Equal(4, only.Number);
        Assert.Equal("Автор", only.Author);
        Assert.Equal("Иванов новый реквизит", only.Comment);
    }

    // ------------------------------------------------------------------
    // ConfigurationDiffEngine.BuildObjectList
    // ------------------------------------------------------------------

    [Fact]
    public void BuildObjectList_WalksTopLevelAndNestedObjects_WithOwners()
    {
        var dump = CreateDump();
        // Файловый объект верхнего уровня.
        WriteFile(Path.Combine(dump, "Configuration", "Document", "ЗаказКлиента.xml"), "<Doc/>");
        // Каталог-объект верхнего уровня с вложенными объектами подкаталогов.
        var catalog = Path.Combine(dump, "Configuration", "Catalog", "Контрагенты");
        WriteFile(Path.Combine(catalog, "Forms", "ФормаЭлемента.xml"), "<Form/>");
        WriteFile(Path.Combine(catalog, "Attributes", "Наименование.xml"), "<Attr/>");
        WriteFile(Path.Combine(catalog, "Commands", "КомандаСоздать.xml"), "<Cmd/>");
        // Вложенность глубже одного уровня (Ext/Form/…).
        WriteFile(Path.Combine(catalog, "Ext", "Form", "ФормаПомощника.xml"), "<ExtForm/>");
        // Служебный файл корня выгрузки — игнорируется (лежит вне Configuration/).
        WriteFile(Path.Combine(dump, ConfigurationDiffEngine.ConfigDumpInfoFileName), "<info/>");

        var objects = ConfigurationDiffEngine.BuildObjectList(dump);

        // 2 верхнего уровня + 4 вложенных.
        Assert.Equal(6, objects.Count);

        var topDoc = Assert.Single(objects, o => o.Name == "ЗаказКлиента.xml");
        Assert.True(topDoc.IsTopLevel);
        Assert.Equal("Document", topDoc.TypeDir);
        Assert.Equal(string.Empty, topDoc.Owner);

        var topCatalog = Assert.Single(objects, o => o.Name == "Контрагенты");
        Assert.True(topCatalog.IsTopLevel);
        Assert.Equal("Catalog", topCatalog.TypeDir);

        var form = Assert.Single(objects, o => o.Name == "ФормаЭлемента");
        Assert.False(form.IsTopLevel);
        Assert.Equal("Forms", form.TypeDir);
        Assert.Equal("Контрагенты", form.Owner);

        Assert.Single(objects, o => o.Name == "Наименование" && o.TypeDir == "Attributes" && o.Owner == "Контрагенты");
        Assert.Single(objects, o => o.Name == "КомандаСоздать" && o.TypeDir == "Commands" && o.Owner == "Контрагенты");
        Assert.Single(objects, o => o.Name == "ФормаПомощника" && o.TypeDir == "Ext" && o.Owner == "Контрагенты");
    }

    [Fact]
    public void BuildObjectList_LocalizesTypesViaCallback()
    {
        var dump = CreateDump();
        WriteFile(Path.Combine(dump, "Configuration", "Document", "Док.xml"), "<D/>");

        var objects = ConfigurationDiffEngine.BuildObjectList(dump, key => "KEY:" + key);

        var only = Assert.Single(objects);
        Assert.Equal("KEY:ConfigDiff.Type.Document", only.TypeDir);
    }

    [Fact]
    public void BuildObjectList_MissingConfigurationDir_ReturnsEmpty()
    {
        var dump = Path.Combine(_tempRoot, "no_config");
        Directory.CreateDirectory(dump);

        Assert.Empty(ConfigurationDiffEngine.BuildObjectList(dump));
    }

    [Fact]
    public void BuildObjectList_NullRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => ConfigurationDiffEngine.BuildObjectList(string.Empty));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string CreateDump()
    {
        var dir = Path.Combine(_tempRoot, "dump" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Configuration"));
        return dir;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}