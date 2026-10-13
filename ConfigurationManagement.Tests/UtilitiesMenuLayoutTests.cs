using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты модели компоновки меню «Утилиты» (issue #374): стандартная компоновка
/// воспроизводит текущее статическое меню (состав, порядок, группы, разделители),
/// а JSON-сериализация/десериализация сохраняет структуру.
/// </summary>
public sealed class UtilitiesMenuLayoutTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Стандартная компоновка идентична текущему статическому меню «Утилиты» (обратная совместимость).</summary>
    [Fact]
    public void DefaultLayout_MatchesCurrentStaticMenu()
    {
        var layout = UtilitiesMenuLayouts.Default();

        // Корень: 11 узлов — 6 пунктов верхнего уровня + «Блокировка приложения»,
        // подменю «Опции блокировки», «Информация», «Операции» и «Проверить обновления».
        Assert.Equal(11, layout.Root.Count);

        // ---- Верхний уровень ----
        AssertItem(layout.Root[0], "CommandPalette", separatorBefore: false);
        AssertItem(layout.Root[1], "ActualReleases", separatorBefore: true);
        AssertItem(layout.Root[2], "PlatformUpdate", separatorBefore: false);
        AssertItem(layout.Root[3], "PlatformDownload", separatorBefore: false);
        AssertItem(layout.Root[4], "ServerMonitor", separatorBefore: false);
        AssertItem(layout.Root[5], "ServerConsoleExternal", separatorBefore: false);

        // «Блокировка приложения» на верхнем уровне, перед подменю (issue #294).
        AssertItem(layout.Root[6], "AppLock", separatorBefore: true);

        // ---- Подменю «Опции блокировки» ----
        AssertGroup(layout.Root[7], UtilitiesMenuCatalog.GroupLockOptions, separatorBefore: false);
        var lockChildren = layout.Root[7].Children;
        Assert.Equal(3, lockChildren.Count);
        AssertItem(lockChildren[0], "UnlockPrivateBases", separatorBefore: false);
        AssertItem(lockChildren[1], "ChangeAppLock", separatorBefore: false);
        AssertItem(lockChildren[2], "RunBackupScenario", separatorBefore: true);

        // ---- Подменю «Информация» ----
        AssertGroup(layout.Root[8], UtilitiesMenuCatalog.GroupInformation, separatorBefore: true);
        var infoChildren = layout.Root[8].Children;
        Assert.Equal(7, infoChildren.Count);
        AssertItem(infoChildren[0], "MaintenanceCenter", separatorBefore: false);
        AssertItem(infoChildren[1], "ConfigTypesEdit", separatorBefore: true);
        AssertItem(infoChildren[2], "ItsAccounts", separatorBefore: false);
        AssertItem(infoChildren[3], "ScheduledTasks", separatorBefore: true);
        AssertItem(infoChildren[4], "BackupScenarios", separatorBefore: false);
        AssertItem(infoChildren[5], "ScriptsSettings", separatorBefore: true);
        AssertItem(infoChildren[6], "UsageStatistics", separatorBefore: false);

        // ---- Подменю «Операции» ----
        AssertGroup(layout.Root[9], UtilitiesMenuCatalog.GroupOperations, separatorBefore: true);
        var opsChildren = layout.Root[9].Children;
        Assert.Equal(15, opsChildren.Count);
        AssertItem(opsChildren[0], "ConfigDiff", separatorBefore: false);
        AssertItem(opsChildren[1], "MetadataExplorer", separatorBefore: false);
        AssertItem(opsChildren[2], "RepositoryBatchUpdate", separatorBefore: true);
        AssertItem(opsChildren[3], "RepositoryBrowser", separatorBefore: false);
        AssertItem(opsChildren[4], "ExportCsv", separatorBefore: true);
        AssertItem(opsChildren[5], "ExportHtml", separatorBefore: false);
        AssertItem(opsChildren[6], "ExportJson", separatorBefore: false);
        AssertItem(opsChildren[7], "ImportJson", separatorBefore: false);
        AssertItem(opsChildren[8], "ClusterImport", separatorBefore: false);
        AssertItem(opsChildren[9], "ProcessInspector", separatorBefore: true);
        AssertItem(opsChildren[10], "KillOneCProcesses", separatorBefore: false);
        AssertItem(opsChildren[11], "ConnectionReplace", separatorBefore: true);
        AssertItem(opsChildren[12], "UndoConnectionReplace", separatorBefore: false);
        // Пункт «Блокировка сеансов ИБ» есть только в Avalonia/Linux: в стандартной
        // компоновке он присутствует с разделителем (как в статическом меню Linux),
        // а WPF-построитель отфильтрует его по LinuxOnly.
        AssertItem(opsChildren[13], "SessionLock", separatorBefore: true);
        AssertItem(opsChildren[14], "RemoveMissingFileBases", separatorBefore: true);

        // ---- «Проверить обновления» в конце, с разделителем ----
        AssertItem(layout.Root[10], "CheckForUpdates", separatorBefore: true);
    }

    /// <summary>Каталог содержит все пункты стандартной компоновки (ни один ItemId не теряется).</summary>
    [Fact]
    public void Catalog_ContainsAllDefaultLayoutItems()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var ids = new HashSet<string>();
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);
        foreach (var id in ids)
        {
            Assert.NotNull(UtilitiesMenuCatalog.Find(id));
        }
        // В каталоге больше пунктов, чем в стандартной компоновке? Нет: компоновка
        // включает все пункты каталога, кроме пометок «новинка» (сейчас их нет).
        Assert.True(ids.Count <= UtilitiesMenuCatalog.All.Count);
    }

    /// <summary>JSON-сериализация и обратная десериализация сохраняют структуру компоновки.</summary>
    [Fact]
    public void Json_RoundTrip_PreservesStructure()
    {
        var layout = UtilitiesMenuLayouts.Default();
        var json = JsonSerializer.Serialize(layout, JsonOptions);
        var restored = JsonSerializer.Deserialize<UtilitiesMenuLayout>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(layout.SchemaVersion, restored!.SchemaVersion);
        Assert.Equal(layout.Root.Count, restored.Root.Count);
        for (var i = 0; i < layout.Root.Count; i++)
        {
            var original = layout.Root[i];
            var copy = restored.Root[i];
            Assert.Equal(original.Kind, copy.Kind);
            Assert.Equal(original.ItemId, copy.ItemId);
            Assert.Equal(original.GroupId, copy.GroupId);
            Assert.Equal(original.SeparatorBefore, copy.SeparatorBefore);
            Assert.Equal(original.Visible, copy.Visible);
            Assert.Equal(original.Children.Count, copy.Children.Count);
        }
    }

    /// <summary>JSON читается с учётом регистра имён свойств (ручная правка файла).</summary>
    [Fact]
    public void Json_Deserialize_IsCaseInsensitive()
    {
        const string json = """
            {
              "SchemaVersion": 1,
              "Root": [
                {
                  "Kind": "item",
                  "ItemId": "CommandPalette",
                  "Visible": true
                },
                {
                  "Kind": "group",
                  "GroupId": "LockOptions",
                  "Visible": true,
                  "Children": [
                    { "Kind": "item", "ItemId": "UnlockPrivateBases", "Visible": true }
                  ]
                }
              ]
            }
            """;
        var layout = JsonSerializer.Deserialize<UtilitiesMenuLayout>(json, JsonOptions);
        Assert.NotNull(layout);
        Assert.Equal(2, layout!.Root.Count);
        Assert.Equal("CommandPalette", layout.Root[0].ItemId);
        Assert.Equal("group", layout.Root[1].Kind);
        Assert.Single(layout.Root[1].Children);
        Assert.Equal("UnlockPrivateBases", layout.Root[1].Children[0].ItemId);
    }

    private static void AssertItem(UtilitiesMenuNode node, string itemId, bool separatorBefore)
    {
        Assert.NotNull(node);
        Assert.Equal("item", node.Kind);
        Assert.Equal(itemId, node.ItemId);
        Assert.Equal(separatorBefore, node.SeparatorBefore);
        Assert.True(node.Visible);
    }

    private static void AssertGroup(UtilitiesMenuNode node, string groupId, bool separatorBefore)
    {
        Assert.NotNull(node);
        Assert.Equal("group", node.Kind);
        Assert.Equal(groupId, node.GroupId);
        Assert.Equal(separatorBefore, node.SeparatorBefore);
        Assert.True(node.Visible);
    }
}