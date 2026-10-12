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

        // Файл записан под читаемым именем «Имя.<id>.task.json» (issue #366).
        var text = File.ReadAllText(Path.Combine(
            _tempDir,
            ScheduledTaskStore.SanitizeName(task.Name) + "." + task.Id + ".task.json"));
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

    [Fact]
    public void Save_SameTaskTwice_UpdatesInPlace_NoDuplicate()
    {
        // Issue #370: редактирование задания не должно плодить дубль — при сохранении
        // того же объекта (Id не меняется) файл перезаписывается, запись в списке одна.
        var task = new ScheduledTask { Name = "До правки", Time = "02:00" };
        _store.Save(task);

        task.Name = "После правки";
        task.Time = "05:00";
        _store.Save(task);

        var all = _store.LoadAll();
        Assert.Single(all);
        Assert.Equal(task.Id, all[0].Id);
        Assert.Equal("После правки", all[0].Name);
        Assert.Equal("05:00", all[0].Time);
    }

    [Fact]
    public void CreateCopy_NewId_CopiesSettings_ResetsLastRun()
    {
        // Issue #370: «Скопировать» — новый Id, все настройки копируются,
        // история последнего запуска сбрасывается (копия ещё не выполнялась).
        var source = new ScheduledTask
        {
            Name = "Ночное резервирование",
            Kind = ScheduledTaskKind.BackupThenUpdateConfig,
            Enabled = false,
            Time = "03:15",
            DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Tuesday, DayOfWeek.Friday },
            ScenarioId = "scenario-1",
            InfobaseId = "base-1",
            ConfigFilePath = @"C:\cfg\update.cf",
            PlatformCheckExpression = "8.5.*",
            PlatformCheckIncludeTestVersions = true,
            PlatformNotifyChannel = PlatformUpdateNotifyChannel.SystemNotifications,
            ConfigCheckConfiguration = "ЗУП",
            ConfigCheckNotifyChannel = PlatformUpdateNotifyChannel.SystemNotifications,
            LastRunAt = new DateTime(2026, 10, 1, 3, 0, 0),
            LastRunUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            LastRunSuccess = true,
            LastRunMessage = "ok"
        };

        var copy = source.CreateCopy("Ночное резервирование — копия");

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Ночное резервирование — копия", copy.Name);
        Assert.Equal(source.Kind, copy.Kind);
        Assert.False(copy.Enabled);
        Assert.Equal(source.Time, copy.Time);
        Assert.Equal(source.DaysOfWeek, copy.DaysOfWeek);
        Assert.Equal(source.ScenarioId, copy.ScenarioId);
        Assert.Equal(source.InfobaseId, copy.InfobaseId);
        Assert.Equal(source.ConfigFilePath, copy.ConfigFilePath);
        Assert.Equal(source.PlatformCheckExpression, copy.PlatformCheckExpression);
        Assert.True(copy.PlatformCheckIncludeTestVersions);
        Assert.Equal(source.PlatformNotifyChannel, copy.PlatformNotifyChannel);
        Assert.Equal(source.ConfigCheckConfiguration, copy.ConfigCheckConfiguration);
        Assert.Equal(source.ConfigCheckNotifyChannel, copy.ConfigCheckNotifyChannel);
        Assert.Null(copy.LastRunAt);
        Assert.Null(copy.LastRunUtc);
        Assert.False(copy.LastRunSuccess);
        Assert.Null(copy.LastRunMessage);
        // Исходное задание не изменяется.
        Assert.Equal("Ночное резервирование", source.Name);
        Assert.NotNull(source.LastRunAt);
    }

    [Fact]
    public void CreateCopy_AndSave_BothTasksExist()
    {
        // Issue #370: после копии и сохранения копии в списке две разные записи.
        var task = new ScheduledTask { Name = "Оригинал", Time = "01:00" };
        _store.Save(task);

        var copy = task.CreateCopy("Оригинал — копия");
        _store.Save(copy);

        var all = _store.LoadAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.Id == task.Id && t.Name == "Оригинал");
        Assert.Contains(all, t => t.Id == copy.Id && t.Name == "Оригинал — копия");
    }
}