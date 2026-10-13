using System;
using System.IO;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты хранилища компоновки меню «Утилиты» (issue #374): сохранение в файл
/// utilities_menu_layout.json (атомарная запись), чтение с нормализацией и
/// устойчивость к битому файлу.
/// </summary>
public sealed class UtilitiesMenuLayoutStoreTests : IDisposable
{
    private readonly string _tempDir;

    public UtilitiesMenuLayoutStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_utilities_menu_tests_" + Guid.NewGuid().ToString("N"));
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

    private UtilitiesMenuLayoutStore CreateStore() => new(directoryOverride: _tempDir);

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var store = CreateStore();
        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsLayout()
    {
        var store = CreateStore();
        var layout = UtilitiesMenuLayouts.Default();
        store.Save(layout);

        Assert.True(File.Exists(store.FilePath));
        var restored = store.Load();
        Assert.NotNull(restored);
        Assert.Equal(layout.Root.Count, restored!.Root.Count);
        Assert.Equal("CommandPalette", restored.Root[0].ItemId);
        Assert.Equal("group", restored.Root[7].Kind);
        Assert.Equal(UtilitiesMenuCatalog.GroupLockOptions, restored.Root[7].GroupId);
        Assert.Equal(3, restored.Root[7].Children.Count);
    }

    [Fact]
    public void Save_CreatesReadableJson_WithUtf8Cyrillic()
    {
        var store = CreateStore();
        var layout = new UtilitiesMenuLayout
        {
            SchemaVersion = 1,
            Root = new System.Collections.Generic.List<UtilitiesMenuNode>
            {
                UtilitiesMenuLayouts.Item("CommandPalette"),
                UtilitiesMenuLayouts.Group(UtilitiesMenuCatalog.GroupLockOptions, new System.Collections.Generic.List<UtilitiesMenuNode>
                {
                    UtilitiesMenuLayouts.Item("UnlockPrivateBases")
                })
            }
        };
        store.Save(layout);

        var text = File.ReadAllText(store.FilePath);
        // Кириллица записывается читаемыми символами, а не \uXXXX.
        Assert.DoesNotContain("\\u", text);
        // Ключи свойств в JSON — имена свойств модели.
        Assert.Contains("ItemId", text);
        Assert.Contains("Root", text);
    }

    [Fact]
    public void Load_BrokenFile_ReturnsNull_AndDoesNotThrow()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath, "{ this is not valid json");

        var result = store.Load();
        Assert.Null(result);
        // Битый файл на диске не трогаем.
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Load_FileWithUnknownItem_DropsUnknownNode()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 1,
              "Root": [
                { "Kind": "item", "ItemId": "CommandPalette", "Visible": true },
                { "Kind": "item", "ItemId": "UnknownItemId", "Visible": true },
                { "Kind": "group", "GroupId": "LockOptions", "Visible": true, "Children": [] }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        Assert.Equal(2, result!.Root.Count);
        Assert.Equal("CommandPalette", result.Root[0].ItemId);
        Assert.Equal("group", result.Root[1].Kind);
    }

    [Fact]
    public void Load_FileWithNullCollections_Normalizes()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath,
            """
            {
              "SchemaVersion": 1,
              "Root": [
                { "Kind": "group", "GroupId": "Operations", "Children": null }
              ]
            }
            """);

        var result = store.Load();
        Assert.NotNull(result);
        Assert.Single(result!.Root);
        Assert.NotNull(result.Root[0].Children);
        Assert.Empty(result.Root[0].Children!);
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var store = CreateStore();
        var first = new UtilitiesMenuLayout();
        first.Root.Add(UtilitiesMenuLayouts.Item("CommandPalette"));
        store.Save(first);

        var second = new UtilitiesMenuLayout();
        second.Root.Add(UtilitiesMenuLayouts.Item("ActualReleases"));
        store.Save(second);

        var restored = store.Load();
        Assert.NotNull(restored);
        Assert.Single(restored!.Root);
        Assert.Equal("ActualReleases", restored.Root[0].ItemId);
    }
}