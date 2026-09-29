using System;
using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты JSON-хранилища пользовательских типовых конфигураций 1С (issue #321):
/// запись/чтение читаемого файла custom_config_types.json, идемпотентная миграция
/// из AppSettings.CustomConfigTypes, единый список «встроенные + пользовательские»,
/// защита от подмены предопределённого набора через файл и устойчивость к битому файлу.
/// </summary>
public sealed class CustomConfigTypesStoreTests : IDisposable
{
    private readonly string _tempDir;

    public CustomConfigTypesStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_custom_config_types_tests_" + Guid.NewGuid().ToString("N"));
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

    private static OneCConfigType Sample(string name, string red) => new()
    {
        Code = "C_" + name,
        Name = name,
        UrlCode = "url_" + name,
        Nick = "nick_" + name,
        Editions =
        {
            new OneCConfigEdition { Name = red, Red = red, SubRed = "1", UrlOverride = "" },
        },
    };

    private CustomConfigTypesStore CreateStore(IInfobaseRepository? repository = null) =>
        new(repository, directoryOverride: _tempDir);

    [Fact]
    public void SaveLoad_Roundtrip_PreservesItemsAndOrder()
    {
        var store = CreateStore();
        var items = new[] { Sample("Бухгалтерия предприятия", "3.0"), Sample("Управление торговлей", "11") };

        store.Save(items);

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("Бухгалтерия предприятия", loaded[0].Name);
        Assert.Equal("Управление торговлей", loaded[1].Name);
        Assert.Equal("C_Бухгалтерия предприятия", loaded[0].Code);
        Assert.Equal("nick_Бухгалтерия предприятия", loaded[0].Nick);
        Assert.Single(loaded[0].Editions);
        Assert.Equal("3.0", loaded[0].Editions[0].Red);
        Assert.Equal("1", loaded[0].Editions[0].SubRed);
    }

    [Fact]
    public void FileIsReadableUtf8_CyrillicNotEscaped()
    {
        var store = CreateStore();
        store.Save(new[] { Sample("Розница", "2.3") });

        var text = File.ReadAllText(store.FilePath);
        // Кириллица пишется читаемыми символами, а не \uXXXX-последовательностями.
        Assert.Contains("Розница", text);
        Assert.DoesNotContain("\\u", text);
        // Файл рядом с настройками, имя фиксированное.
        Assert.Equal("custom_config_types.json", Path.GetFileName(store.FilePath));
    }

    [Fact]
    public void MigrationFromSettings_CreatesFile_AndIsIdempotent()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings
        {
            CustomConfigTypes = new() { Sample("Зарплата и управление персоналом", "3.1") },
        });

        var store = CreateStore(repo);
        var first = store.Load();

        // Файл создан из старых настроек при первом обращении.
        Assert.Single(first);
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal("Зарплата и управление персоналом", first[0].Name);

        // Повторная загрузка не дублирует элементы (идемпотентность).
        Assert.Single(store.Load());

        // Старые настройки не изменяются (обратная совместимость со старыми версиями).
        var settings = repo.LoadSettings();
        Assert.Single(settings.CustomConfigTypes);

        // После миграции файл «выигрывает» у настроек: изменение настроек не влияет на список.
        repo.SaveSettings(new AppSettings
        {
            CustomConfigTypes = new() { Sample("Другая конфигурация", "1.0") },
        });
        var third = store.Load();
        Assert.Single(third);
        Assert.Equal("Зарплата и управление персоналом", third[0].Name);
    }

    [Fact]
    public void MigrationFromSettings_WhenEmpty_DoesNotCreateFile()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings()); // CustomConfigTypes пуст по умолчанию.

        var store = CreateStore(repo);
        Assert.Empty(store.Load());
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Save_ForcesIsBuiltInFalse()
    {
        var store = CreateStore();
        // Передали объект с IsBuiltIn=true (как если бы пытались сохранить предопределённый набор).
        store.Save(new[] { new OneCConfigType { Code = "BP", Name = "Бухгалтерия предприятия", IsBuiltIn = true } });

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.False(loaded[0].IsBuiltIn);
    }

    [Fact]
    public void LoadAll_ReturnsBuiltInPlusCustom()
    {
        var store = CreateStore();
        store.Save(new[] { Sample("Моя конфигурация", "1.0") });

        var all = store.LoadAll();
        Assert.True(all.Count > BuiltInConfigTypes.All.Count);
        Assert.Contains(all, c => c.Code == "C_Моя конфигурация");
        Assert.Contains(all, c => c.IsBuiltIn);
    }

    [Fact]
    public void CorruptFile_ReturnsEmpty_NoThrow()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath, "{ это не json");

        var loaded = store.Load();
        Assert.Empty(loaded);
        // Сам файл остаётся на диске — пользователь может поправить его вручную.
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void EditingOneItem_DoesNotAffectOthers()
    {
        var store = CreateStore();
        var items = new[] { Sample("Первая", "3.0"), Sample("Вторая", "2.0") };
        store.Save(items);

        // Правка ведётся на копии (как в отдельном окне правки): изменяем только первую
        // конфигурацию и сохраняем список целиком — вторая строка не должна измениться.
        var first = items[0];
        var copy = new OneCConfigType
        {
            Code = first.Code,
            Name = first.Name,
            UrlCode = first.UrlCode,
            Nick = first.Nick,
            Editions =
            {
                new OneCConfigEdition { Name = "9.9", Red = "9.9" },
            },
        };
        store.Save(new[] { copy, items[1] });

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("9.9", loaded[0].Editions[0].Red);
        Assert.Single(loaded[0].Editions);
        Assert.Equal("Вторая", loaded[1].Name);
        Assert.Equal("2.0", loaded[1].Editions[0].Red);
    }
}