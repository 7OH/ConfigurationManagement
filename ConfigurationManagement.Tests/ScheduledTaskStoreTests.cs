using System.IO;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты JSON-хранилища заданий по расписанию (<see cref="ScheduledTaskStore"/>, issue #320):
/// CRUD, читаемый UTF-8 при сохранении и обратная совместимость с \uXXXX-файлами.
/// Тесты используют временный каталог (directoryOverride) — реальные данные профиля
/// не затрагиваются.
/// </summary>
public sealed class ScheduledTaskStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScheduledTaskStore _store;

    public ScheduledTaskStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_tasks_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new ScheduledTaskStore(directoryOverride: _tempDir);
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
        var task = new ScheduledTask
        {
            Name = "Задание на субботу",
            Kind = ScheduledTaskKind.Backup,
            Time = "03:30",
            DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Saturday }
        };

        _store.Save(task);

        var loaded = _store.Get(task.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Задание на субботу", loaded!.Name);
        Assert.Equal(ScheduledTaskKind.Backup, loaded.Kind);
        Assert.Equal(new[] { DayOfWeek.Saturday }, loaded.DaysOfWeek);
    }

    [Fact]
    public void Save_WritesReadableUtf8_NotEscapedUnicode()
    {
        // Issue #320: русские буквы в новых файлах пишутся читаемым UTF-8,
        // а не \uXXXX-последовательностями.
        var task = new ScheduledTask
        {
            Name = "Ежедневное резервирование",
            Kind = ScheduledTaskKind.Backup,
            Time = "02:00"
        };

        _store.Save(task);

        var text = File.ReadAllText(Path.Combine(_tempDir, task.Id + ".task.json"));
        Assert.Contains("\"Name\": \"Ежедневное резервирование\"", text);
        Assert.DoesNotContain(@"\u", text);
    }

    [Fact]
    public void Get_LegacyJsonWithEscapedUnicode_ReadsSameStrings()
    {
        // Обратная совместимость (issue #320): старые файлы с \uXXXX-последовательностями
        // читаются как раньше — System.Text.Json разбирает escapes.
        // ScheduledTaskStore без строкового конвертера enum: Kind хранится числом (0 = Backup).
        var legacyJson = "{\r\n" +
            "  \"Id\": \"legacy-task-id\",\r\n" +
            "  \"Name\": \"\\u0415\\u0436\\u0435\\u0434\\u043d\\u0435\\u0432\\u043d\\u043e\\u0435 \\u0440\\u0435\\u0437\\u0435\\u0440\\u0432\\u0438\\u0440\\u043e\\u0432\\u0430\\u043d\\u0438\\u0435\",\r\n" +
            "  \"Kind\": 0,\r\n" +
            "  \"Enabled\": true,\r\n" +
            "  \"Time\": \"02:00\",\r\n" +
            "  \"DaysOfWeek\": []\r\n" +
            "}";
        File.WriteAllText(Path.Combine(_tempDir, "legacy-task-id.task.json"), legacyJson);

        var loaded = _store.Get("legacy-task-id");

        Assert.NotNull(loaded);
        Assert.Equal("Ежедневное резервирование", loaded!.Name);
        Assert.Equal(ScheduledTaskKind.Backup, loaded.Kind);
    }

    [Fact]
    public void Save_EmptyId_AssignsNewId()
    {
        var task = new ScheduledTask { Name = "Пустой id" };

        _store.Save(task);

        Assert.False(string.IsNullOrWhiteSpace(task.Id));
        Assert.NotNull(_store.Get(task.Id));
    }

    [Fact]
    public void Delete_RemovesFileAndEntry()
    {
        var task = new ScheduledTask { Name = "К удалению" };
        _store.Save(task);

        _store.Delete(task.Id);

        Assert.Null(_store.Get(task.Id));
        Assert.Empty(_store.LoadAll());
        Assert.False(Directory.GetFiles(_tempDir, "*.task.json").Any());
    }
}