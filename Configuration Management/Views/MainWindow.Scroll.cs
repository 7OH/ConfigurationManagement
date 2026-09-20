#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using MaterialDesignThemes.Wpf;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Configuration_Management
{
    public partial class MainWindow
    {

        /// <summary>Кэш внутреннего <see cref="ScrollViewer"/> дерева (issue #252).</summary>
        private ScrollViewer? _treeScrollViewer;
        /// <summary>
        /// Кэш внутреннего <see cref="ScrollContentPresenter"/> дерева (issue #255): полный обход
        /// визуального дерева на каждый вызов при прокрутке добавлял лишнюю нагрузку. Сбрасывается
        /// вместе с <see cref="_treeScrollViewer"/> при отсоединении дерева (Unloaded).
        /// </summary>
        private ScrollContentPresenter? _treeScrollContentPresenter;
        private bool _treeScrollHookAttached;

        // Guard от рекурсии при принудительном сбросе горизонтали дерева (issue #255).
        private bool _resettingTreeHScroll;

        /// <summary>Позиция вертикальной прокрутки, запомненная до пересборки дерева (issue #252).</summary>
        private double _treeScrollOffset;

        /// <summary>Ссылка на верхнюю видимую строку до пересборки (восстановление по строке, а не по пикселям).</summary>
        private object? _treeScrollAnchorData;

        // Ключ группы выбранной базы до пересборки (issue #252): если после пересборки база
        // осталась в той же группе и видимой, позицию восстанавливаем точным offset, а не
        // BringIntoView — иначе список «съезжает» после правки свойств без смены группы.
        private string? _treeScrollAnchorGroup;

        /// <summary>
        /// Возвращает данные верхней видимой строки дерева. Виртуализация реализует только
        /// видимые контейнеры, поэтому первый собранный контейнер и есть верхняя видимая строка.
        /// </summary>
        private object? GetTopVisibleRowData()
        {
            var rows = GetVisibleTreeViewItems();
            return rows.Count > 0 ? rows[0].DataContext : null;
        }

        /// <summary>
        /// Запоминает позицию прокрутки до пересборки: список опустеет, и после неё
        /// прежнюю позицию узнать уже неоткуда (паттерн из Linux/Avalonia-версии).
        /// Помимо «сырого» offset сохраняем ссылку на верхнюю видимую строку: после
        /// пересборки виртуализированное дерево имеет другой контент/высоты, и восстановление
        /// по строке точнее, чем по пикселям (issue #252). Значения не обнуляются после
        /// применения, чтобы две пересборки подряд восстановили одну и ту же позицию.
        /// </summary>
        private void RememberTreeScroll()
        {
            // Перед открытием модального окна свойств снимаем отложенную прокрутку (issue #252):
            // в очереди мог стоять DeferredScrollSelectedIntoView, поставленный кликом, которым
            // открыли свойства. Если его не снять, он исполнится позже (priority Loaded) и перетрёт
            // восстановленную при закрытии окна позицию — список «скачет» после «Нет».
            _scrollQueued = false;
            _scrollTargetData = null;

            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            _treeScrollOffset = treeScroll.VerticalOffset;
            _treeScrollAnchorData = GetTopVisibleRowData();
            _treeScrollAnchorGroup = _viewModel?.SelectedInfobase is { } ib
                ? FindGroupNodeByInfobase(ib)?.NodeKey
                : null;
        }

        /// <summary>
        /// Восстанавливает позицию прокрутки после пересборки дерева, чтобы список
        /// не «прыгал» в сторону (например, после сохранения свойств базы, issue #252).
        /// Вызывается после восстановления выделения: BringIntoView мог сдвинуть список
        /// к строке — возвращаем позицию, где был пользователь.
        /// </summary>
        private void RestoreTreeScrollAfterRebuild()
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            try
            {
                // Раскрытие предков и layout должны устояться, иначе позиция «садится не туда»
                // и сравнение верхней видимой строки (GetTopVisibleRowData) будет некорректным.
                MainTree.UpdateLayout();

                // Если выбранная база осталась в той же группе, ключевое состояние не изменилось —
                // восстанавливаем точный offset, а не «якорную» строку: так после правки свойств
                // без смены группы список не сдвигается (issue #252).
                var currentGroup = _viewModel?.SelectedInfobase is { } ib
                    ? FindGroupNodeByInfobase(ib)?.NodeKey
                    : null;
                var groupUnchanged = !string.IsNullOrEmpty(_treeScrollAnchorGroup)
                    && string.Equals(_treeScrollAnchorGroup, currentGroup, StringComparison.Ordinal);

                // Guard «ничего ключевого не изменилось» (issue #252): если группа не сменилась И
                // запомненная верхняя видимая строка осталась верхней после пересборки — позиция
                // уже корректна, не позиционируем вовсе (требование пользователя: «если ничего
                // ключевого не изменилось, просто не надо позиции пересчитывать»). Касается только
                // прокрутки; выделение и фокус восстанавливаются отдельно в RevealAndSelectAfterRebuild.
                if (groupUnchanged &&
                    _treeScrollAnchorData is not null &&
                    ReferenceEquals(GetTopVisibleRowData(), _treeScrollAnchorData))
                {
                    return;
                }

                if (groupUnchanged)
                {
                    treeScroll.ScrollToVerticalOffset(_treeScrollOffset);
                }
                else if (_treeScrollAnchorData is { } anchor)
                {
                    // Восстанавливаем по запомненной верхней видимой строке (не по «сырым» пикселям):
                    // виртуализированное дерево после пересборки имеет другой контент/высоты (issue #252).
                    if (FindTreeViewItemForData(anchor) is { } anchorItem)
                        anchorItem.BringIntoView();
                }
                else
                {
                    treeScroll.ScrollToVerticalOffset(_treeScrollOffset);
                }

                // Горизонтальная прокрутка дерева не используется (синхронизацию ведёт внешний
                // заголовок): сбрасываем горизонталь, чтобы восстановление не создавало лишний
                // горизонтальный скрол (issue #255).
                if (treeScroll.HorizontalOffset != 0)
                    treeScroll.ScrollToHorizontalOffset(0);

                // Clamp вертикальной позиции к допустимому диапазону (issue #252).
                var maxOffset = Math.Max(0, treeScroll.ScrollableHeight);
                if (treeScroll.VerticalOffset > maxOffset + 0.01)
                    treeScroll.ScrollToVerticalOffset(maxOffset);
            }
            catch
            {
                // Дерево могло отсоединиться во время пересборки — игнорируем.
            }
        }

        /// <summary>
        /// Восстанавливает прежнюю позицию прокрутки после закрытия окна свойств базы без
        /// сохранения («Нет»). Пересборки дерева не было, поэтому события TreeRebuilding/
        /// TreeRebuilt не сработали и <see cref="RestoreTreeScrollAfterRebuild"/> не вызвался,
        /// а при закрытии модального окна WPF сам подтягивает выбранную строку в видимую
        /// область — список «уезжает» вверх/вниз (issue #252). Возвращаем точный offset,
        /// запомненный <see cref="RememberTreeScroll"/> перед открытием окна.
        /// </summary>
        private void RestoreTreeScrollAfterCancel()
        {
            // Откладываем восстановление до ApplicationIdle (issue #252): синхронный вызов здесь
            // «проигрывал» отложенному DeferredScrollSelectedIntoView (priority Loaded), который
            // исполнялся позже и перетирал восстановленную позицию. На ApplicationIdle любые
            // отложенные прокрутки уже завершились, и позиция возвращается без конкуренции.
            Dispatcher.BeginInvoke(new Action(RestoreTreeScrollAfterCancelCore),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>Непосредственное восстановление позиции после «Нет» (см. <see cref="RestoreTreeScrollAfterCancel"/>).</summary>
        private void RestoreTreeScrollAfterCancelCore()
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            try
            {
                // Чистый restore-путь: принудительная раскладка не нужна, позиция возвращается
                // точным offset без пересчёта ширины колонок (issue #252).
                treeScroll.ScrollToVerticalOffset(_treeScrollOffset);

                // Горизонталь дерева не используется — сбрасываем (как после пересборки).
                if (treeScroll.HorizontalOffset != 0)
                    treeScroll.ScrollToHorizontalOffset(0);

                var maxOffset = Math.Max(0, treeScroll.ScrollableHeight);
                if (treeScroll.VerticalOffset > maxOffset + 0.01)
                    treeScroll.ScrollToVerticalOffset(maxOffset);
            }
            catch
            {
                // Дерево могло быть отсоединено — игнорируем.
            }
        }

        /// <summary>
        /// Внутренний ScrollViewer шаблона TreeView (отвечает за вертикальную и горизонтальную прокрутку).
        /// Найденный экземпляр кэшируется: без кэша каждый вызов делает <c>ApplyTemplate()</c> и полный
        /// обход визуального дерева, что при частой прокрутке/материализации строк добавляет нагрузку и
        /// «прыжки» списка. Кэш сбрасывается при отсоединении дерева (Unloaded), чтобы не оставаться
        /// с устаревшей ссылкой после пересборки/пересоздания контейнера.
        /// </summary>
        private ScrollViewer? GetTreeScrollViewer()
        {
            if (MainTree is null)
                return null;

            if (!_treeScrollHookAttached)
            {
                _treeScrollHookAttached = true;
                MainTree.Unloaded += (_, _) =>
                {
                    _treeScrollViewer = null;
                    _treeScrollContentPresenter = null;
                };
            }
            if (_treeScrollViewer is not null)
                return _treeScrollViewer;

            // Шаблон может быть ещё не применён.
            MainTree.ApplyTemplate();
            _treeScrollViewer = FindVisualChild<ScrollViewer>(MainTree);
            return _treeScrollViewer;
        }

        /// <summary>
        /// Подписывается на ScrollChanged внутреннего ScrollViewer дерева (синхронизация заголовка).
        /// </summary>
        private void AttachTreeScrollHandler()
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            treeScroll.ScrollChanged -= OnTreeScroll_ScrollChanged;
            treeScroll.ScrollChanged += OnTreeScroll_ScrollChanged;
        }

        private void OnTreeScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (DbHeaderScroll is null)
                return;

            // Горизонтальная прокрутка дерева не используется (её ведёт внешний заголовок),
            // но при пиксельной виртуализации на старте дерево может получить ненулевую
            // горизонталь — из-за неё появляется «необоснованный» горизонтальный скролл
            // (issue #255). Принудительно держим горизонталь дерева на нуле.
            if (!_resettingTreeHScroll && Math.Abs(e.HorizontalOffset) > 0.01)
            {
                _resettingTreeHScroll = true;
                try { ((ScrollViewer)sender).ScrollToHorizontalOffset(0); }
                finally { _resettingTreeHScroll = false; }
            }

            if (Math.Abs(DbHeaderScroll.HorizontalOffset - e.HorizontalOffset) > 0.01)
                DbHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);

            // Синхронизируем ширину заголовка с данными только при изменении размеров вьюпорта,
            // а не при изменении ExtentWidth. При пиксельной виртуализации ExtentWidth определяется
            // максимумом по РЕАЛИЗОВАННЫМ детям и меняется на каждом шаге прокрутки; реакция на него
            // каждый раз пересчитывала ширину и запускала повторную раскладку, из-за чего полоса
            // прокрутки меняла размер и список «прыгал» (issue #255).
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
                SyncHeaderWidthWithList();
        }

        private void OnMainTree_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollListByWheel(e);
        }

        private void OnDbHeader_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollListByWheel(e);
        }

        /// <summary>
        /// Прокрутка списка: колесо — вертикаль, Shift+колесо — горизонталь.
        /// Всегда помечает событие обработанным, чтобы вложенные элементы не «съедали» колесо.
        /// </summary>
        private void ScrollListByWheel(MouseWheelEventArgs e)
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
            {
                // Повторная попытка после загрузки шаблона.
                AttachTreeScrollHandler();
                treeScroll = GetTreeScrollViewer();
            }

            if (treeScroll is null)
                return;

            // e.Delta обычно ±120; делим для плавности.
            var offset = -e.Delta / 3.0;

            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                treeScroll.ScrollToHorizontalOffset(treeScroll.HorizontalOffset + offset);
            }
            else
            {
                treeScroll.ScrollToVerticalOffset(treeScroll.VerticalOffset + offset);
            }

            e.Handled = true;
        }

    }
}
#endif
