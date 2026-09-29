using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>Роль узла дерева «Обозревателя метаданных» (цикл 0.3.9.132–0.3.9.136, этап 2).</summary>
public enum MetadataTreeNodeKind
{
    /// <summary>Корень: «Конфигурация <имя> <версия>».</summary>
    Root,

    /// <summary>Подсистема (контейнер «Подсистемы» или сама подсистема).</summary>
    Subsystem,

    /// <summary>Группа «Без подсистемы» / «Все объекты» (типы → объекты).</summary>
    NoSubsystemGroup,

    /// <summary>Тип метаданных («Справочники», «Документы» …) — локализуется через <see cref="Services.MetadataTypeLocalizer"/>.</summary>
    Type,

    /// <summary>Конкретный объект метаданных (ссылка на <see cref="MetadataObjectSummary"/>).</summary>
    Object
}

/// <summary>
/// Узел дерева «Обозревателя метаданных» (этап 2): ленивое дерево
/// «Конфигурация → подсистемы → типы → объекты». Дети подгружаются по требованию:
/// <see cref="EnsureLoaded"/> вызывает переданный при создании загрузчик один раз
/// (идемпотентно, флаг <see cref="IsLoaded"/>); окно зовёт его в обработчике раскрытия
/// узла (WPF — <c>TreeViewItem.Expanded</c>, Avalonia — <c>TreeViewItem.Expanded</c>).
/// Чистый .NET без платформенных зависимостей — используется обеими платформами.
///
/// Для того чтобы раскрывалка была видна ДО загрузки детей, узел с загрузчиком
/// добавляет себе узел-заглушку (<see cref="IsPlaceholder"/>): WPF/Avalonia рисуют
/// стрелку только при непустой коллекции детей. При <see cref="EnsureLoaded"/> заглушка
/// удаляется и заменяется реальными детьми.
/// </summary>
public sealed class MetadataTreeNodeViewModel : ViewModelBase
{
    private readonly Func<IEnumerable<MetadataTreeNodeViewModel>>? _childrenLoader;
    private bool _isLoaded;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isMatch;

    /// <param name="kind">Роль узла (см. <see cref="MetadataTreeNodeKind"/>).</param>
    /// <param name="displayName">Отображаемое имя (для типов — уже локализованное через
    /// <see cref="Services.MetadataTypeLocalizer.GetDisplayName"/>).</param>
    /// <param name="countText">Текст счётчика вида «(N)» (обычно у узлов типов); пусто — нет.</param>
    /// <param name="childrenLoader">Ленивый загрузчик детей; null — узел без детей
    /// (или дети добавляются напрямую через <see cref="AddChild"/>).</param>
    /// <param name="hasChildren">Заведомо ли есть дети (иначе определится после загрузки).
    /// True — узел получит заглушку-раскрывалку.</param>
    /// <param name="summary">Объект метаданных для узла <see cref="MetadataTreeNodeKind.Object"/>.</param>
    /// <param name="subsystem">Подсистема для узла <see cref="MetadataTreeNodeKind.Subsystem"/>.</param>
    public MetadataTreeNodeViewModel(
        MetadataTreeNodeKind kind,
        string displayName,
        string countText = "",
        Func<IEnumerable<MetadataTreeNodeViewModel>>? childrenLoader = null,
        bool hasChildren = false,
        MetadataObjectSummary? summary = null,
        MetadataSubsystem? subsystem = null)
    {
        Kind = kind;
        DisplayName = displayName ?? string.Empty;
        CountText = countText ?? string.Empty;
        _childrenLoader = childrenLoader;
        HasChildren = hasChildren;
        ObjectSummary = summary;
        Subsystem = subsystem;

        // Заглушка-раскрывалка: без неё TreeView не покажет стрелку у ленивого узла
        // (раскрывать будет нечего, событие Expanded не наступит). Узел невидим
        // (пустое имя), окно скрывает его по IsPlaceholder.
        if (hasChildren && childrenLoader is not null)
            Children.Add(new MetadataTreeNodeViewModel(MetadataTreeNodeKind.Object, string.Empty, isPlaceholder: true));
    }

    /// <summary>Внутренний конструктор узла-заглушки.</summary>
    private MetadataTreeNodeViewModel(MetadataTreeNodeKind kind, string displayName, bool isPlaceholder)
    {
        Kind = kind;
        DisplayName = displayName;
        CountText = string.Empty;
        IsPlaceholder = isPlaceholder;
    }

    /// <summary>Роль узла в дереве.</summary>
    public MetadataTreeNodeKind Kind { get; }

    /// <summary>Отображаемое имя (локализованное).</summary>
    public string DisplayName { get; }

    /// <summary>Текст счётчика («(N)») или пустая строка.</summary>
    public string CountText { get; }

    /// <summary>Дети узла (подгружаются лениво через <see cref="EnsureLoaded"/>).</summary>
    public ObservableCollection<MetadataTreeNodeViewModel> Children { get; } = new();

    /// <summary>Есть ли у узла дети (известно при создании либо после первой загрузки).</summary>
    public bool HasChildren { get; private set; }

    /// <summary>Родительский узел (null для корня).</summary>
    public MetadataTreeNodeViewModel? Parent { get; private set; }

    /// <summary>Объект метаданных узла <see cref="MetadataTreeNodeKind.Object"/> (null для остальных).</summary>
    public MetadataObjectSummary? ObjectSummary { get; }

    /// <summary>Подсистема узла <see cref="MetadataTreeNodeKind.Subsystem"/> (null для остальных).</summary>
    public MetadataSubsystem? Subsystem { get; }

    /// <summary>Дети загружены (после первого <see cref="EnsureLoaded"/>); повторные вызовы — no-op.</summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>Узел-заглушка раскрывалки (невидимый плейсхолдер до первой загрузки детей).</summary>
    public bool IsPlaceholder { get; }

    /// <summary>Раскрыт ли узел (управляется деревом/окном; нужен для поиска на этапе 3).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Выбран ли узел (управляется деревом/окном).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Пометка совпадения поиска (заготовка этапа 3: подсветка совпадений и
    /// автораскрытие путей). На этапе 2 всегда false.
    /// </summary>
    public bool IsMatch
    {
        get => _isMatch;
        set => SetProperty(ref _isMatch, value);
    }

    /// <summary>
    /// Ленивая загрузка детей: вызывает загрузчик один раз (идемпотентно), удаляя
    /// заглушку-раскрывалку и добавляя реальные узлы. Вызывается окном при раскрытии
    /// узла; безопасна из любого потока (на UI-потоке — после события дерева).
    /// </summary>
    public void EnsureLoaded()
    {
        if (_isLoaded || _childrenLoader is null)
            return;

        _isLoaded = true;
        Children.Clear();

        foreach (var child in _childrenLoader())
            AddChild(child);

        HasChildren = Children.Count > 0;
    }

    /// <summary>Добавляет дочерний узел, проставляя ему родителя.</summary>
    public void AddChild(MetadataTreeNodeViewModel child)
    {
        if (child is null || child.IsPlaceholder)
            return;
        child.Parent = this;
        Children.Add(child);
    }
}