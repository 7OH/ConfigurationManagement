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
}