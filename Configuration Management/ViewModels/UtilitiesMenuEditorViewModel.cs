using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Параметры создания/правки группы в редакторе меню «Утилиты» (issue #374):
/// имя группы и ключ иконки каталога (пустой — без иконки).</summary>
public sealed class UtilitiesGroupEditParams
{
    public UtilitiesGroupEditParams(string title, string? iconKey)
    {
        Title = title;
        IconKey = iconKey;
    }

    /// <summary>Имя группы.</summary>
    public string Title { get; }

    /// <summary>Ключ иконки каталога (пустой — без иконки).</summary>
    public string? IconKey { get; }
}

/// <summary>
/// Редактор структуры меню «Утилиты» (issue #374): дерево компоновки с перемещением
/// пунктов (вверх/вниз/в группу), отключением (видимость), созданием групп, назначением
/// иконок из каталога программы и из внешнего файла, сбросом к стандартной компоновке
/// и сохранением/загрузкой компоновки в файл <c>utilities_menu_layout.json</c>.
/// Чистый .NET — используется обеими платформами (WPF и Avalonia).
/// </summary>
public class UtilitiesMenuEditorViewModel : ViewModelBase
{
    private readonly IUtilitiesMenuLayoutStore? _store;
    private UtilitiesMenuLayout _layout = UtilitiesMenuLayouts.Default();
    private UtilitiesMenuNodeItem? _selectedItem;
    private UtilitiesMenuNodeItem? _selectedGroup;

    /// <param name="store">Хранилище компоновки (для загрузки/сохранения); может быть null.</param>
    public UtilitiesMenuEditorViewModel(IUtilitiesMenuLayoutStore? store)
    {
        _store = store;
        var loaded = store?.Load();
        _layout = loaded ?? UtilitiesMenuLayouts.Default();
        Roots = BuildTree(_layout.Root, null);

        MoveUpCommand = new RelayCommand(_ => MoveSelected(-1), _ => CanMoveSelected(-1));
        MoveDownCommand = new RelayCommand(_ => MoveSelected(1), _ => CanMoveSelected(1));
        MoveToGroupCommand = new RelayCommand(_ => MoveSelectedToGroup(), _ => CanMoveToGroup());
        AddGroupCommand = new RelayCommand(p => AddGroup(p as UtilitiesGroupEditParams));
        EditGroupCommand = new RelayCommand(p => EditGroup(p as UtilitiesGroupEditParams), _ => CanEditGroup);
        DeleteGroupCommand = new RelayCommand(p => DeleteGroup(p is bool b && b), _ => CanDeleteGroup);
        SetCatalogIconCommand = new RelayCommand(p => ApplyCatalogIcon(p as string));
        SetExternalIconCommand = new RelayCommand(p => ApplyExternalIcon(p as string));
        ClearIconCommand = new RelayCommand(_ => ClearSelectedIcon(), _ => CanClearIcon());
        ResetDefaultCommand = new RelayCommand(_ => ResetToDefault());
        SaveCommand = new RelayCommand(_ => Save());
        LoadCommand = new RelayCommand(_ => Load());
        CloseCommand = new RelayCommand(_ => Accepted = true);
        ToggleVisibleCommand = new RelayCommand(_ => { /* видимость переключается чекбоксом напрямую */ });
    }

    /// <summary>Корневые узлы дерева компоновки.</summary>
    public ObservableCollection<UtilitiesMenuNodeItem> Roots { get; }

    /// <summary>Выбранный узел дерева (объект операций перемещения/иконки).</summary>
    public UtilitiesMenuNodeItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetPropertyWithRelated(ref _selectedItem, value, nameof(SelectedItem), nameof(HasSelection), nameof(CanMoveSelected), nameof(CanMoveToGroup), nameof(CanClearIcon), nameof(CanEditGroup), nameof(CanDeleteGroup)))
                RaiseCommandsCanExecuteChanged();
        }
    }

    /// <summary>Выбранная группа-назначение (для «В группу»).</summary>
    public UtilitiesMenuNodeItem? SelectedGroup
    {
        get => _selectedGroup;
        set => SetPropertyWithRelated(ref _selectedGroup, value, nameof(SelectedGroup), nameof(CanMoveToGroup));
    }

    /// <summary>Выбран ли узел (для UI-включения кнопок).</summary>
    public bool HasSelection => SelectedItem is not null;

    /// <summary>Можно ли править выбранный узел как группу (кнопка «Правка группы»).</summary>
    public bool CanEditGroup => IsGroup(SelectedItem);

    /// <summary>Можно ли удалить выбранную группу (кнопка «Удалить группу»).</summary>
    public bool CanDeleteGroup => IsGroup(SelectedItem);

    /// <summary>Выбранный ключ иконки каталога (ComboBox редактора).</summary>
    public string SelectedIconKey
    {
        get => _selectedIconKey;
        set => SetProperty(ref _selectedIconKey, value);
    }

    private string _selectedIconKey = string.Empty;

    /// <summary>Диалог завершён нажатием «Готово».</summary>
    public bool Accepted { get; private set; }

    /// <summary>Доступные иконки из каталога для назначения (ключ → подпись).</summary>
    public IReadOnlyList<UtilitiesIconOption> CatalogIcons { get; } = BuildCatalogIcons();

    /// <summary>Все группы дерева (для выпадающего списка «Поместить в группу»).</summary>
    public ObservableCollection<UtilitiesMenuNodeItem> Groups { get; } = new();

    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand MoveToGroupCommand { get; }
    public RelayCommand AddGroupCommand { get; }
    public RelayCommand EditGroupCommand { get; }
    public RelayCommand DeleteGroupCommand { get; }
    public RelayCommand SetCatalogIconCommand { get; }
    public RelayCommand SetExternalIconCommand { get; }
    public RelayCommand ClearIconCommand { get; }
    public RelayCommand ResetDefaultCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand ToggleVisibleCommand { get; }

    /// <summary>Сохранённая компоновка (для построителя меню после закрытия редактора).</summary>
    public UtilitiesMenuLayout Layout => _layout;

    /// <summary>Восстанавливает дерево из компоновки.</summary>
    private ObservableCollection<UtilitiesMenuNodeItem> BuildTree(IEnumerable<UtilitiesMenuNode> nodes, UtilitiesMenuNodeItem? parent)
    {
        var result = new ObservableCollection<UtilitiesMenuNodeItem>();
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            var item = new UtilitiesMenuNodeItem(node, parent);
            foreach (var child in BuildTree(node.Children, item))
                item.Children.Add(child);
            result.Add(item);
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
                Groups.Add(item);
            CollectGroups(item);
        }
        return result;
    }

    private void CollectGroups(UtilitiesMenuNodeItem item)
    {
        foreach (var child in item.Children)
        {
            if (string.Equals(child.Node.Kind, "group", StringComparison.Ordinal))
                Groups.Add(child);
            CollectGroups(child);
        }
    }

    private bool CanMoveSelected(int delta)
    {
        var item = SelectedItem;
        if (item is null)
            return false;
        var siblings = item.Parent is null ? Roots : item.Parent.Children;
        var index = siblings.IndexOf(item);
        var target = index + delta;
        return target >= 0 && target < siblings.Count;
    }

    private void MoveSelected(int delta)
    {
        var item = SelectedItem;
        if (item is null)
            return;
        var siblings = item.Parent is null ? Roots : item.Parent.Children;
        var index = siblings.IndexOf(item);
        var target = index + delta;
        if (target < 0 || target >= siblings.Count)
            return;
        siblings.Move(index, target);
        RaiseCommandsCanExecuteChanged();
    }

    private bool CanMoveToGroup()
    {
        var item = SelectedItem;
        var group = SelectedGroup;
        return item is not null && group is not null
            && !ReferenceEquals(item, group)
            && !string.Equals(item.Node.Kind, "group", StringComparison.Ordinal);
    }

    private void MoveSelectedToGroup()
    {
        var item = SelectedItem;
        var group = SelectedGroup;
        if (item is null || group is null || !CanMoveToGroup())
            return;
        var siblings = item.Parent is null ? Roots : item.Parent.Children;
        siblings.Remove(item);
        item.Parent = group;
        group.Children.Add(item);
        SelectedItem = item;
        RaiseCommandsCanExecuteChanged();
    }

    private void AddGroup(UtilitiesGroupEditParams? editParams)
    {
        var title = editParams is not null && !string.IsNullOrWhiteSpace(editParams.Title)
            ? editParams.Title!.Trim()
            : LocalizationManager.T("Utilities.Editor.NewGroup");
        var group = new UtilitiesMenuNode
        {
            Kind = "group",
            GroupId = null,
            CustomTitle = title,
        };
        ApplyIconToNode(group, editParams?.IconKey);
        var parent = IsGroup(SelectedItem) ? SelectedItem : null;
        var item = new UtilitiesMenuNodeItem(group, parent);
        if (parent is null)
            Roots.Add(item);
        else
            parent.Children.Add(item);
        Groups.Add(item);
        SelectedItem = item;
    }

    /// <summary>Переименование выбранной группы (диалог «Правка группы»).</summary>
    private void EditGroup(UtilitiesGroupEditParams? editParams)
    {
        var item = SelectedItem;
        if (item is null || !IsGroup(item) || editParams is null)
            return;
        if (!string.IsNullOrWhiteSpace(editParams.Title))
            item.Node.CustomTitle = editParams.Title!.Trim();
        ApplyIconToNode(item.Node, editParams.IconKey);
        item.NotifyChanged();
    }

    /// <summary>Удаление выбранной группы; при <paramref name="moveToRoot"/> дети переносятся
    /// в корень на место группы (с сохранением порядка), иначе группа удаляется вместе с детьми.</summary>
    private void DeleteGroup(bool moveToRoot)
    {
        var item = SelectedItem;
        if (item is null || !IsGroup(item))
            return;
        var siblings = item.Parent is null ? Roots : item.Parent.Children;
        if (moveToRoot && item.Children.Count > 0)
        {
            var index = siblings.IndexOf(item);
            var children = item.Children.ToList();
            foreach (var child in children)
            {
                child.Parent = null;
                siblings.Insert(index++, child);
            }
        }
        siblings.Remove(item);
        Groups.Remove(item);
        SelectedItem = null;
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Назначает иконку каталога узлу (ключ → ключ+цвет, сброс внешнего файла).</summary>
    private void ApplyIconToNode(UtilitiesMenuNode node, string? iconKey)
    {
        if (node is null || string.IsNullOrWhiteSpace(iconKey))
            return;
        var option = CatalogIcons.FirstOrDefault(o => string.Equals(o.Key, iconKey, StringComparison.Ordinal));
        if (option is null)
            return;
        node.IconKey = option.Key;
        node.IconColor = option.Color;
        node.ExternalIconPath = null;
    }

    /// <summary>True — узел является группой (подменю).</summary>
    private static bool IsGroup(UtilitiesMenuNodeItem? item) =>
        item is not null && string.Equals(item.Node.Kind, "group", StringComparison.Ordinal);

    private void ApplyCatalogIcon(string? key)
    {
        var item = SelectedItem;
        if (item is null || string.IsNullOrWhiteSpace(key))
            return;
        var option = CatalogIcons.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal));
        if (option is null)
            return;
        item.Node.IconKey = option.Key;
        item.Node.IconColor = option.Color;
        item.Node.ExternalIconPath = null;
        item.NotifyChanged();
    }

    private void ApplyExternalIcon(string path)
    {
        var item = SelectedItem;
        if (item is null || string.IsNullOrWhiteSpace(path))
            return;
        item.Node.ExternalIconPath = path;
        item.NotifyChanged();
    }

    private bool CanClearIcon() => SelectedItem is not null &&
        (!string.IsNullOrWhiteSpace(SelectedItem!.Node.IconKey) ||
         !string.IsNullOrWhiteSpace(SelectedItem!.Node.IconColor) ||
         !string.IsNullOrWhiteSpace(SelectedItem!.Node.ExternalIconPath));

    private void ClearSelectedIcon()
    {
        var item = SelectedItem;
        if (item is null)
            return;
        item.Node.IconKey = null;
        item.Node.IconColor = null;
        item.Node.ExternalIconPath = null;
        item.NotifyChanged();
    }

    private void ResetToDefault()
    {
        _layout = UtilitiesMenuLayouts.Default();
        Roots.Clear();
        Groups.Clear();
        foreach (var item in BuildTree(_layout.Root, null))
            Roots.Add(item);
        SelectedItem = null;
        SelectedGroup = null;
    }

    private void Save()
    {
        // Собираем дерево обратно в компоновку.
        var layout = new UtilitiesMenuLayout { SchemaVersion = 1 };
        foreach (var item in Roots)
            layout.Root.Add(ToNode(item));
        _layout = layout;
        try
        {
            _store?.Save(layout);
        }
        catch
        {
            // Не удалось сохранить — редактор остаётся открытым; ошибку покажет вызывающий код.
        }
    }

    private static UtilitiesMenuNode ToNode(UtilitiesMenuNodeItem item)
    {
        var node = new UtilitiesMenuNode
        {
            Kind = item.Node.Kind,
            ItemId = item.Node.ItemId,
            GroupId = item.Node.GroupId,
            CustomTitle = item.Node.CustomTitle,
            IconKey = item.Node.IconKey,
            IconColor = item.Node.IconColor,
            ExternalIconPath = item.Node.ExternalIconPath,
            Visible = item.Node.Visible,
            SeparatorBefore = item.Node.SeparatorBefore,
        };
        foreach (var child in item.Children)
            node.Children.Add(ToNode(child));
        return node;
    }

    private void Load()
    {
        var loaded = _store?.Load();
        if (loaded is null)
            return;
        _layout = loaded;
        Roots.Clear();
        Groups.Clear();
        foreach (var item in BuildTree(_layout.Root, null))
            Roots.Add(item);
        SelectedItem = null;
        SelectedGroup = null;
    }

    private void RaiseCommandsCanExecuteChanged()
    {
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        MoveToGroupCommand.RaiseCanExecuteChanged();
        ClearIconCommand.RaiseCanExecuteChanged();
        EditGroupCommand.RaiseCanExecuteChanged();
        DeleteGroupCommand.RaiseCanExecuteChanged();
    }

    private static IReadOnlyList<UtilitiesIconOption> BuildCatalogIcons()
    {
        var result = new List<UtilitiesIconOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in UtilitiesMenuCatalog.All)
        {
            if (string.IsNullOrWhiteSpace(item.WpfIconKind) || string.IsNullOrWhiteSpace(item.IconColor))
                continue;
            if (seen.Add(item.WpfIconKind + "|" + item.IconColor))
                result.Add(new UtilitiesIconOption(item.WpfIconKind, item.IconColor));
        }
        return result;
    }
}

/// <summary>Вариант иконки каталога для редактора (ключ PackIcon + цвет).</summary>
public sealed class UtilitiesIconOption
{
    public UtilitiesIconOption(string key, string color)
    {
        Key = key;
        Color = color;
    }

    /// <summary>Ключ иконки PackIcon (WPF).</summary>
    public string Key { get; }

    /// <summary>Цвет иконки #RRGGBB.</summary>
    public string Color { get; }

    /// <summary>Подпись для выпадающего списка: «Ключ (цвет)».</summary>
    public string Label => $"{Key} ({Color})";
}

/// <summary>
/// Узел дерева редактора: обёртка над <see cref="UtilitiesMenuNode"/> с коллекцией
/// дочерних узлов и родителем (для операций перемещения). Изменения свойств узла
/// транслируются в <see cref="UtilitiesMenuNode"/> компоновки.
/// </summary>
public sealed class UtilitiesMenuNodeItem : ViewModelBase
{
    /// <param name="node">Узел компоновки, который оборачиваем.</param>
    /// <param name="parent">Родительский узел дерева (null — корень).</param>
    public UtilitiesMenuNodeItem(UtilitiesMenuNode node, UtilitiesMenuNodeItem? parent)
    {
        Node = node;
        Parent = parent;
    }

    /// <summary>Оборачиваемый узел компоновки.</summary>
    public UtilitiesMenuNode Node { get; }

    /// <summary>Родительский узел дерева (null — корневой список).</summary>
    public UtilitiesMenuNodeItem? Parent { get; set; }

    /// <summary>Дочерние узлы (для групп).</summary>
    public ObservableCollection<UtilitiesMenuNodeItem> Children { get; } = new();

    /// <summary>Заголовок узла: пользовательский/локализованный для групп, словарь для пунктов.</summary>
    public string Title
    {
        get
        {
            if (string.Equals(Node.Kind, "group", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(Node.CustomTitle))
                    return Node.CustomTitle!;
                var key = UtilitiesMenuCatalog.GroupTitleKey(Node.GroupId ?? string.Empty);
                return string.IsNullOrEmpty(key) ? LocalizationManager.T("Utilities.Editor.CustomGroup") : LocalizationManager.T(key);
            }
            var catalog = UtilitiesMenuCatalog.Find(Node.ItemId);
            return catalog is null
                ? (Node.ItemId ?? "?")
                : LocalizationManager.T(catalog.TextKey);
        }
    }

    /// <summary>Пометка вида: «группа» / «пункт».</summary>
    public string KindLabel =>
        string.Equals(Node.Kind, "group", StringComparison.Ordinal)
            ? LocalizationManager.T("Utilities.Editor.KindGroup")
            : LocalizationManager.T("Utilities.Editor.KindItem");

    /// <summary>Видимость узла (чекбокс в дереве).</summary>
    public bool IsVisible
    {
        get => Node.Visible;
        set
        {
            Node.Visible = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Строка для списка групп: заголовок.</summary>
    public string GroupLabel => Title;

    /// <summary>Ключ иконки каталога для отображения в дереве (пусто — без иконки).</summary>
    public string DisplayIconKey => Node.IconKey ?? string.Empty;

    /// <summary>Цвет иконки каталога (#RRGGBB) для отображения в дереве.</summary>
    public string DisplayIconColor => Node.IconColor ?? string.Empty;

    /// <summary>Есть ли иконка каталога для отображения в дереве (видимость превью).</summary>
    public bool HasDisplayIcon => !string.IsNullOrWhiteSpace(Node.IconKey);

    /// <summary>Оповещает UI об изменении внешних свойств узла (иконка, заголовок).</summary>
    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(GroupLabel));
        OnPropertyChanged(nameof(DisplayIconKey));
        OnPropertyChanged(nameof(DisplayIconColor));
        OnPropertyChanged(nameof(HasDisplayIcon));
    }
}