using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты генератора самодостаточного HTML-отчёта по базам (0.3.9.131):
/// экранирование HTML-спецсимволов (имена баз/подписи — пользовательский ввод),
/// наличие обязательных секций (шапка, сводка, навигация, таблицы по группам),
/// корректность сводки на фикстуре и запись файла в UTF-8.
/// </summary>
public sealed class HtmlReportExporterTests
{
    // Сущности собираются из фрагментов: при XML-обработке исходников теста
    // последовательности вида "<" декодировались бы в "<" и ломали проверки.
    private const string Lt = "&" + "lt;";
    private const string Gt = "&" + "gt;";
    private const string Amp = "&" + "amp;";
    private const string Quot = "&" + "quot;";
    private const string Apos = "&" + "#39;";

    [Fact]
    public void Escape_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, HtmlReportExporter.Escape(null));
        Assert.Equal(string.Empty, HtmlReportExporter.Escape(string.Empty));
    }

    [Fact]
    public void Escape_PlainText_Unchanged()
    {
        Assert.Equal("База «Альфа»", HtmlReportExporter.Escape("База «Альфа»"));
        Assert.Equal("1C:Предприятие", HtmlReportExporter.Escape("1C:Предприятие"));
    }

    [Fact]
    public void Escape_SpecialChars_Encoded()
    {
        Assert.Equal(Amp, HtmlReportExporter.Escape("&"));
        Assert.Equal(Lt + "b" + Gt, HtmlReportExporter.Escape("<b>"));
        Assert.Equal(Quot + "q" + Quot, HtmlReportExporter.Escape("\"q\""));
        Assert.Equal("a" + Apos + "b", HtmlReportExporter.Escape("a'b"));
    }

    [Fact]
    public void BuildDocument_ContainsRequiredSections()
    {
        var html = HtmlReportExporter.BuildDocument(CreateFixture());

        // Шапка: doctype, заголовок, дата/время и профиль.
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title>", html);
        Assert.Contains("<h1>Отчёт по информационным базам</h1>", html);
        Assert.Contains("Профиль: Тест", html);

        // Сводка и навигация.
        Assert.Contains("id=\"summary\"", html);
        Assert.Contains("toggleProblems", html);
        Assert.Contains("По группам:", html);

        // Таблицы по секциям групп.
        Assert.Contains("<table>", html);
        Assert.Contains("<th>Имя базы</th>", html);
        Assert.Contains("<th>Доступность</th>", html);
    }

    [Fact]
    public void BuildDocument_EscapesBaseNameAndLabels()
    {
        var doc = CreateFixture();
        var html = HtmlReportExporter.BuildDocument(doc);

        // Имя базы с HTML-разметкой не должно попасть в документ как разметка.
        Assert.DoesNotContain("<b>Бета</b>", html);
        Assert.Contains(Lt + "b" + Gt + "Бета" + Lt + "/" + "b" + Gt, html);
        Assert.Contains(Amp, html);
        Assert.Contains(Quot, html);
    }

    [Fact]
    public void BuildDocument_SummaryCorrectOnFixture()
    {
        var html = HtmlReportExporter.BuildDocument(CreateFixture());

        // Числа сводки на фикстуре: 3 базы, 2 доступны, 1 недоступна,
        // 2 с копиями, 2 проблемные.
        Assert.Contains(">3</div>", html);
        Assert.Contains(">2</div>", html);
        Assert.Contains(">1</div>", html);
        Assert.Contains("Всего баз", html);
        Assert.Contains("Доступно", html);
        Assert.Contains("Недоступно", html);
        Assert.Contains("Баз с копиями", html);
        Assert.Contains("Проблемных баз", html);
        Assert.Contains("Объём ИБ", html);
    }

    [Fact]
    public void BuildDocument_HighlightsProblemRows()
    {
        var html = HtmlReportExporter.BuildDocument(CreateFixture());

        // Проблемные строки помечены классом и инлайновым стилем подсветки.
        Assert.Contains("class=\"data-row problem\" style=\"background-color:#FDE8E8\"", html);
        Assert.Contains("badge-bad", html);
        Assert.Contains("badge-ok", html);
    }

    [Fact]
    public void BuildDocument_GroupsIntoSectionsWithAnchors()
    {
        var html = HtmlReportExporter.BuildDocument(CreateFixture());

        // Секции по группам с количеством баз и ссылки-якоря в навигации.
        Assert.Contains("Бухгалтерия (2)", html);
        Assert.Contains("Производство (1)", html);
        Assert.Contains("href=\"#g-", html);
    }

    [Fact]
    public void WriteFile_WritesUtf8WithoutBom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"html-report-{Guid.NewGuid():N}.html");
        try
        {
            HtmlReportExporter.WriteFile(path, CreateFixture());

            var bytes = File.ReadAllBytes(path);
            // HTML пишется в UTF-8 без BOM.
            Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

            var text = File.ReadAllText(path, HtmlReportExporter.Utf8);
            Assert.StartsWith("<!DOCTYPE html>", text);
            Assert.Contains("Отчёт по информационным базам", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Фикстура: 3 базы (2 доступны, 1 недоступна; 2 с копиями; 2 проблемные —
    /// недоступная и база со старой копией). Имя одной базы содержит HTML-разметку,
    /// чтобы проверить экранирование.
    /// </summary>
    private static HtmlReportDocument CreateFixture()
    {
        return new HtmlReportDocument
        {
            AppName = "Управление конфигурациями 1С",
            GeneratedAtText = "Сформирован: 28.09.2026 21:00",
            ProfileName = "Профиль: Тест",
            TotalCount = 3,
            AvailableCount = 2,
            UnavailableCount = 1,
            WithBackupsCount = 2,
            TotalSizeBytes = 2048,
            ProblemCount = 2,
            Labels = new HtmlReportLabels
            {
                Header = "Отчёт по информационным базам",
                SummaryTotal = "Всего баз",
                SummaryAvailable = "Доступно",
                SummaryUnavailable = "Недоступно",
                SummaryWithBackups = "Баз с копиями",
                SummarySize = "Объём ИБ",
                SummaryProblems = "Проблемных баз",
                ShowAll = "Все базы",
                OnlyProblems = "Только проблемы",
                GroupNav = "По группам:",
                Legend = "Подсветка: проблема",
                ColName = "Имя базы",
                ColGroup = "Группа",
                ColType = "Тип",
                ColConnection = "Строка подключения",
                ColAvailability = "Доступность",
                ColConfiguration = "Конфигурация / версия",
                ColSize = "Размер ИБ",
                ColLastBackup = "Последняя копия",
                ColModified = "Дата изменений",
                ColTags = "Теги",
                ColFavorite = "Закладка",
                Available = "Доступна",
                Unavailable = "Недоступна"
            },
            Rows = new[]
            {
                new HtmlReportRow
                {
                    Name = "Альфа",
                    GroupPath = "Бухгалтерия",
                    Type = "Файловая",
                    ConnectionString = "File=\"C:\\bases\\alfa\"",
                    IsAvailable = true,
                    AvailabilityText = "Доступна",
                    Configuration = "Бухгалтерия предприятия 3.0.142.32",
                    Size = "1,0 КБ",
                    LastBackup = "27.09.2026 12:00",
                    Modified = "2026-09-27 12:00",
                    Tags = "основная",
                    Favorite = "1",
                    HasProblem = false
                },
                new HtmlReportRow
                {
                    Name = "<b>Бета</b> & Ко",
                    GroupPath = "Бухгалтерия",
                    Type = "Клиент-серверная",
                    ConnectionString = "Srvr=\"srv1\";Ref=\"beta\"",
                    IsAvailable = false,
                    AvailabilityText = "Недоступна",
                    Configuration = "ЗУП 3.1.30.105",
                    Size = "—",
                    LastBackup = "Никогда",
                    Modified = string.Empty,
                    Tags = string.Empty,
                    Favorite = string.Empty,
                    HasProblem = true
                },
                new HtmlReportRow
                {
                    Name = "Гамма",
                    GroupPath = "Производство",
                    Type = "Веб",
                    ConnectionString = "http://web/base",
                    IsAvailable = true,
                    AvailabilityText = "Доступна",
                    Configuration = "ERP 2.5.17.88",
                    Size = "3,4 ГБ",
                    LastBackup = "01.01.2026 00:00",
                    Modified = "2026-09-28 08:00",
                    Tags = "тест",
                    Favorite = string.Empty,
                    HasProblem = true
                }
            }
        };
    }
}