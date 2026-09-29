using System.IO;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты JSON-хранилища сценариев резервирования (<see cref="BackupScenarioStore"/>, issue #320):
/// CRUD, читаемый UTF-8 при сохранении и обратная совместимость с \uXXXX-файлами.
/// Тесты используют временный каталог (directoryOverride) — реальные данные профиля
/// не затрагиваются.
/// </summary>
public sealed class BackupScenarioStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BackupScenarioStore _store;

    public BackupScenarioStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_backup_scenarios_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new BackupScenarioStore(directoryOverride: _tempDir);
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
        var scenario = new BackupScenario
        {
            Name = "Резервная копия",
            FileNameTemplate = "{Base}_{Timestamp}",
            TargetDirectories = new List<string> { @"D:\backups" },
            KeepCount = 7
        };

        _store.Save(scenario);

        var loaded = _store.Get(scenario.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Резервная копия", loaded!.Name);
        Assert.Equal(new[] { @"D:\backups" }, loaded.TargetDirectories);
        Assert.Equal(7, loaded.KeepCount);
    }

    [Fact]
    public void Save_WritesReadableUtf8_NotEscapedUnicode()
    {
        // Issue #320: русские буквы в новых файлах пишутся читаемым UTF-8,
        // а не \uXXXX-последовательностями.
        var scenario = new BackupScenario
        {
            Name = "Резервная копия 2",
            TargetDirectories = new List<string> { @"D:\бэкапы" }
        };

        _store.Save(scenario);

        var text = File.ReadAllText(Path.Combine(_tempDir, scenario.Id + ".scenario.json"));
        Assert.Contains("\"Name\": \"Резервная копия 2\"", text);
        // В JSON обратный слэш пути экранируется как \\.
        Assert.Contains(@"D:\\бэкапы", text);
        Assert.DoesNotContain(@"\u", text);
    }

    [Fact]
    public void Get_LegacyJsonWithEscapedUnicode_ReadsSameStrings()
    {
        // Обратная совместимость (issue #320): старые файлы с \uXXXX-последовательностями
        // читаются как раньше — System.Text.Json разбирает escapes.
        var legacyJson = "{\r\n" +
            "  \"Id\": \"legacy-backup-id\",\r\n" +
            "  \"Name\": \"\\u0420\\u0435\\u0437\\u0435\\u0440\\u0432\\u043d\\u0430\\u044f \\u043a\\u043e\\u043f\\u0438\\u044f\",\r\n" +
            "  \"FileNameTemplate\": \"{Base}_{Timestamp}\",\r\n" +
            "  \"TargetDirectories\": [],\r\n" +
            "  \"IncludeTimestamp\": true,\r\n" +
            "  \"KeepCount\": 0,\r\n" +
            "  \"DeleteOlderThanDays\": 0\r\n" +
            "}";
        File.WriteAllText(Path.Combine(_tempDir, "legacy-backup-id.scenario.json"), legacyJson);

        var loaded = _store.Get("legacy-backup-id");

        Assert.NotNull(loaded);
        Assert.Equal("Резервная копия", loaded!.Name);
    }

    [Fact]
    public void Save_EmptyId_AssignsNewId()
    {
        var scenario = new BackupScenario { Name = "Пустой id" };

        _store.Save(scenario);

        Assert.False(string.IsNullOrWhiteSpace(scenario.Id));
        Assert.NotNull(_store.Get(scenario.Id));
    }

    [Fact]
    public void Delete_RemovesFileAndEntry()
    {
        var scenario = new BackupScenario { Name = "К удалению" };
        _store.Save(scenario);

        _store.Delete(scenario.Id);

        Assert.Null(_store.Get(scenario.Id));
        Assert.Empty(_store.LoadAll());
        Assert.False(Directory.GetFiles(_tempDir, "*.scenario.json").Any());
    }
}