#if LINUX
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Обёртка закреплённой базы для элемента дерева в узле «Закреплённые» (Avalonia,
/// issue #301). Раньше один и тот же экземпляр <see cref="Infobase"/> попадал в дерево
/// дважды — в узел «Закреплённые» (первый корень) и в собственную группу базы, а штатное
/// выделение Avalonia красит ПЕРВЫЙ контейнер с данными по всему дереву
/// (TreeContainerFromItem → SetCurrentValue, который в Avalonia 11.3 перезаписывает
/// и локальные значения свойства): клик или восстановление выделения подсвечивали
/// копию в «Закреплённых». Уникальный объект данных у каждой строки дерева делает
/// такое перекрашивание структурно невозможным: поиск контейнера по данным всегда
/// находит единственный контейнер.
///
/// Обёртка живёт только элементом дерева (Items узла «Закреплённые»); строка строится
/// и привязывается по <see cref="Base"/> — BuildInfobaseRow и все команды получают
/// развёрнутую базу, поэтому поведение строки (кнопки, меню, перетаскивание)
/// не меняется. Точки разворачивания: BuildTreeRow, OnTreeSelectionChanged,
/// определение объекта перетаскивания (MainWindow.Avalonia.DragDrop.cs).
/// </summary>
public sealed class PinnedInfobaseItem
{
    public PinnedInfobaseItem(Infobase infobase) => Base = infobase;

    /// <summary>Реальная информационная база этой строки.</summary>
    public Infobase Base { get; }
}
#endif
