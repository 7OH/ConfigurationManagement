using System;
using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты цепочки «связанная конфигурация / свойства конфигурации базы → URL каталога релизов →
/// парсинг ответа» для окна проверки обновлений F9 (issue #323): каталог строится из свойств
/// конфигурации (вкладка «Платформа»), явное связывание остаётся override, а при пустом нике
/// на releases.1c.ru проверка честно сообщает причину.
/// </summary>
public sealed class UpdateCheckCatalogTests : IDisposable
{
    private readonly string _tempDir;

    public UpdateCheckCatalogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_update_check_catalog_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Игнорируем: каталог мог быть занят или уже удалён.
        }
    }

    private CustomConfigTypesStore CreateStore() => new(directoryOverride: _tempDir);

    /// <summary>Воспроизводит шаг «после связывания»: запись с кодом связи в файле + поиск по коду.</summary>
    private static OneCConfigType? FindLinked(ICustomConfigTypesStore store, string code) =>
        store.LoadAll().FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AfterLinking_UrlBuiltFromConfigNick()
    {
        var store = CreateStore();
        // Пользователь дополнил встроенную ЗУП (правка предопределённой строки): создана
        // копия-переопределение с кодом ZUP и ником на releases.1c.ru — F9 после связывания
        // находит её по коду связи и строит каталог из ника.
        store.Save(new[]
        {
            new OneCConfigType
            {
                Code = "ZUP",
                Name = "Зарплата и управление персоналом",
                Nick = "Zup31Nick",
                OverridesBuiltIn = true,
                Editions = { new OneCConfigEdition { Name = "3.1", Red = "3.1" } },
            },
        });

        var linked = FindLinked(store, "ZUP");
        Assert.NotNull(linked);

        var url = new OneCUpdatesService(
                repository: new InfobaseRepository(directory: _tempDir),
                logger: new TestLogger())
            .BuildUpdateUrl(linked, linked!.DefaultEdition, null, null);

        Assert.Equal("https://releases.1c.ru/project/Zup31Nick", url);
    }

    [Fact]
    public void PersonalSegment_OverridesConfigNick()
    {
        var store = CreateStore();
        store.Save(new[]
        {
            new OneCConfigType { Code = "BP", Name = "Бухгалтерия предприятия", Nick = "AccountingCorp30" },
        });

        var linked = FindLinked(store, "BP")!;
        var service = new OneCUpdatesService(new InfobaseRepository(directory: _tempDir), new TestLogger());

        var url = service.BuildUpdateUrl(linked, linked.DefaultEdition, null, "MyPersonalSegment");

        Assert.Equal("https://releases.1c.ru/project/MyPersonalSegment", url);
    }

    [Fact]
    public void PropertiesFromPlatformTab_AutoMatchConfig_BuildsUrl()
    {
        // База НЕ связана, но свойства конфигурации определены (вкладка «Платформа»):
        // каталог релизов строится из них автоматически (issue #323). Пользовательская ЗУП
        // с ником переопределяет встроенную — в общем списке она и подбирается по имени.
        var store = CreateStore();
        store.Save(new[]
        {
            new OneCConfigType
            {
                Code = "ZUP",
                Name = "Зарплата и управление персоналом",
                Nick = "Zup30Nick",
                OverridesBuiltIn = true,
                Editions = { new OneCConfigEdition { Name = "3.0", Red = "3.0" } },
            },
        });

        var config = ConfigTypeMatcher.FindByInfobaseName(store.LoadAll(), "Зарплата и управление персоналом");
        Assert.NotNull(config);

        var service = new OneCUpdatesService(new InfobaseRepository(directory: _tempDir), new TestLogger());
        var url = service.BuildUpdateUrl(config, config!.DefaultEdition, null, null);

        Assert.Equal("https://releases.1c.ru/project/Zup30Nick", url);
    }

    [Fact]
    public void LinkedConfigWithoutNick_ReturnsEmptyUrl()
    {
        // Встроенная ЗУП в исходном наборе не имеет ника (точный ник не подтверждён):
        // URL построить нельзя — проверка должна завершиться понятной ошибкой, а не «молчать».
        var store = CreateStore();
        var linked = FindLinked(store, "ZUP");
        Assert.NotNull(linked);
        Assert.True(string.IsNullOrWhiteSpace(linked!.Nick));

        var service = new OneCUpdatesService(new InfobaseRepository(directory: _tempDir), new TestLogger());
        var url = service.BuildUpdateUrl(linked, linked.DefaultEdition, null, null);

        Assert.Equal(string.Empty, url);
    }

    // ---------- Парсер HTML-ответа каталога releases.1c.ru/project/<ник> (issue #323). ----------

    [Fact]
    public void ParseProjectHtml_TakesFirstRowOfVersionsTable()
    {
        const string html = """
            <html><body>
            <table id="versionsTable">
              <tr><td>1</td><td><a href="/version_files?nick=Zup31Nick&ver=3.1.14.1">3.1.14.1</a></td></tr>
              <tr><td>2</td><td><a href="/version_files?nick=Zup31Nick&ver=3.1.13.5">3.1.13.5</a></td></tr>
            </table>
            </body></html>
            """;

        var version = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html);

        Assert.Equal("3.1.14.1", version);
    }

    [Fact]
    public void ParseProjectHtml_Fallback_WhenNoTable()
    {
        const string html =
            "<div><a href=\"/version_files?nick=X&ver=8.3.24.1646\">8.3.24.1646</a></div>";

        var version = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html);

        Assert.Equal("8.3.24.1646", version);
    }

    [Fact]
    public void ParseProjectHtml_EmptyOrGarbage_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, OneCUpdatesService.ParseLatestVersionFromProjectHtml(null!));
        Assert.Equal(string.Empty, OneCUpdatesService.ParseLatestVersionFromProjectHtml("   "));
        Assert.Equal(string.Empty, OneCUpdatesService.ParseLatestVersionFromProjectHtml("<html>нет версий</html>"));
    }

    [Fact]
    public void ParseArchiveLinks_TakesMaxVersion()
    {
        // Имена дистрибутивов вида 1c_<версия>.zip (паттерн ArchiveLinkRegex: «1c…zip»).
        const string html =
            "<a href='https://cdn.example/1c_3.0.13.7.zip'>v1</a>" +
            "<a href='https://cdn.example/1c_3.0.15.2.zip'>v2</a>" +
            "<a href='https://cdn.example/1c_3.0.9.1.zip'>v3</a>";

        var version = OneCUpdatesService.ParseLatestVersion(html);

        Assert.Equal("3.0.15.2", version);
    }

    /// <summary>Минимальный логгер для конструктора OneCUpdatesService (без внешних зависимостей).</summary>
    private sealed class TestLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}