using System;
using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты JSON-хранилища учётных записей ИТС (issue #333): идемпотентная миграция
/// логина/пароля из старых настроек, правила «Основная» (2+ → первый найденный,
/// 0 → первая запись, удаление основной → первая), CRUD, резолв для типовой
/// конфигурации (пусто → основная) и маскирование пароля в журнале.
/// </summary>
public sealed class ItsAccountsStoreTests : IDisposable
{
    private readonly string _tempDir;

    public ItsAccountsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_its_accounts_tests_" + Guid.NewGuid().ToString("N"));
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

    private static ItsAccount Sample(string name, string login, bool primary = false) => new()
    {
        Name = name,
        Login = login,
        Password = "pwd-" + login,
        IsPrimary = primary,
    };

    private ItsAccountsStore CreateStore(IInfobaseRepository? repository = null) =>
        new(repository, directoryOverride: _tempDir);

    // ---------- Миграция из старых настроек (AppSettings.UpdatesLogin/UpdatesPassword) ----------

    [Fact]
    public void MigrationFromSettings_CreatesPrimaryEntry_AndIsIdempotent()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings
        {
            UpdatesLogin = "login@its",
            UpdatesPassword = "secret",
        });

        var store = CreateStore(repo);

        // Первый запуск: файла нет — текущие логин/пароль переносятся в запись «Основная».
        var first = store.Load();
        var entry = Assert.Single(first);
        Assert.Equal("Основная", entry.Name);
        Assert.Equal("login@its", entry.Login);
        Assert.Equal("secret", entry.Password);
        Assert.True(entry.IsPrimary);
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal("its_accounts.json", Path.GetFileName(store.FilePath));

        // Повторный запуск: читается только файл, дубли не создаются.
        var second = store.Load();
        Assert.Single(second);
        Assert.Equal("login@its", second[0].Login);
    }

    [Fact]
    public void MigrationFromSettings_WhenEmpty_DoesNotCreateFile()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings()); // UpdatesLogin пуст по умолчанию.

        var store = CreateStore(repo);
        Assert.Empty(store.Load());
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Migration_KeepsOldSettingsForBackwardCompatibility()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings
        {
            UpdatesLogin = "legacy@its",
            UpdatesPassword = "legacy-pwd",
        });

        var store = CreateStore(repo);
        store.Load();

        // Старые настройки НЕ удаляются и НЕ изменяются (обратная совместимость).
        var settings = repo.LoadSettings();
        Assert.Equal("legacy@its", settings.UpdatesLogin);
        Assert.Equal("legacy-pwd", settings.UpdatesPassword);

        // После миграции файл «выигрывает» у настроек.
        repo.SaveSettings(new AppSettings
        {
            UpdatesLogin = "other@its",
        });
        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal("legacy@its", loaded[0].Login);
    }

    // ---------- Правила «Основная» ----------

    [Fact]
    public void Load_TwoPrimary_KeepsFirstFound()
    {
        var store = CreateStore();
        var first = Sample("Первая", "a@its");
        var second = Sample("Вторая", "b@its", primary: true);
        var third = Sample("Третья", "c@its", primary: true);
        first.IsPrimary = true;
        second.IsPrimary = true;
        store.Save(new[] { first, second, third });

        var loaded = store.Load();
        Assert.Equal(3, loaded.Count);
        // 2+ основных → основной остаётся первый найденный, остальные сброшены.
        Assert.Single(loaded, a => a.IsPrimary);
        Assert.True(loaded[0].IsPrimary);
        Assert.False(loaded[1].IsPrimary);
        Assert.False(loaded[2].IsPrimary);
    }

    [Fact]
    public void Load_NoPrimary_MakesFirstPrimary()
    {
        var store = CreateStore();
        store.Save(new[]
        {
            Sample("Первая", "a@its"),
            Sample("Вторая", "b@its"),
        });

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        // 0 основных → основной становится первая запись.
        Assert.True(loaded[0].IsPrimary);
        Assert.False(loaded[1].IsPrimary);
    }

    [Fact]
    public void Delete_Primary_MakesFirstPrimary()
    {
        var store = CreateStore();
        var first = Sample("Первая", "a@its");
        var second = Sample("Вторая", "b@its");
        var third = Sample("Третья", "c@its");
        store.Save(new[] { first, second, third });
        store.SetPrimary(second.Id); // основная — вторая

        store.Delete(second.Id);

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("Первая", loaded[0].Name);
        // Удалена основная → основной становится первая запись списка.
        Assert.True(loaded[0].IsPrimary);
        Assert.False(loaded[1].IsPrimary);
    }

    [Fact]
    public void Delete_NonPrimary_DoesNotChangePrimary()
    {
        var store = CreateStore();
        var first = Sample("Первая", "a@its", primary: true);
        var second = Sample("Вторая", "b@its");
        store.Save(new[] { first, second });

        store.Delete(second.Id);

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.True(loaded[0].IsPrimary);
    }

    [Fact]
    public void SetPrimary_OnlyViaStore_SinglePrimary()
    {
        var store = CreateStore();
        var first = Sample("Первая", "a@its");
        var second = Sample("Вторая", "b@its");
        store.Save(new[] { first, second });

        // Смена основной только через SetPrimary (UI-флажок read-only).
        store.SetPrimary(second.Id);

        var loaded = store.Load();
        Assert.True(loaded[1].IsPrimary);
        Assert.False(loaded[0].IsPrimary);
        Assert.Single(loaded, a => a.IsPrimary);
    }

    // ---------- CRUD ----------

    [Fact]
    public void Upsert_AddNew_SetsPrimaryForFirstEntry()
    {
        var store = CreateStore();
        store.Upsert(Sample("Первая", "a@its"));

        var loaded = store.Load();
        var entry = Assert.Single(loaded);
        Assert.True(entry.IsPrimary); // первая запись пустого справочника — основная
    }

    [Fact]
    public void Upsert_SecondEntry_IsNotPrimary()
    {
        var store = CreateStore();
        store.Upsert(Sample("Первая", "a@its"));
        store.Upsert(Sample("Вторая", "b@its"));

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.True(loaded[0].IsPrimary);
        Assert.False(loaded[1].IsPrimary);
    }

    [Fact]
    public void Upsert_EditExisting_KeepsPrimaryFlag()
    {
        var store = CreateStore();
        store.Upsert(Sample("Первая", "a@its"));
        var primary = store.GetPrimary()!;

        // Правка (та же Id) не должна снять/изменить флаг «Основная».
        store.Upsert(new ItsAccount
        {
            Id = primary.Id,
            Name = "Переименованная",
            Login = "new@its",
            Password = "new-pwd",
        });

        var loaded = store.Load();
        var edited = Assert.Single(loaded);
        Assert.Equal("Переименованная", edited.Name);
        Assert.Equal("new@its", edited.Login);
        Assert.True(edited.IsPrimary);
    }

    [Fact]
    public void SaveLoad_Roundtrip_PreservesFields_ReadableUtf8()
    {
        var store = CreateStore();
        store.Save(new[] { Sample("Основная учётка", "ivan@its.ru", primary: true) });

        var text = File.ReadAllText(store.FilePath);
        Assert.Contains("ivan@its.ru", text);
        Assert.Contains("pwd-ivan@its.ru", text);
        // Кириллица пишется читаемыми символами, а не \uXXXX-последовательностями.
        Assert.DoesNotContain("\\u", text);

        var loaded = store.Load();
        var entry = Assert.Single(loaded);
        Assert.Equal("Основная учётка", entry.Name);
        Assert.Equal("ivan@its.ru", entry.Login);
        Assert.Equal("pwd-ivan@its.ru", entry.Password);
        Assert.True(entry.IsPrimary);
    }

    [Fact]
    public void CorruptFile_ReturnsEmpty_NoThrow()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath, "{ это не json");

        Assert.Empty(store.Load());
        Assert.True(File.Exists(store.FilePath)); // файл остаётся для ручной правки
    }

    [Fact]
    public void GetById_And_GetPrimary_Work()
    {
        var store = CreateStore();
        var first = Sample("Первая", "a@its");
        var second = Sample("Вторая", "b@its");
        store.Save(new[] { first, second });
        store.SetPrimary(second.Id);

        Assert.Equal(second.Id, store.GetById(second.Id)?.Id);
        Assert.Null(store.GetById("missing"));
        Assert.Equal("Вторая", store.GetPrimary()?.Name);
    }

    // ---------- Резолв учётной записи для типовой конфигурации (#333/#321) ----------

    [Fact]
    public void Resolve_EmptyAccountId_ReturnsPrimary()
    {
        var store = CreateStore();
        store.Save(new[]
        {
            Sample("Основная", "main@its"),
            Sample("Дополнительная", "extra@its"),
        });

        // Типовая конфигурация без указания учётной записи (пусто) → «Основная».
        var config = new OneCConfigType { Code = "BP", Name = "Бухгалтерия", AccountId = "" };
        var resolved = store.Resolve(config.AccountId);
        Assert.NotNull(resolved);
        Assert.True(resolved!.IsPrimary);
        Assert.Equal("main@its", resolved.Login);
    }

    [Fact]
    public void Resolve_KnownAccountId_ReturnsThatAccount()
    {
        var store = CreateStore();
        var extra = Sample("Дополнительная", "extra@its");
        store.Save(new[]
        {
            Sample("Основная", "main@its"),
            extra,
        });

        var config = new OneCConfigType { Code = "ZUP", Name = "ЗУП", AccountId = extra.Id };
        var resolved = store.Resolve(config.AccountId);
        Assert.NotNull(resolved);
        Assert.Equal("extra@its", resolved!.Login);
    }

    [Fact]
    public void Resolve_MissingAccountId_FallsBackToPrimary()
    {
        var store = CreateStore();
        store.Save(new[] { Sample("Основная", "main@its", primary: true) });

        var resolved = store.Resolve("несуществующий-id");
        Assert.NotNull(resolved);
        Assert.Equal("main@its", resolved!.Login);
    }

    // ---------- Маскирование пароля в журнале ----------

    [Fact]
    public void MaskPasswordForLog_HidesPassword()
    {
        var account = Sample("Моя учётка", "user@its");
        const string logText = "Операция с учётной записью «Моя учётка», пароль pwd-user@its использован";

        var masked = ItsAccountsStore.MaskPasswordForLog(logText, account.Password);

        Assert.DoesNotContain(account.Password, masked);
        Assert.Contains("***", masked);
    }

    [Fact]
    public void MaskPasswordForLog_EmptySecret_ReturnsText()
    {
        const string logText = "Запись без секрета";
        Assert.Equal(logText, ItsAccountsStore.MaskPasswordForLog(logText, string.Empty));
    }
}