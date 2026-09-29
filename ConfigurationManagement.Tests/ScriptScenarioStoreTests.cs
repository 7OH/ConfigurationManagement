using System.IO;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты JSON-хранилища сценариев запуска скриптов
/// (<see cref="ScriptScenarioStore"/>, issue #308): CRUD, перезапись по идентификатору,
/// санитизация имени файла и устойчивость к битым JSON-файлам.
/// Тесты используют временный каталог — реальные данные профиля не затрагиваются.
/// </summary>
public sealed class ScriptScenarioStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScriptScenarioStore _store;

    public ScriptScenarioStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_scripts_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new ScriptScenarioStore(directoryOverride: _tempDir);
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
            // Временный каталог может быть занят антивирусом; для теста это не критично.
        }
    }

    [Fact]
    public void Save_And_Get_RoundTrip()
    {
        var scenario = new ScriptScenario
        {
            Name = "Отчёт",
            FilePath = @"C:\tools\report.bat",
            Parameters = new List<string> { "/S:%connection.server%", "%date%" }
        };

        _store.Save(scenario);

        var loaded = _store.Get(scenario.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Отчёт", loaded!.Name);
        Assert.Equal(@"C:\tools\report.bat", loaded.FilePath);
        Assert.Equal(new[] { "/S:%connection.server%", "%date%" }, loaded.Parameters);
    }

    [Fact]
    public void Save_And_Get_RoundTrip_IncludesWorkingDirectory()
    {
        // Issue #308, п.7: «Папка запуска» сериализуется и читается обратно.
        var scenario = new ScriptScenario
        {
            Name = "Отчёт с рабочей папкой",
            FilePath = @"C:\tools\report.bat",
            WorkingDirectory = @"C:\reports\output",
            Parameters = new List<string> { "/O:%date%" }
        };

        _store.Save(scenario);

        var loaded = _store.Get(scenario.Id);
        Assert.NotNull(loaded);
        Assert.Equal(@"C:\reports\output", loaded!.WorkingDirectory);
    }

    [Fact]
    public void Get_LegacyJsonWithoutWorkingDirectory_MigratesToEmpty()
    {
        // Миграция старых JSON-файлов сценариев (issue #308, п.7): файл без поля
        // WorkingDirectory загружается без ошибок, значение — пустое (поведение прежнее).
        var legacyJson = "{\r\n" +
            "  \"Id\": \"legacy-id\",\r\n" +
            "  \"Name\": \"Старый\",\r\n" +
            "  \"FilePath\": \"old.bat\",\r\n" +
            "  \"Parameters\": [],\r\n" +
            "  \"HideWindow\": true\r\n" +
            "}";
        File.WriteAllText(Path.Combine(_tempDir, "legacy-id.script.json"), legacyJson);

        var loaded = _store.Get("legacy-id");

        Assert.NotNull(loaded);
        Assert.Equal("Старый", loaded!.Name);
        Assert.Equal("", loaded.WorkingDirectory);
    }

    [Fact]
    public void Save_EmptyId_AssignsNewId()
    {
        var scenario = new ScriptScenario { Id = "", Name = "Пустой id" };

        _store.Save(scenario);

        Assert.False(string.IsNullOrWhiteSpace(scenario.Id));
        Assert.NotNull(_store.Get(scenario.Id));
    }

    [Fact]
    public void LoadAll_SortsByName_AndSkipsBrokenFiles()
    {
        _store.Save(new ScriptScenario { Name = "Бета", FilePath = "b.bat" });
        _store.Save(new ScriptScenario { Name = "Альфа", FilePath = "a.bat" });

        // Битый JSON-файл рядом не должен ронять загрузку остальных.
        File.WriteAllText(Path.Combine(_tempDir, "broken.script.json"), "{ не json");

        var all = _store.LoadAll();

        Assert.Equal(2, all.Count);
        Assert.Equal(new[] { "Альфа", "Бета" }, all.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Save_SameId_OverwritesExistingFile()
    {
        var scenario = new ScriptScenario
        {
            Id = "fixed-id",
            Name = "Старое имя",
            FilePath = "old.bat"
        };
        _store.Save(scenario);

        scenario.Name = "Новое имя";
        scenario.FilePath = "new.bat";
        _store.Save(scenario);

        // Перезапись не плодит второй файл: тот же id → тот же файл.
        var files = Directory.GetFiles(_tempDir, "*.script.json");
        Assert.Single(files);

        var loaded = _store.Get("fixed-id");
        Assert.NotNull(loaded);
        Assert.Equal("Новое имя", loaded!.Name);
        Assert.Equal("new.bat", loaded.FilePath);
    }

    [Fact]
    public void LoadAll_SameIdRepeatedSave_NoDuplicates()
    {
        // Issue #308: редактирование сценария не должно плодить копию — повторный Save
        // изменённого объекта (тот же Id, как после ApplyTo) перезаписывает файл,
        // и LoadAll возвращает один элемент с прежним Id и новыми полями.
        var scenario = new ScriptScenario
        {
            Name = "Сценарий",
            FilePath = "tool.bat",
            Parameters = new List<string> { "%name%" },
            HideWindow = true
        };
        _store.Save(scenario);
        var originalId = scenario.Id;

        scenario.Name = "Сценарий (изменён)";
        scenario.FilePath = "tool2.bat";
        scenario.Parameters = new List<string> { "%connection.password%" };
        scenario.HideWindow = false;
        _store.Save(scenario);

        var all = _store.LoadAll();

        Assert.Single(all);
        Assert.Equal(originalId, all[0].Id);
        Assert.Equal("Сценарий (изменён)", all[0].Name);
        Assert.Equal("tool2.bat", all[0].FilePath);
        Assert.Equal(new[] { "%connection.password%" }, all[0].Parameters);
        Assert.False(all[0].HideWindow);
        Assert.Single(Directory.GetFiles(_tempDir, "*.script.json"));
    }

    [Fact]
    public void Delete_RemovesFileAndEntry()
    {
        var scenario = new ScriptScenario { Name = "К удалению", FilePath = "del.bat" };
        _store.Save(scenario);

        _store.Delete(scenario.Id);

        Assert.Null(_store.Get(scenario.Id));
        Assert.Empty(_store.LoadAll());
        Assert.False(Directory.GetFiles(_tempDir, "*.script.json").Any());
    }

    [Fact]
    public void Save_InvalidFileNameCharsInId_SanitizesWithoutErrors()
    {
        var scenario = new ScriptScenario
        {
            Id = "id:with/illegal*chars",
            Name = "Санитизация",
            FilePath = "tool.exe"
        };

        _store.Save(scenario);

        Assert.Single(Directory.GetFiles(_tempDir, "*.script.json"));
        Assert.NotNull(_store.Get("id:with/illegal*chars"));
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        Assert.Null(_store.Get("no-such-id"));
    }

    [Fact]
    public void LoadAll_EmptyDirectory_ReturnsEmptyList()
    {
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Save_And_Get_RoundTrip_IncludesShell()
    {
        // Issue #308, п.9: «Интерпретатор» сериализуется и читается обратно.
        var scenario = new ScriptScenario
        {
            Name = "Сценарий с PowerShell",
            FilePath = @"C:\tools\report.ps1",
            Shell = ScriptShell.PowerShell
        };

        _store.Save(scenario);

        var loaded = _store.Get(scenario.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ScriptShell.PowerShell, loaded!.Shell);
    }

    [Fact]
    public void Get_LegacyJsonWithoutShell_MigratesToAuto()
    {
        // Миграция старых JSON-файлов сценариев (issue #308, п.9): файл без поля Shell
        // загружается без ошибок, значение — Auto (прежнее поведение: cmd/sh по платформе).
        var legacyJson = "{\r\n" +
            "  \"Id\": \"legacy-shell-id\",\r\n" +
            "  \"Name\": \"Старый без шелла\",\r\n" +
            "  \"FilePath\": \"old.bat\",\r\n" +
            "  \"Parameters\": [],\r\n" +
            "  \"HideWindow\": true\r\n" +
            "}";
        File.WriteAllText(Path.Combine(_tempDir, "legacy-shell-id.script.json"), legacyJson);

        var loaded = _store.Get("legacy-shell-id");

        Assert.NotNull(loaded);
        Assert.Equal(ScriptShell.Auto, loaded!.Shell);
    }

    [Fact]
    public void Save_WritesReadableUtf8_NotEscapedUnicode()
    {
        // Issue #320: русские буквы в новых файлах пишутся читаемым UTF-8,
        // а не \uXXXX-последовательностями.
        var scenario = new ScriptScenario
        {
            Name = "Тест 2",
            FilePath = @"C:\tools\report.bat",
            Parameters = new List<string> { "Параметр с кириллицей", "%name%" }
        };

        _store.Save(scenario);

        var text = File.ReadAllText(Path.Combine(_tempDir, scenario.Id + ".script.json"));
        Assert.Contains("\"Name\": \"Тест 2\"", text);
        Assert.Contains("Параметр с кириллицей", text);
        Assert.DoesNotContain(@"\u", text);
    }

    [Fact]
    public void Get_LegacyJsonWithEscapedUnicode_ReadsSameStrings()
    {
        // Обратная совместимость (issue #320): старые файлы с \uXXXX-последовательностями
        // читаются как раньше — System.Text.Json разбирает escapes.
        var legacyJson = "{\r\n" +
            "  \"Id\": \"legacy-unicode-id\",\r\n" +
            "  \"Name\": \"\\u0422\\u0435\\u0441\\u0442 2\",\r\n" +
            "  \"FilePath\": \"old.bat\",\r\n" +
            "  \"Parameters\": [\"\\u041f\\u0430\\u0440\\u0430\\u043c\\u0435\\u0442\\u0440\"],\r\n" +
            "  \"HideWindow\": true\r\n" +
            "}";
        File.WriteAllText(Path.Combine(_tempDir, "legacy-unicode-id.script.json"), legacyJson);

        var loaded = _store.Get("legacy-unicode-id");

        Assert.NotNull(loaded);
        Assert.Equal("Тест 2", loaded!.Name);
        Assert.Equal(new[] { "Параметр" }, loaded.Parameters);
    }
}