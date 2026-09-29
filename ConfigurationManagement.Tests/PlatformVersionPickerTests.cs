using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой логики окна выбора версии платформы (<see cref="PlatformVersionService"/>,
/// issue #304): фильтрация по разрядности и поиск узла дерева с учётом разрядности
/// и fallback на доступного предка при отфильтрованной версии.
/// </summary>
public sealed class PlatformVersionPickerTests
{
    // ======================= Фильтрация по разрядности =======================

    [Fact]
    public void FilterByArchitecture_WithoutSuffix_KeptInBothFilters()
    {
        var infos = Infos("8.5.1", "8.5.1 (64)", "8.5.1 (32)");

        var x64 = PlatformVersionService.FilterByArchitecture(infos, "x64");
        var x32 = PlatformVersionService.FilterByArchitecture(infos, "x32");

        // Вариант без явной разрядности показывается в обоих фильтрах (#304/#251):
        // ParseVariant считает его x32, но это лишь значение по умолчанию.
        Assert.Contains(x64, i => i.Display == "8.5.1");
        Assert.Contains(x64, i => i.Display == "8.5.1 (64)");
        Assert.DoesNotContain(x64, i => i.Display == "8.5.1 (32)");

        Assert.Contains(x32, i => i.Display == "8.5.1");
        Assert.Contains(x32, i => i.Display == "8.5.1 (32)");
        Assert.DoesNotContain(x32, i => i.Display == "8.5.1 (64)");
    }

    [Fact]
    public void FilterByArchitecture_All_ReturnsEverything()
    {
        var infos = Infos("8.5.1", "8.5.1 (64)", "8.5.1 (32)");
        var all = PlatformVersionService.FilterByArchitecture(infos, "all");
        Assert.Equal(3, all.Count);
    }

    // ======================= Поиск узла (FindBestNode) =======================

    [Fact]
    public void FindBestNode_PartialVersion_ReturnsBuildGroupFolder()
    {
        // «8.5.4» и «8.5.1» — частичные версии (по 3 сегмента): им соответствует
        // папка группы сборок, а не максимальная сборка в ней (#251).
        var tree = BuildTree("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (64)", "8.5.1 (32)");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5.1", node.Name);
        Assert.Equal(PlatformNodeKind.BuildGroup, node.Kind);
    }

    [Fact]
    public void FindBestNode_BuildGroupFiltered_FallsBackToLine()
    {
        // 8.5.1 существует только как x32, а фильтр x64 её исключил — осталась линия «8.5»
        // с группой «8.5.4». Выделение должно осмысленно перейти на линию (#304).
        var filtered = PlatformVersionService.FilterByArchitecture(
            Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)"), "x64");
        var tree = PlatformVersionService.BuildGroupedTree(filtered);

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5", node.Name);
        Assert.Equal(PlatformNodeKind.Line, node.Kind);
    }

    [Fact]
    public void FindBestNode_EmptyTree_ReturnsNull()
    {
        // Все версии 8.5.1 — x32; фильтр x64 исключает их полностью → дерево пусто.
        var empty = PlatformVersionService.BuildGroupedTree(new List<PlatformVersionInfo>());

        var node = PlatformVersionService.FindBestNode(empty, "8.5.1 (32)");

        Assert.Null(node);
    }

    [Fact]
    public void FindBestNode_FullVersionWithArch_ReturnsExactLeaf()
    {
        // Полная версия с явной разрядностью должна выбрать именно x64-лист,
        // а не первый попавшийся лист той же версии (x32 идёт вторым) (#304).
        var tree = BuildTree("8.5.1.1000 (64)", "8.5.1.1000 (32)");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1.1000 (64)");

        Assert.NotNull(node);
        Assert.True(node!.IsLeaf);
        Assert.Equal("8.5.1.1000 (64)", node.Variant);
    }

    [Fact]
    public void FindBestNode_FullVersionWithoutArch_ReturnsAnyLeafOfThatVersion()
    {
        // Версия без суффикса — любой лист той же версии (#251).
        var tree = BuildTree("8.5.1.1000 (64)", "8.5.1.1000 (32)");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1.1000");

        Assert.NotNull(node);
        Assert.True(node!.IsLeaf);
        Assert.StartsWith("8.5.1.1000", node.Variant);
    }

    [Fact]
    public void FindBestNode_FullVersionArchFiltered_FallsBackToSameVersionOtherArch()
    {
        // В дереве x64 есть только 8.5.1.1000 (64), ищем 8.5.1.1000 (32) —
        // осмысленный fallback на доступную сборку той же версии (#304).
        var tree = BuildTree("8.5.1.1000 (64)");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1.1000 (32)");

        Assert.NotNull(node);
        Assert.True(node!.IsLeaf);
        Assert.Equal("8.5.1.1000 (64)", node.Variant);
    }

    // ======================= Совпадение варианта (MatchesCurrent) =======================

    [Fact]
    public void MatchesCurrent_ExplicitArch_MustMatchSameArch()
    {
        Assert.True(PlatformVersionService.MatchesCurrent("8.5.1 (64)", "8.5.1 (64)"));
        Assert.True(PlatformVersionService.MatchesCurrent("8.5.1 (32)", "8.5.1 (32)"));
        // Разрядность теперь учитывается: лист x64 не совпадает с искомой x32 (#304).
        Assert.False(PlatformVersionService.MatchesCurrent("8.5.1 (64)", "8.5.1 (32)"));
        Assert.False(PlatformVersionService.MatchesCurrent("8.5.1 (32)", "8.5.1 (64)"));
    }

    [Fact]
    public void MatchesCurrent_NoArchInRequest_MatchesAnyLeaf()
    {
        Assert.True(PlatformVersionService.MatchesCurrent("8.5.1 (64)", "8.5.1"));
        Assert.True(PlatformVersionService.MatchesCurrent("8.5.1 (32)", "8.5.1"));
    }

    [Fact]
    public void MatchesCurrent_DifferentVersion_ReturnsFalse()
    {
        Assert.False(PlatformVersionService.MatchesCurrent("8.5.4 (64)", "8.5.1 (64)"));
    }

    // ============ Сценарий #304: 8.5.4 → 8.5.1 → фильтр x64 ============

    [Fact]
    public void BuildGroupedTree_WithArchFilter_KeepsFolderWhenLineHasVisibleVariant()
    {
        // У 8.5.1 только x32-сборка, у 8.5.4 — x32 и x64. При фильтре x64 папка «8.5.1»
        // должна остаться в дереве (линия 8.5 видима), хотя её листья скрыты (#304).
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)"), "x64");

        var line = Assert.Single(tree);
        Assert.Equal("8.5", line.Name);
        Assert.Equal(PlatformNodeKind.Line, line.Kind);

        var group51 = Assert.Single(line.Children, g => g.Name == "8.5.1");
        Assert.Equal(PlatformNodeKind.BuildGroup, group51.Kind);
        Assert.Empty(group51.Children); // все листья 8.5.1 скрыты фильтром

        var group54 = Assert.Single(line.Children, g => g.Name == "8.5.4");
        Assert.Single(group54.Children); // остался только лист «8.5.4 (64)»
        Assert.Equal("8.5.4 (64)", group54.Children[0].Variant);
    }

    [Fact]
    public void BuildGroupedTree_WithArchFilter_HidesOtherArchLeaves()
    {
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.1 (64)", "8.5.1 (32)"), "x64");

        var line = Assert.Single(tree);
        var group = Assert.Single(line.Children, g => g.Name == "8.5.1");
        var leaf = Assert.Single(group.Children);
        Assert.Equal("8.5.1 (64)", leaf.Variant);
    }

    [Fact]
    public void BuildGroupedTree_WithArchFilter_OmitsFullyFilteredLine()
    {
        // Вся линия 8.5 — только x32: при фильтре x64 её не показываем вовсе,
        // fallback бессмыслен (issue #304).
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.1 (32)", "8.5.4 (32)"), "x64");

        Assert.Empty(tree);
    }

    [Fact]
    public void FindBestNode_PartialVersionInFilteredTree_ReturnsBuildGroupFolder()
    {
        // Главный сценарий: текущая 8.5.4, выбрана папка «8.5.1», переключаем фильтр
        // на x64 — выделение должно остаться на папке 8.5.1, а НЕ уходить на линию 8.5.
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)"), "x64");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5.1", node.Name);
        Assert.Equal(PlatformNodeKind.BuildGroup, node.Kind);
    }

    [Fact]
    public void FindBestNode_LeafFullVersionFiltered_FallsBackToBuildGroupFolder()
    {
        // Выбран лист «8.5.1.1000 (32)»; у 8.5.1 нет x64-сборок — при фильтре x64
        // выделение переходит на папку группы сборок 8.5.1, а не на линию 8.5 (#304).
        var tree = PlatformVersionService.BuildGroupedTree(
            Infos("8.5.1.1000 (32)", "8.5.4 (64)"), "x64");

        var node = PlatformVersionService.FindBestNode(tree, "8.5.1.1000 (32)");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5.1", node.Name);
        Assert.Equal(PlatformNodeKind.BuildGroup, node.Kind);
    }

    [Fact]
    public void Scenario_854To851SwitchX64_KeepsSelectionOn851()
    {
        // Полный сценарий из комментария @7OH: «Была 8.5.4, выбираю 8.5.1,
        // переключаю на х64 и выделяет 8.5». После исправления выделение остаётся
        // на папке 8.5.1.
        var all = Infos("8.5.4 (64)", "8.5.4 (32)", "8.5.1 (32)");

        // Окно открыто с текущей версией 8.5.4 (64); пользователь выбрал папку 8.5.1,
        // затем переключил фильтр разрядности на x64 — RefreshTree строит дерево
        // из ПОЛНОГО списка версий с активным фильтром и восстанавливает выбор.
        var tree = PlatformVersionService.BuildGroupedTree(all, "x64");
        var node = PlatformVersionService.FindBestNode(tree, "8.5.1");

        Assert.NotNull(node);
        Assert.False(node!.IsLeaf);
        Assert.Equal("8.5.1", node.Name);
        Assert.Equal(PlatformNodeKind.BuildGroup, node.Kind);
        Assert.DoesNotContain(tree, n => n.Name == "8.5" && n.Kind == PlatformNodeKind.BuildGroup);
    }

    // ======================= Хелперы =======================

    private static List<PlatformVersionInfo> Infos(params string[] displays)
        => displays.Select(d => new PlatformVersionInfo { Display = d, Path = "" }).ToList();

    private static List<PlatformVersionGroup> BuildTree(params string[] displays)
        => PlatformVersionService.BuildGroupedTree(Infos(displays));
}