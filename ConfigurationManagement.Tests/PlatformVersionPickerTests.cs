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

    // ======================= Хелперы =======================

    private static List<PlatformVersionInfo> Infos(params string[] displays)
        => displays.Select(d => new PlatformVersionInfo { Display = d, Path = "" }).ToList();

    private static List<PlatformVersionGroup> BuildTree(params string[] displays)
        => PlatformVersionService.BuildGroupedTree(Infos(displays));
}