namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика клавиатурной навигации по дереву баз (issue #331):
/// «следующий/предыдущий видимый узел» и «влево/вправо» по фактической иерархии
/// произвольной глубины. Без платформенных зависимостей — используется обеими
/// платформами (Windows/WPF и Linux/Avalonia).
/// <para>
/// Семантика стрелок:
/// вправо — раскрыть свёрнутую папку или перейти к первому потомку;
/// влево — свернуть развёрнутую папку (ТОЛЬКО её, не предков) или перейти к родителю;
/// вверх/вниз — предыдущий/следующий видимый узел в порядке обхода.
/// </para>
/// </summary>
public static class TreeNavigationHelper
{
    /// <summary>
    /// Индекс следующего видимого узла в порядке обхода. При current < 0 (строки
    /// нет под курсором) — первый узел; на последнем узле остаёмся на месте.
    /// </summary>
    public static int NextVisible(int rowCount, int current)
    {
        if (rowCount <= 0)
            return -1;
        if (current < 0)
            return 0;
        return Math.Min(rowCount - 1, current + 1);
    }

    /// <summary>Индекс предыдущего видимого узла; на первом узле остаёмся на месте.</summary>
    public static int PreviousVisible(int current) => current <= 0 ? 0 : current - 1;

    /// <summary>Действие стрелки влево/вправо по строке дерева.</summary>
    public enum LateralAction
    {
        /// <summary>Действия нет (строка без детей / корень без родителя).</summary>
        None,

        /// <summary>Развернуть группу (вправо по свёрнутой папке).</summary>
        Expand,

        /// <summary>Свернуть группу (влево по развёрнутой папке).</summary>
        Collapse,

        /// <summary>Перейти к родительской строке (влево по свёрнутой папке/базе).</summary>
        GoToParent,

        /// <summary>Перейти к первому потомку (вправо по развёрнутой папке).</summary>
        GoToFirstChild
    }

    /// <summary>
    /// Сведения о строке, достаточные для влево/вправо. Индексы — позиции в списке
    /// видимых строк в порядке обхода (строки развёрнутых подгрупп включены).
    /// </summary>
    /// <param name="IsGroup">Строка группы (может иметь потомков).</param>
    /// <param name="IsExpanded">Группа развёрнута.</param>
    /// <param name="HasChildren">У группы есть потомки.</param>
    /// <param name="ParentIndex">Индекс родительской строки или null для корня.</param>
    /// <param name="FirstChildIndex">Индекс первого потомка (группа развёрнута с детьми) или null.</param>
    public readonly record struct RowInfo(
        bool IsGroup,
        bool IsExpanded,
        bool HasChildren,
        int? ParentIndex,
        int? FirstChildIndex);

    /// <summary>Вправо: развернуть свёрнутую папку или перейти к первому потомку.</summary>
    public static LateralAction DecideRight(RowInfo row)
    {
        if (!row.IsGroup || !row.HasChildren)
            return LateralAction.None; // база (детей нет): вправо ничего не делает
        return row.IsExpanded ? LateralAction.GoToFirstChild : LateralAction.Expand;
    }

    /// <summary>
    /// Влево: свернуть развёрнутую папку (ТОЛЬКО её — корневые секции вроде
    /// «Закреплённых» не затрагиваются, кейс 2 issue #331) или перейти к родителю.
    /// </summary>
    public static LateralAction DecideLeft(RowInfo row)
    {
        if (row.IsGroup && row.IsExpanded && row.HasChildren)
            return LateralAction.Collapse;
        return row.ParentIndex is { } ? LateralAction.GoToParent : LateralAction.None;
    }

    /// <summary>
    /// Индекс строки-цели для действий перехода. Для Expand/Collapse/None строка
    /// не меняется — возвращается текущая.
    /// </summary>
    public static int TargetIndex(LateralAction action, RowInfo row, int currentIndex)
    {
        switch (action)
        {
            case LateralAction.GoToFirstChild:
                return row.FirstChildIndex ?? currentIndex;
            case LateralAction.GoToParent:
                return row.ParentIndex ?? currentIndex;
            default:
                return currentIndex;
        }
    }

    /// <summary>Узел дерева для юнит-тестов навигации.</summary>
    public sealed class TreeNode
    {
        public TreeNode(string id, bool isGroup, bool isExpanded, IReadOnlyList<TreeNode>? children = null, TreeNode? parent = null)
        {
            Id = id;
            IsGroup = isGroup;
            IsExpanded = isExpanded;
            Children = children ?? Array.Empty<TreeNode>();
            Parent = parent;
            foreach (var child in Children)
                child.Parent = this;
        }

        public string Id { get; }
        public bool IsGroup { get; }
        public bool IsExpanded { get; }
        public IReadOnlyList<TreeNode> Children { get; }
        public TreeNode? Parent { get; private set; }
        public bool HasChildren => Children.Count > 0;
    }

    /// <summary>
    /// Строки в порядке показа (обход в глубину, в развёрнутые группы с заходом).
    /// Используется тестами «следующий/предыдущий видимый узел».
    /// </summary>
    public static List<TreeNode> CollectVisible(IReadOnlyList<TreeNode> roots)
    {
        var result = new List<TreeNode>();
        void Walk(TreeNode node)
        {
            result.Add(node);
            if (node.IsGroup && node.IsExpanded)
                foreach (var child in node.Children)
                    Walk(child);
        }

        foreach (var root in roots)
            Walk(root);
        return result;
    }

    /// <summary>
    /// Превращает порядок обхода в <see cref="RowInfo"/> для каждой строки:
    /// вычисляет индекс родителя (ближайший развёрнутый предок) и первого потомка.
    /// Используется юнит-тестами «влево/вправо» на дереве глубины 3–4.
    /// </summary>
    public static List<RowInfo> BuildRowInfos(IReadOnlyList<TreeNode> order)
    {
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < order.Count; i++)
            indexById[order[i].Id] = i;

        var infos = new List<RowInfo>(order.Count);
        for (var i = 0; i < order.Count; i++)
        {
            var node = order[i];

            int? parentIndex = null;
            for (var p = node.Parent; p is not null; p = p.Parent)
            {
                if (indexById.TryGetValue(p.Id, out var pi))
                {
                    parentIndex = pi;
                    break;
                }
            }

            int? firstChildIndex = null;
            if (node.IsGroup && node.IsExpanded && node.HasChildren)
                firstChildIndex = i + 1; // первый потомок идёт сразу после строки группы

            infos.Add(new RowInfo(node.IsGroup, node.IsExpanded, node.HasChildren, parentIndex, firstChildIndex));
        }

        return infos;
    }
}