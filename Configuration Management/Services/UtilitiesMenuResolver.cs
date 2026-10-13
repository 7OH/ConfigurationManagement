using System.Collections.Generic;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Резолвер компоновки меню «Утилиты» (issue #374): выбирает эффективную компоновку
/// по настройкам (правка включена/выключена) и собирает «новинки программы» — пункты
/// каталога, добавленные в новых версиях и отсутствующие в сохранённой компоновке.
/// Платформенно-нейтральная логика, общая для построителей WPF и Avalonia.
/// </summary>
public static class UtilitiesMenuResolver
{
    /// <summary>
    /// Возвращает компоновку для построения меню:
    /// <list type="bullet">
    /// <item>правка выключена (по умолчанию) — стандартная компоновка (обратная совместимость);</item>
    /// <item>правка включена и файл компоновки есть — пользовательская компоновка;</item>
    /// <item>правка включена, файла нет — стандартная компоновка (файл создаст редактор).</item>
    /// </list>
    /// </summary>
    public static UtilitiesMenuLayout Resolve(AppSettings? settings, IUtilitiesMenuLayoutStore? store)
    {
        if (settings is null || !settings.UtilitiesMenuEditingEnabled)
            return UtilitiesMenuLayouts.Default();
        var layout = store?.Load();
        return layout ?? UtilitiesMenuLayouts.Default();
    }

    /// <summary>
    /// Собирает «новинки программы»: пункты каталога с <see cref="UtilitiesMenuItem.IsNew"/>,
    /// отсутствующие в компоновке (по <c>ItemId</c>). Такие пункты автоматически попадают
    /// в подменю «Новинки программы» в конце меню, пока пользователь не разместит их
    /// в редакторе структуры.
    /// </summary>
    public static IReadOnlyList<UtilitiesMenuItem> CollectNewItems(UtilitiesMenuLayout layout)
    {
        var present = new HashSet<string>();
        CollectItemIds(layout?.Root, present);
        var result = new List<UtilitiesMenuItem>();
        foreach (var item in UtilitiesMenuCatalog.All)
        {
            if (item.IsNew && !present.Contains(item.Id))
                result.Add(item);
        }
        return result;
    }

    /// <summary>Собирает все ItemId узлов дерева (включая вложенные группы).</summary>
    public static void CollectItemIds(IEnumerable<UtilitiesMenuNode>? nodes, HashSet<string> target)
    {
        if (nodes is null || target is null)
            return;
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "item", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(node.ItemId))
            {
                target.Add(node.ItemId!);
            }
            CollectItemIds(node.Children, target);
        }
    }
}