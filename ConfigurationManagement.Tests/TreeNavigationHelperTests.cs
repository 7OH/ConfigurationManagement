using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой логики клавиатурной навигации по дереву баз (issue #331):
/// «следующий/предыдущий видимый узел» и «влево/вправо» на фактической иерархии
/// произвольной глубины (в тестах — 3–4 уровня вложенности групп).
/// </summary>
public sealed class TreeNavigationHelperTests
{
    // ======================= Вверх/вниз: следующий/предыдущий видимый узел =======================

    [Fact]
    public void NextVisible_MovesForwardAndClampsAtLast()
    {
        Assert.Equal(1, TreeNavigationHelper.NextVisible(11, 0));
        Assert.Equal(7, TreeNavigationHelper.NextVisible(11, 6));
        Assert.Equal(10, TreeNavigationHelper.NextVisible(11, 10)); // последний — остаёмся
        Assert.Equal(0, TreeNavigationHelper.NextVisible(11, -1));  // строки нет — первый
        Assert.Equal(-1, TreeNavigationHelper.NextVisible(0, 0));   // пустой список
    }

    [Fact]
    public void PreviousVisible_MovesBackAndClampsAtFirst()
    {
        Assert.Equal(0, TreeNavigationHelper.PreviousVisible(0));
        Assert.Equal(5, TreeNavigationHelper.PreviousVisible(6));
        Assert.Equal(0, TreeNavigationHelper.PreviousVisible(1));
    }

    [Fact]
    public void CollectVisible_Depth34_OrdersRowsInTraversalOrder()
    {
        var tree = BuildDepthFourTree();

        var visible = TreeNavigationHelper.CollectVisible(tree).Select(n => n.Id).ToList();

        // Порядок обхода: корень «Закреплённые» + его базы, затем группы глубины 3-4;
        // «d1» скрыта, потому что g1a свёрнута.
        Assert.Equal(
            new[] { "pinned", "b1", "b2", "all", "g1", "g1a", "b3", "g1b", "e1", "e2", "b4" },
            visible);
    }

    [Fact]
    public void VerticalNavigation_OnVisibleOrder_StepsOneByOne()
    {
        var tree = BuildDepthFourTree();
        var order = TreeNavigationHelper.CollectVisible(tree);

        // Вниз с первой строки — на вторую; вверх с десятой — на девятую.
        Assert.Equal(1, TreeNavigationHelper.NextVisible(order.Count, 0));
        Assert.Equal(9, TreeNavigationHelper.PreviousVisible(10));

        // Перескоков между секциями нет: порядок обхода один (закрепления — просто
        // первые строки, идущие по порядку).
        var pinnedIdx = order.FindIndex(n => n.Id == "pinned");
        var allIdx = order.FindIndex(n => n.Id == "all");
        Assert.True(pinnedIdx < allIdx);
    }

    // ======================= Влево/вправо по фактической иерархии =======================

    [Fact]
    public void Right_CollapsedGroup_ExpandsIt()
    {
        var tree = BuildDepthFourTree();
        var order = TreeNavigationHelper.CollectVisible(tree);
        var infos = TreeNavigationHelper.BuildRowInfos(order);

        var idx = order.FindIndex(n => n.Id == "g1a"); // свёрнутая группа с детьми
        var action = TreeNavigationHelper.DecideRight(infos[idx]);

        Assert.Equal(TreeNavigationHelper.LateralAction.Expand, action);
        Assert.Equal(idx, TreeNavigationHelper.TargetIndex(action, infos[idx], idx)); // строка не меняется
    }

    [Fact]
    public void Right_ExpandedGroup_MovesToFirstChild()
    {
        var tree = BuildDepthFourTree();
        var order = TreeNavigationHelper.CollectVisible(tree);
        var infos = TreeNavigationHelper.BuildRowInfos(order);

        var idx = order.FindIndex(n => n.Id == "g1"); // развёрнутая группа
        var action = TreeNavigationHelper.DecideRight(infos[idx]);

        Assert.Equal(TreeNavigationHelper.LateralAction.GoToFirstChild, action);
        Assert.Equal(idx + 1, TreeNavigationHelper.TargetIndex(action, infos[idx], idx));
        Assert.Equal("g1a", order[TreeNavigationHelper.TargetIndex(action, infos[idx], idx)].Id);
    }

    [Fact]
    public void Right_LeafBase_DoesNothing()
    {
        var tree = BuildDepthFourTree();
        var order = TreeNavigationHelper.CollectVisible(tree);
        var infos = TreeNavigationHelper.BuildRowInfos(order);

        var idx = order.FindIndex(n => n.Id == "b3"); // база без детей
        Assert.Equal(TreeNavigationHelper.LateralAction.None, TreeNavigationHelper.DecideRight(infos[idx]));
    }

    [Fact]
    public void Left_ExpandedGroup_CollapsesOnlyThisGroup_NotRootSections()
    {
        // Кейс 2 issue #331: влево на развёрнутой вложенной группе сворачивает
        // ТОЛЬКО её, а не ближайшего развёрнутого предка вплоть до «Закреплённых».
        var tree = BuildDepthFourTree();
        var order = TreeNavigationHelper.CollectVisible(tree);
        var infos = TreeNavigationHelper.BuildRowInfos(order);

        var idx = order.FindIndex(n => n.Id == "g1b"); // развёрнутая группа глубины 3
        var action = TreeNavigationHelper.DecideLeft(infos[idx]);

        Assert.Equal(TreeNavigationHelper.LateralAction.Collapse, action);
        // Действие не переносит выделение: строка остаётся на самой группе.
        Assert.Equal(idx, TreeNavigationHelper.TargetIndex(action, infos[idx], idx));
    }

    [Fact]
    public void Left_CollapsedGroup_MovesToParent()
    {
        var tree = BuildDepthFourTree();
        var order = TreeNavigationHelper.CollectVisible(tree);
        var infos = TreeNavigationHelper.BuildRowInfos(order);

        var idx = order.FindIndex(n => n.Id == "g1a"); // свёрнутая группа
        var action = TreeNavigationHelper.DecideLeft(infos[idx]);

        Assert.Equal(TreeNavigationHelper.LateralAction.GoToParent, action);
        var target = TreeNavigationHelper.TargetIndex(action, infos[idx], idx);
        Assert.Equal("g1", order[target].Id); // родитель, а не «Закреплённые»
    }

    [Fact]
    public void Left_LeafBase_MovesToParent()
    {
        var tree = BuildDepthFourTree();
        var order = TreeNavigationHelper.CollectVisible(tree);
        var infos = TreeNavigationHelper.BuildRowInfos(order);

        var idx = order.FindIndex(n => n.Id == "e1"); // база на глубине 4
        var action = TreeNavigationHelper.DecideLeft(infos[idx]);

        Assert.Equal(TreeNavigationHelper.LateralAction.GoToParent, action);
        Assert.Equal("g1b", order[TreeNavigationHelper.TargetIndex(action, infos[idx], idx)].Id);
    }

    [Fact]
    public void Left_RootCollapsedGroupWithoutParent_DoesNothing()
    {
        // Свёрнутый корень без родителя: сворачивать нечего, родителей нет —
        // влево ничего не делает (None), чтобы не «уехать» за пределы дерева.
        var tree = new List<TreeNavigationHelper.TreeNode>
        {
            new("root", isGroup: true, isExpanded: false, new[] { Leaf("x") })
        };
        var order = TreeNavigationHelper.CollectVisible(tree);
        var infos = TreeNavigationHelper.BuildRowInfos(order);

        Assert.Equal(TreeNavigationHelper.LateralAction.None, TreeNavigationHelper.DecideLeft(infos[0]));
    }

    [Fact]
    public void Left_RootExpandedSection_CollapsesIt()
    {
        var tree = BuildDepthFourTree();
        var order = TreeNavigationHelper.CollectVisible(tree);
        var infos = TreeNavigationHelper.BuildRowInfos(order);

        var idx = order.FindIndex(n => n.Id == "pinned"); // развёрнутая корневая секция
        Assert.Equal(TreeNavigationHelper.LateralAction.Collapse, TreeNavigationHelper.DecideLeft(infos[idx]));
    }

    // ======================= Помощники =======================

    private static List<TreeNavigationHelper.TreeNode> BuildDepthFourTree() =>
        new()
        {
            new TreeNavigationHelper.TreeNode("pinned", isGroup: true, isExpanded: true,
                new[] { Leaf("b1"), Leaf("b2") }),
            new TreeNavigationHelper.TreeNode("all", isGroup: true, isExpanded: true,
                new[]
                {
                    new TreeNavigationHelper.TreeNode("g1", isGroup: true, isExpanded: true,
                        new[]
                        {
                            new TreeNavigationHelper.TreeNode("g1a", isGroup: true, isExpanded: false,
                                new[] { Leaf("d1") }), // свёрнута — d1 не видна
                            Leaf("b3"),
                            new TreeNavigationHelper.TreeNode("g1b", isGroup: true, isExpanded: true,
                                new[] { Leaf("e1"), Leaf("e2") })
                        }),
                    Leaf("b4")
                })
        };

    private static TreeNavigationHelper.TreeNode Leaf(string id) =>
        new(id, isGroup: false, isExpanded: false);
}