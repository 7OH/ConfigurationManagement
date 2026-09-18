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
        private bool _treeScrollHookAttached;

        /// <summary>Позиция вертикальной прокрутки, запомненная до пересборки дерева (issue #252).</summary>
        private double _treeScrollOffset;

        /// <summary>
        /// Запоминает позицию прокрутки до пересборки: список опустеет, и после неё
        /// прежнюю позицию узнать уже неоткуда (паттерн из Linux/Avalonia-версии).
        /// Значение не обнуляется после применения, чтобы две пересборки подряд
        /// восстановили одну и ту же позицию.
        /// </summary>
        private void RememberTreeScroll()
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is not null)
                _treeScrollOffset = treeScroll.VerticalOffset;
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
                treeScroll.ScrollToVerticalOffset(_treeScrollOffset);
            }
            catch
            {
                // Дерево могло отсоединиться во время пересборки — игнорируем.
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
                MainTree.Unloaded += (_, _) => _treeScrollViewer = null;
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
