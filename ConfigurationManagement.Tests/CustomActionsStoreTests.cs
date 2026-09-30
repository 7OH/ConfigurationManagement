using System;
using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты JSON-хранилища пользовательских действий контекстного меню (функция 7, этап 0.3.9.193):
/// единый читаемый файл custom_actions.json, атомарная запись, перезапись по Id без дублей,
/// сортировка и нормализация при чтении, enum строками (с чтением чисел), читаемая кириллица,
/// устойчивость к битому файлу и безопасные Delete/Get.
/// </summary>
public sealed class CustomActionsStoreTests : IDisposable
{
    private readonly string _tempDir;

    public CustomActionsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_custom_actions_tests_" + Guid.NewGuid().ToString("N"));
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

    private static CustomAction Sample(string name, string command = "echo {ИмяБазы}") => new()
    {
        Name = name,
        Command = command,
    };

    private CustomActionsStore CreateStore(string? directoryOverride = null) =>
        new(directoryOverride: directoryOverride ?? _tempDir);

    [Fact]
    public void Save_CreatesFileInOverrideDirectory()
    {
        var store = CreateStore();
        store.Save(Sample("Архивировать"));

        Assert.True(File.Exists(store.FilePath));
        Assert.Equal("custom_actions.json", Path.GetFileName(store.FilePath));
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void Save_SameId_OverwritesWithoutDuplicates()
    {
        var store = CreateStore();
        var id = Guid.NewGuid().ToString("N");
        store.Save(new CustomAction { Id = id, Name = "Первое действие", Command = "echo one" });

        // Тот же Id — запись должна замениться, а не добавиться дубликатом.
        store.Save(new CustomAction { Id = id, Name = "Изменённое действие", Command = "echo two" });

        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal(id, loaded[0].Id);
        Assert.Equal("Изменённое действие", loaded[0].Name);
        Assert.Equal("echo two", loaded[0].Command);
    }

    [Fact]
    public void Save_DifferentIds_BothRecordsPresent()
    {
        var store = CreateStore();
        store.Save(Sample("Альфа"));
        store.Save(Sample("Бета"));

        var loaded = store.LoadAll();
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, a => a.Name == "Альфа");
        Assert.Contains(loaded, a => a.Name == "Бета");
    }

    [Fact]
    public void LoadAll_SortsByOrdinalIgnoreCaseName()
    {
        var store = CreateStore();
        store.Save(Sample("Зета"));
        store.Save(Sample("альфа"));
        store.Save(Sample("Бета"));

        var loaded = store.LoadAll();
        Assert.Equal(3, loaded.Count);
        Assert.Equal("альфа", loaded[0].Name);
        Assert.Equal("Бета", loaded[1].Name);
        Assert.Equal("Зета", loaded[2].Name);
    }

    [Fact]
    public void LoadAll_NoFileOrDirectory_ReturnsEmpty()
    {
        // Каталог существует, файла ещё нет.
        Assert.Empty(CreateStore().LoadAll());

        // Каталога вообще нет — тоже пусто, без исключений.
        var missingDir = Path.Combine(_tempDir, "missing");
        Assert.Empty(CreateStore(missingDir).LoadAll());
    }

    [Fact]
    public void Delete_RemovesById()
    {
        var store = CreateStore();
        store.Save(Sample("Оставить"));
        var toDelete = Sample("Удалить");
        store.Save(toDelete);

        store.Delete(toDelete.Id);

        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal("Оставить", loaded[0].Name);
    }

    [Fact]
    public void Delete_UnknownId_IsNoOp()
    {
        var store = CreateStore();
        store.Save(Sample("Единственное"));

        store.Delete(Guid.NewGuid().ToString("N"));

        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void Delete_NoFile_DoesNotThrow()
    {
        var store = CreateStore();
        store.Delete(Guid.NewGuid().ToString("N")); // файла нет — no-op, без исключений.
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void CorruptFile_ReturnsEmpty_NoThrow()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath, "{ это не json");

        var loaded = store.LoadAll();
        Assert.Empty(loaded);
        // Сам файл остаётся на диске — пользователь может поправить его вручную.
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void LoadAll_NormalizesRecords()
    {
        var store = CreateStore();
        // Ручной «повреждённый» файл: пустые/пробельные имена, нулевой таймаут, null-поля.
        // Ключи — канонические (как их пишет сериализатор: без PropertyNamingPolicy).
        File.WriteAllText(store.FilePath, """
            [
              { "Id": "1", "Name": "Хорошее действие", "Command": "echo ok", "TimeoutMs": -5 },
              { "Id": "2", "Name": "", "Command": "echo skip" },
              { "Id": "3", "Name": "   ", "Command": "echo skip2" },
              { "Id": "4", "Name": "Нулевые поля", "Command": null, "Hotkey": null, "WorkingDirectory": null }
            ]
            """);

        var loaded = store.LoadAll();

        // Записи с пустым/пробельным именем пропущены; осталось две.
        Assert.Equal(2, loaded.Count);
        Assert.DoesNotContain(loaded, a => string.IsNullOrWhiteSpace(a.Name));

        // TimeoutMs <= 0 → дефолт 30 000.
        var good = loaded.Single(a => a.Name == "Хорошее действие");
        Assert.Equal(ExternalCommandRunner.DefaultPreCommandTimeoutMs, good.TimeoutMs);

        // null-поля → пустые строки.
        var withNulls = loaded.Single(a => a.Name == "Нулевые поля");
        Assert.Equal(string.Empty, withNulls.Command);
        Assert.Equal(string.Empty, withNulls.Hotkey);
        Assert.Equal(string.Empty, withNulls.WorkingDirectory);
        Assert.False(string.IsNullOrEmpty(withNulls.Id));
    }

    [Fact]
    public void Enum_SerializedAsStrings()
    {
        var store = CreateStore();
        store.Save(new CustomAction
        {
            Name = "PowerShell-действие",
            Command = "echo test",
            Shell = ScriptShell.PowerShell,
            Scope = CustomActionScope.Base,
        });

        var text = File.ReadAllText(store.FilePath);
        Assert.Contains("\"Shell\": \"PowerShell\"", text);
        Assert.Contains("\"Scope\": \"Base\"", text);
    }

    [Fact]
    public void Enum_ReadsBothStringsAndNumbers()
    {
        var store = CreateStore();
        // Строковые значения («Auto», «Both»).
        File.WriteAllText(store.FilePath, """
            [
              { "Id": "s1", "Name": "Строковый", "Command": "echo s", "Shell": "Auto", "Scope": "Both" }
            ]
            """);
        var fromStrings = store.LoadAll();
        Assert.Single(fromStrings);
        Assert.Equal(ScriptShell.Auto, fromStrings[0].Shell);
        Assert.Equal(CustomActionScope.Both, fromStrings[0].Scope);

        // Числовые значения (Shell: 0 = Auto, Scope: 1 = Group) — чтение принимает и числа.
        File.WriteAllText(store.FilePath, """
            [
              { "Id": "n1", "Name": "Числовой", "Command": "echo n", "Shell": 0, "Scope": 1 }
            ]
            """);
        var fromNumbers = store.LoadAll();
        Assert.Single(fromNumbers);
        Assert.Equal(ScriptShell.Auto, fromNumbers[0].Shell);
        Assert.Equal(CustomActionScope.Group, fromNumbers[0].Scope);
    }

    [Fact]
    public void FileIsReadableUtf8_CyrillicNotEscaped()
    {
        var store = CreateStore();
        store.Save(Sample("Архивировать и отправить", "echo {ИмяБазы}"));

        var text = File.ReadAllText(store.FilePath);
        // Кириллица пишется читаемыми символами, а не \uXXXX-последовательностями.
        Assert.Contains("Архивировать и отправить", text);
        Assert.Contains("{ИмяБазы}", text);
        Assert.DoesNotContain("\\u", text);
    }

    [Fact]
    public void Save_IsAtomic_NoTmpLeftBehind()
    {
        var store = CreateStore();
        store.Save(Sample("Атомарное действие"));
        store.Save(Sample("Ещё одно"));

        Assert.True(File.Exists(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void Get_ReturnsById_OrNull()
    {
        var store = CreateStore();
        var action = Sample("Искомое действие");
        store.Save(action);

        Assert.NotNull(store.Get(action.Id));
        Assert.Equal("Искомое действие", store.Get(action.Id)!.Name);

        Assert.Null(store.Get(Guid.NewGuid().ToString("N")));
        Assert.Null(store.Get(""));
        Assert.Null(store.Get("   "));
    }
}