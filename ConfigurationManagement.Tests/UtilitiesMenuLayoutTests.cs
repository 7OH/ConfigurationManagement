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

    /// <summary>Стандартная компоновка идентична текущему статическому меню «Утилиты» (обратная совместимость).
    /// Разделители — самостоятельные узлы <c>Kind == "separator"</c> (issue #374).</summary>
    [Fact]
    public void DefaultLayout_MatchesCurrentStaticMenu()
    {
        var layout = UtilitiesMenuLayouts.Default();

        // Корень: 16 узлов — 11 пунктов/групп + 5 узлов-разделителей (issue #374):
        // перед «Актуальные релизы», «Блокировка приложения», «Информация», «Операции», «Проверить обновления».
        Assert.Equal(16, layout.Root.Count);

        // ---- Верхний уровень ----
        AssertItem(layout.Root[0], "CommandPalette");
        AssertSeparator(layout.Root[1]);
        AssertItem(layout.Root[2], "ActualReleases");
        AssertItem(layout.Root[3], "PlatformUpdate");
        AssertItem(layout.Root[4], "PlatformDownload");
        AssertItem(layout.Root[5], "ServerMonitor");
        AssertItem(layout.Root[6], "ServerConsoleExternal");
        AssertSeparator(layout.Root[7]);

        // «Блокировка приложения» на верхнем уровне, перед подменю (issue #294).
        AssertItem(layout.Root[8], "AppLock");

        // ---- Подменю «Опции блокировки» ----
        AssertGroup(layout.Root[9], UtilitiesMenuCatalog.GroupLockOptions);
        var lockChildren = layout.Root[9].Children;
        Assert.Equal(4, lockChildren.Count);
        AssertItem(lockChildren[0], "UnlockPrivateBases");
        AssertItem(lockChildren[1], "ChangeAppLock");
        AssertSeparator(lockChildren[2]);
        AssertItem(lockChildren[3], "RunBackupScenario");

        // ---- Подменю «Информация» ----
        AssertSeparator(layout.Root[10]);
        AssertGroup(layout.Root[11], UtilitiesMenuCatalog.GroupInformation);
        var infoChildren = layout.Root[11].Children;
        Assert.Equal(10, infoChildren.Count);
        AssertItem(infoChildren[0], "MaintenanceCenter");
        AssertSeparator(infoChildren[1]);
        AssertItem(infoChildren[2], "ConfigTypesEdit");
        AssertItem(infoChildren[3], "ItsAccounts");
        AssertSeparator(infoChildren[4]);
        AssertItem(infoChildren[5], "ScheduledTasks");
        AssertItem(infoChildren[6], "BackupScenarios");
        AssertSeparator(infoChildren[7]);
        AssertItem(infoChildren[8], "ScriptsSettings");
        AssertItem(infoChildren[9], "UsageStatistics");

        // ---- Подменю «Операции» ----
        AssertSeparator(layout.Root[12]);
        AssertGroup(layout.Root[13], UtilitiesMenuCatalog.GroupOperations);
        var opsChildren = layout.Root[13].Children;
        Assert.Equal(21, opsChildren.Count);
        AssertItem(opsChildren[0], "ConfigDiff");
        AssertItem(opsChildren[1], "MetadataExplorer");
        AssertSeparator(opsChildren[2]);
        AssertItem(opsChildren[3], "RepositoryBatchUpdate");
        AssertItem(opsChildren[4], "RepositoryBrowser");
        AssertSeparator(opsChildren[5]);
        AssertItem(opsChildren[6], "ExportCsv");
        AssertItem(opsChildren[7], "ExportHtml");
        AssertItem(opsChildren[8], "ExportJson");
        AssertItem(opsChildren[9], "ImportJson");
        AssertItem(opsChildren[10], "ClusterImport");
        AssertSeparator(opsChildren[11]);
        AssertItem(opsChildren[12], "ProcessInspector");
        AssertItem(opsChildren[13], "KillOneCProcesses");
        AssertSeparator(opsChildren[14]);
        AssertItem(opsChildren[15], "ConnectionReplace");
        AssertItem(opsChildren[16], "UndoConnectionReplace");
        // Пункт «Блокировка сеансов ИБ» есть только в Avalonia/Linux: в стандартной
        // компоновке он присутствует с разделителем (как в статическом меню Linux),
        // а WPF-построитель отфильтрует его по LinuxOnly.
        AssertSeparator(opsChildren[17]);
        AssertItem(opsChildren[18], "SessionLock");
        AssertSeparator(opsChildren[19]);
        AssertItem(opsChildren[20], "RemoveMissingFileBases");

        // ---- «Проверить обновления» в конце, с разделителем ----
        AssertSeparator(layout.Root[14]);
        AssertItem(layout.Root[15], "CheckForUpdates");
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

    private static void AssertItem(UtilitiesMenuNode node, string itemId)
    {
        Assert.NotNull(node);
        Assert.Equal("item", node.Kind);
        Assert.Equal(itemId, node.ItemId);
        Assert.False(node.SeparatorBefore);
        Assert.True(node.Visible);
    }

    private static void AssertGroup(UtilitiesMenuNode node, string groupId)
    {
        Assert.NotNull(node);
        Assert.Equal("group", node.Kind);
        Assert.Equal(groupId, node.GroupId);
        Assert.False(node.SeparatorBefore);
        Assert.True(node.Visible);
    }

    private static void AssertSeparator(UtilitiesMenuNode node)
    {
        Assert.NotNull(node);
        Assert.Equal("separator", node.Kind);
        Assert.Null(node.ItemId);
        Assert.Null(node.GroupId);
        Assert.False(node.SeparatorBefore);
        Assert.True(node.Visible);
    }
}