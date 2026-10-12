using System;
using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Читаемые имена файлов заданий в <see cref="ScheduledTaskStore"/> (issue #366,
/// комментарий 7OH): «Имя.<id>.task.json» как в папке сценариев, миграция старого
/// формата «<id>.task.json», поиск/удаление по Id в обоих форматах.
/// </summary>
public class ScheduledTaskStoreNamingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cm_sched_store_" + Guid.NewGuid().ToString("N"));

    public ScheduledTaskStoreNamingTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* тестовая свалка */ }
    }

    private ScheduledTaskStore CreateStore() => new(null, _dir);

    [Fact]
    public void Save_UsesReadableNameWithIdSuffix()
    {
        var store = CreateStore();
        var task = new ScheduledTask { Id = new string('a', 32), Name = "Резервная копия УТ" };

        store.Save(task);

        var expected = $"Резервная копия УТ.{task.Id}.task.json";
        Assert.True(File.Exists(Path.Combine(_dir, expected)), "файл должен называться «Имя.<id>.task.json»");
        Assert.Single(Directory.EnumerateFiles(_dir, "*.task.json"));
    }

    [Fact]
    public void Save_MigratesOldFormatFileNames()
    {
        var store = CreateStore();
        var task = new ScheduledTask { Id = new string('b', 32), Name = "Обновление конфигурации" };
        var oldPath = Path.Combine(_dir, task.Id + ".task.json");
        File.WriteAllText(oldPath, "{\"Id\":\"" + task.Id + "\",\"Name\":\"Обновление конфигурации\"}");

        store.Save(task); // сохраняем тот же Id под новым именем

        Assert.False(File.Exists(oldPath), "старый файл «<id>.task.json» должен быть удалён при миграции");
        Assert.True(File.Exists(Path.Combine(_dir, $"Обновление конфигурации.{task.Id}.task.json")),
            "новый файл должен существовать");
        Assert.Single(Directory.EnumerateFiles(_dir, "*.task.json"));
    }

    [Fact]
    public void Save_RenameMovesFile_NoDuplicates()
    {
        var store = CreateStore();
        var task = new ScheduledTask { Id = new string('c', 32), Name = "Старое имя" };
        store.Save(task);

        task.Name = "Новое имя";
        store.Save(task);

        var files = Directory.EnumerateFiles(_dir, "*.task.json").ToList();
        Assert.Single(files);
        Assert.Contains("Новое имя." + task.Id, Path.GetFileName(files[0]));
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void LoadAll_ReadsIdFromFileName_NewAndOldFormats()
    {
        var store = CreateStore();
        var task = new ScheduledTask { Id = new string('d', 32), Name = "Задание" };
        store.Save(task);
        // «Ручное» переименование файла: имя стало другим, id остался прежним.
        File.Move(
            Path.Combine(_dir, $"Задание.{task.Id}.task.json"),
            Path.Combine(_dir, $"Другое имя.{task.Id}.task.json"));

        var loaded = store.LoadAll();

        var item = Assert.Single(loaded);
        Assert.Equal(task.Id, item.Id);
        Assert.Equal("Задание", item.Name); // имя читается из JSON
    }

    [Fact]
    public void FileNameToId_ExtractsIdFromBothFormats()
    {
        Assert.Equal(new string('e', 32), ScheduledTaskStore.FileNameToId($"Имя с точками.{new string('e', 32)}.task.json"));
        Assert.Equal(new string('f', 32), ScheduledTaskStore.FileNameToId($"{new string('f', 32)}.task.json"));
        Assert.Null(ScheduledTaskStore.FileNameToId("без суффикса.json"));
    }

    [Fact]
    public void SanitizeName_ReplacesInvalidChars_AndKeepsCyrillic()
    {
        Assert.Equal("Резервная копия УТ", ScheduledTaskStore.SanitizeName("Резервная копия УТ"));
        Assert.Equal("A_B", ScheduledTaskStore.SanitizeName("A/B"));
        // Ведущая точка (скрытый файл на Linux) получает префикс «_» — точка остаётся в имени.
        Assert.Equal("_.hidden", ScheduledTaskStore.SanitizeName(".hidden"));
        Assert.Equal("_CON", ScheduledTaskStore.SanitizeName("CON"));
        Assert.Equal("", ScheduledTaskStore.SanitizeName("   "));
    }

    [Fact]
    public void Get_And_Delete_WorkInBothFormats()
    {
        var store = CreateStore();
        var task = new ScheduledTask { Id = new string('g', 32), Name = "Для удаления" };
        store.Save(task);

        // Новый формат.
        Assert.NotNull(store.Get(task.Id));
        store.Delete(task.Id);
        Assert.Empty(Directory.EnumerateFiles(_dir, "*.task.json"));

        // Старый формат («появился» файл старого вида).
        var oldPath = Path.Combine(_dir, task.Id + ".task.json");
        File.WriteAllText(oldPath, "{\"Id\":\"" + task.Id + "\",\"Name\":\"Старый вид\"}");
        Assert.NotNull(store.Get(task.Id));
        store.Delete(task.Id);
        Assert.Empty(Directory.EnumerateFiles(_dir, "*.task.json"));
    }
}