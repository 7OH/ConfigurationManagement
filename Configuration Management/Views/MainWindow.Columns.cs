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

        /// <summary>
        /// Обработчик клика по заголовку колонки для смены сортировки.
        /// </summary>
        private void OnColumnHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string field)
                return;
            _viewModel.SetSortField(field);
            e.Handled = true;
        }

        /// <summary>
        /// Скрывает колонку списка баз по её ключу (пункт «Скрыть колонку»
        /// контекстного меню заголовка, issue #173). Ключ колонки лежит в Tag пункта меню.
        /// </summary>
        private void OnColumnHeaderContextMenu_Hide(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: string key } && !string.IsNullOrEmpty(key))
                _viewModel?.SetColumnVisible(key, false);
        }

        /// <summary>
        /// Открывает настройки сразу на подвкладке «Колонки» (пункт контекстного меню
        /// заголовка, issue #173).
        /// </summary>
        private void OnColumnHeaderContextMenu_OpenSettings(object sender, RoutedEventArgs e)
        {
            OpenSettingsOnColumnsTab();
        }

        /// <summary>Открывает окно настроек сразу на подвкладке «Колонки» (issue #173).</summary>
        private void OpenSettingsOnColumnsTab()
        {
            var dialog = new SettingsWindow(_viewModel) { Owner = this };
            dialog.SelectColumnsTab();
            dialog.ShowDialog();
        }

        /// <summary>
        /// Ограничение «системного меню» для режима «Пользователь» (Этап 10 StartManager).
        /// При открытии контекстного меню базы скрывает все системные пункты, кроме
        /// помеченных <c>Tag="User"</c> (запуск Предприятия/Конфигуратора, избранное, закрепление).
        /// В остальных режимах («Специалист»/«Разработчик») меню показывается полностью.
        /// </summary>
        private void OnBaseContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu || _viewModel == null)
                return;

            var restricted = _viewModel.IsSystemMenuRestricted;
            var items = menu.Items.Cast<object>().ToList();

            // Мультивыделение (0.3.9.90): блок «Для выделенных (N)…» виден только
            // при N > 1, заголовок показывает текущее число помеченных баз.
            // В режиме «Пользователь» пакетные операции — системные, скрываются
            // общим правилом ниже (пункт помечен Tag="Batch", не "User").
            var batchMenu = items.OfType<MenuItem>()
                .FirstOrDefault(m => string.Equals(m.Tag as string, "Batch", StringComparison.Ordinal));
            if (batchMenu is not null)
            {
                var batchCount = _viewModel.BatchSelectedCount;
                batchMenu.Header = string.Format(
                    LocalizationManager.T("Main.BatchForSelected"), batchCount);
                batchMenu.Visibility = batchCount > 1 && !restricted
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            foreach (var item in items.OfType<MenuItem>())
            {
                var allowed = string.Equals(item.Tag as string, "User", StringComparison.Ordinal);
                item.Visibility = restricted && !allowed ? Visibility.Collapsed : Visibility.Visible;
            }
            // Пакетный блок уже получил свою видимость; возвращаем её после общего
            // прохода (в не-restricted режиме общий проход делает все пункты видимыми).
            if (batchMenu is not null && !restricted)
            {
                batchMenu.Visibility = _viewModel.BatchSelectedCount > 1
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            // Скрываем разделители, оставшиеся рядом со скрытыми пунктами.
            if (restricted)
            {
                for (var i = items.Count - 1; i >= 0; i--)
                {
                    if (items[i] is not Separator sep)
                        continue;
                    var prevVisible = items.Take(i).OfType<MenuItem>()
                        .Any(m => m.Visibility == Visibility.Visible);
                    var nextVisible = items.Skip(i + 1).OfType<MenuItem>()
                        .Any(m => m.Visibility == Visibility.Visible);
                    sep.Visibility = prevVisible && nextVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            // Пользовательские действия (0.3.9.197): наполняем подменю в конце — после
            // batch-логики и общего прохода видимости. Финальную видимость подменю и его
            // разделителя метод устанавливает явно (в т.ч. в restricted-режиме, где общий
            // проход оставляет Tag="User" видимым).
            PopulateCustomActionsMenu(menu);
        }

        /// <summary>
        /// Строит целевую последовательность колонок (логические ключи) по выбранному
        /// пользователем порядку. Первая итерация идёт по пользовательскому порядку
        /// (<see cref="_viewModel.ColumnOrderKeys"/>), отбрасывая незнакомые ключи, — поэтому
        /// фактически применяется порядок, заданный пользователем в настройках, в т.ч. перенос
        /// колонки «Действия». Второй проход лишь дополняет недостающие известные колонки в
        /// порядке по умолчанию, гарантируя их наличие.
        /// </summary>
        private List<string> BuildColumnLayout()
        {
            var known = new[] { "Version", "LaunchMode", "Actions", "ServerBase", "LastLaunch", "Size", "Modified", "LastBackup", "Configuration", "ConfigurationVersion" };
            var keys = new List<string>();
            // Идём по ПОЛЬЗОВАТЕЛЬСКОМУ порядку, отбрасывая незнакомые ключи,
            // чтобы фактически применять выбранный порядок (в т.ч. перенос «Действий»).
            var source = _viewModel?.ColumnOrderKeys ?? Array.Empty<string>();
            foreach (var k in source)
                if (Array.IndexOf(known, k) >= 0 && !keys.Contains(k))
                    keys.Add(k);
            // Гарантируем, что все известные колонки присутствуют (незнакомые ключи
            // из сохранённого порядка пропускаются).
            foreach (var k in known)
                if (!keys.Contains(k))
                    keys.Add(k);

            return keys;
        }

        /// <summary>
        /// Перестраивает колонки данных сетки <paramref name="grid"/> (заголовка, строки базы
        /// или заголовка группы) под выбранный порядок: передвигает определения колонок и
        /// обновляет позиции размещённых в них элементов. Фиксированные колонки слева и
        /// «Название» не трогаются.
        /// </summary>
        private void ReorderGridColumns(Grid grid, int firstDataCol)
        {
            if (grid is null || _viewModel is null)
                return;

            var layout = BuildColumnLayout();
            var leading = firstDataCol;
            var dataCount = grid.ColumnDefinitions.Count - leading;
            if (dataCount <= 0)
                return;

            // Первый проход: определяем логический ключ для каждого перемещаемого элемента
            // региона данных по статической раскладке. Ключ сохраняется в attached-свойстве
            // ColumnKey (Tag занят — сортировка/двойной клик), поэтому повторные вызовы
            // корректно работают и после перестановок, и для уже перестроенных сеток.
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                if (Grid.GetColumnSpan(fe) != 1)
                    continue; // объединённые ячейки (название/теги) двигаются отдельно
                var c = Grid.GetColumn(fe);
                if (c < leading || c >= leading + dataCount)
                    continue;
                if (string.IsNullOrEmpty(GetColumnKey(fe)))
                    SetColumnKey(fe, StaticDataColumnKeys[c - leading]);
            }

            // Сопоставление «позиция колонки данных -> логический ключ» по ключам детей.
            var defKey = new string[dataCount];
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                var s = GetColumnKey(fe);
                if (string.IsNullOrEmpty(s))
                    continue;
                if (Grid.GetColumnSpan(fe) != 1)
                    continue;
                var c = Grid.GetColumn(fe);
                if (c >= leading && c < leading + dataCount)
                    defKey[c - leading] = s;
            }

            // Для сеток без дочерних элементов в части колонок данных (например, строка группы,
            // где заполнена только колонка «Действия») ключ незаполненных колонок берём по
            // статической раскладке: такие сетки всегда создаются в статическом порядке.
            for (var i = 0; i < dataCount; i++)
                if (string.IsNullOrEmpty(defKey[i]))
                    defKey[i] = StaticDataColumnKeys[i];

            // Новый порядок определений колонок под нужную раскладку.
            var defs = grid.ColumnDefinitions;
            var newOrder = new List<ColumnDefinition>(dataCount);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in layout)
            {
                for (var i = 0; i < dataCount; i++)
                {
                    if (defKey[i] == token && placed.Add(token))
                    {
                        newOrder.Add(defs[leading + i]);
                        break;
                    }
                }
            }
            for (var i = 0; i < dataCount; i++)
                if (!placed.Contains(defKey[i]))
                {
                    newOrder.Add(defs[leading + i]);
                    placed.Add(defKey[i]);
                }

            // Ранний выход (issue #255): если фактический порядок определений колонок данных
            // уже совпадает с целевым, ничего не пересобираем. Строки реализуются виртуализацией
            // на каждую прокрутку/материализацию, и до правки здесь каждый раз выполнялся
            // defs.Clear() с повторным добавлением всех ColumnDefinition — сброс внутреннего кэша
            // колонок сетки и инвалидация measure/arrange даже когда переставлять нечего.
            var orderUnchanged = true;
            for (var i = 0; i < dataCount; i++)
                if (!ReferenceEquals(defs[leading + i], newOrder[i]))
                {
                    orderUnchanged = false;
                    break;
                }
            if (orderUnchanged)
                return;

            // Пересобираем коллекцию определений: фиксированные слева + новый порядок данных.
            var leadingDefs = new List<ColumnDefinition>(leading);
            for (var i = 0; i < leading; i++)
                leadingDefs.Add(defs[i]);
            defs.Clear();
            foreach (var d in leadingDefs)
                defs.Add(d);
            foreach (var d in newOrder)
                defs.Add(d);

            // Обновляем позиции детей и span широких ячеек (до «Действий»).
            // Заголовок группы: имя/счётчик занимают область названия и тянутся до «Действий».
            var actionsColumn = leading + layout.IndexOf("Actions");
            var isGroup = ReferenceEquals(grid.Tag, GroupGridMarker);
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                var s = GetColumnKey(fe);
                if (!string.IsNullOrEmpty(s))
                {
                    var ti = layout.IndexOf(s);
                    if (ti >= 0)
                        Grid.SetColumn(fe, leading + ti);
                }
                else if (isGroup && Grid.GetRow(fe) == 0 && Grid.GetColumn(fe) == 0 && Grid.GetColumnSpan(fe) > 1)
                {
                    Grid.SetColumnSpan(fe, actionsColumn);
                }
                else if (Grid.GetRow(fe) == 1 && Grid.GetColumn(fe) == 0 && Grid.GetColumnSpan(fe) > 1)
                {
                    Grid.SetColumnSpan(fe, actionsColumn);
                }
            }
        }

        /// <summary>Рекурсивно собирает уже созданные сетки строк баз/заголовков групп по маркеру.</summary>
        private static List<Grid> FindRowGrids(DependencyObject? root, object marker)
        {
            var result = new List<Grid>();
            FindRowGridsCore(root, marker, result);
            return result;
        }

        private static void FindRowGridsCore(DependencyObject? parent, object marker, List<Grid> acc)
        {
            if (parent is null)
                return;
            if (parent is Grid g && ReferenceEquals(g.Tag, marker))
                acc.Add(g);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                FindRowGridsCore(VisualTreeHelper.GetChild(parent, i), marker, acc);
        }

        /// <summary>
        /// Находит первую сетку с указанным маркером внутри <paramref name="root"/>
        /// (используется для выравнивания заголовка по строке базы).
        /// </summary>
        private static Grid? FindGridByMarker(DependencyObject? root, object marker)
        {
            if (root is Grid g && ReferenceEquals(g.Tag, marker))
                return g;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = FindGridByMarker(VisualTreeHelper.GetChild(root, i), marker);
                if (found is not null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Применяет выбранный порядок колонок к заголовку и всем созданным строкам баз
        /// и заголовкам групп. Вызывается при старте (после компоновки) и при изменении
        /// порядка в настройках.
        /// </summary>
        private void ApplyColumnOrder()
        {
            // issue #255: изменение порядка колонок сбрасывает флаг «порядок применён» на всех
            // найденных сетках, после чего существующие строки перестраиваются один раз (повторно
            // применять в On*RowGrid_Loaded не придётся, т.к. флаг снова выставляется здесь же).
            if (HeaderGrid is not null)
            {
                SetColumnOrderApplied(HeaderGrid, false);
                ReorderGridColumns(HeaderGrid, HeaderFirstDataColumn);
                SetColumnOrderApplied(HeaderGrid, true);
            }
            foreach (var grid in FindRowGrids(MainTree, RowGridMarker))
            {
                SetColumnOrderApplied(grid, false);
                ReorderGridColumns(grid, RowFirstDataColumn);
                SetColumnOrderApplied(grid, true);
            }
            foreach (var grid in FindRowGrids(MainTree, GroupGridMarker))
            {
                SetColumnOrderApplied(grid, false);
                ReorderGridColumns(grid, RowFirstDataColumn);
                SetColumnOrderApplied(grid, true);
            }

            // Порядок колонок влияет на состав суммы минимальной ширины (перенос «Действий»,
            // смена мест): после перестановки определений пересчитываем ширину заголовка и
            // минимум контента, иначе полоса оставалась бы по прежнему порядку и могла не
            // дотягивать до последней (новой) колонки (issue #309). Замер фактической
            // ширины строк обновляется вместе с суммой — строки уже перестроены под
            // новый порядок колонок.
            UpdateTreeMinWidthContent();
            SyncHeaderWidthWithList();

            // Fallback-минимум (issue #309, девятая попытка): после применения настроек
            // колонок/порядка список принудительно прокручивается до конца горизонтальной
            // полосы — последние колонки гарантированно достижимы и видны, даже если
            // расчётная минимальная ширина оказалась меньше фактической ширины контента.
            EnsureHorizontalReach();
        }

        /// <summary>
        /// Fallback-минимум (issue #309, девятая попытка): после применения настроек
        /// колонок/порядка принудительно прокручивает список до конца горизонтальной
        /// полосы, чтобы последние колонки были гарантированно достижимы и видны даже
        /// при расхождении расчётной и фактической ширины контента. Выполняется отложенно
        /// — после завершения раскладки строк под новый порядок колонок. Вызывается только
        /// из точки применения настроек, поэтому позиция прокрутки, установленная
        /// пользователем в обычной работе, не затрагивается.
        /// </summary>
        private void EnsureHorizontalReach()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var treeScroll = GetTreeScrollViewer();
                    if (treeScroll is null || treeScroll.ScrollableWidth <= 1)
                        return;
                    treeScroll.ScrollToHorizontalOffset(treeScroll.ExtentWidth);
                }
                catch
                {
                    // Fallback не должен ломать раскладку.
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Обработчик Loaded сетки строки базы в шаблоне: применяет выбранный порядок
        /// колонок к каждой вновь созданной строке (включая строки, появляющиеся при
        /// виртуализации/прокрутке дерева) и пересчитывает выравнивание заголовка.
        /// Пересчёт здесь обязателен: строки реализуются виртуализацией в проходе
        /// разметки ПОСЛЕ события Loaded дерева и пересборки (поиск, крестик поиска,
        /// сохранение свойств базы), поэтому пересчёт на ApplicationIdle, стартовавший
        /// сразу после пересборки, может выполниться до появления первой строки и
        /// оставить компенсатор заголовка в устаревшем значении (issue #214) — так же,
        /// как в Linux/Avalonia выравнивание пересчитывается на событии подготовки
        /// контейнера строки.
        /// </summary>
        private void OnInfobaseRowGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid)
                return;
            // issue #255: при Recycling-виртуализации экземпляр сетки переиспользуется под новые
            // строки, поэтому порядок колонок, однажды применённый, сохраняется до сброса флага
            // в ApplyColumnOrder. Пропускаем ReorderGridColumns целиком для уже приведённых сеток,
            // чтобы на каждую материализацию строки не строились layout/массивы/List/HashSet и не
            // выполнялся обход детей (главный источник CPU и мусора gen2 при прокрутке списка).
            if (!GetColumnOrderApplied(grid))
            {
                ReorderGridColumns(grid, RowFirstDataColumn);
                SetColumnOrderApplied(grid, true);
            }
            grid.Tag = RowGridMarker;
            ApplyRowCompact(grid);
            // Намеренно НЕ вызываем QueueHeaderAlign здесь: обработчик Loaded срабатывает на
            // каждую реализацию строки при прокрутке/виртуализации, и перезапуск цикла
            // стабилизации на каждую строку держал компенсатор заголовка «в работе» всё время
            // скролла и усиливал рывки (issue #255). Стабилизатор HeaderAlignStabilizeStep
            // сам повторяется, пока первая строка ещё не появилась (!hadRows), поэтому
            // выравнивание после пересборки восстанавливается и без перезапуска здесь.
        }

        /// <summary>
        /// Обработчик Loaded сетки заголовка группы в шаблоне: применяет выбранный порядок
        /// колонок, чтобы команды группы оставались в колонке «Действия» на уровне строк баз.
        /// </summary>
        private void OnGroupRowGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid)
                return;
            // То же, что в OnInfobaseRowGrid_Loaded (issue #255): пропускаем пересборку колонок
            // для сеток, к которым порядок уже применён (Recycling переиспользует экземпляр).
            if (!GetColumnOrderApplied(grid))
            {
                ReorderGridColumns(grid, RowFirstDataColumn);
                SetColumnOrderApplied(grid, true);
            }
            grid.Tag = GroupGridMarker;
            ApplyRowCompact(grid);
        }

        /// <summary>
        /// Приводит вновь созданную строку (базы или группы) к текущей плотности. Строки
        /// дерева появляются позже применения режима к окну — при фоновой инициализации,
        /// виртуализации, прокрутке и пересборке дерева (поиск, сохранение свойств базы) —
        /// и без этого вызова оставались бы прежней плотности, «разъезжаясь» с заголовком
        /// (issue #214). Метод и сжимает, и разжимает: строку могло сжать, пока режим был
        /// включён, а потом её отсоединила виртуализация, и вернуться она могла уже после
        /// выключения. Повторные вызовы для той же строки безопасны: масштабирование идёт
        /// от сохранённых исходных значений, а не от текущих.
        /// </summary>
        private void ApplyRowCompact(Grid grid)
        {
            if (_viewModel is null)
                return;
            // В обычном режиме, пока ничего не сжималось, возвращать нечего — не ходим
            // по строке вовсе, чтобы не платить обходом на каждой прокрутке списка.
            if (!_viewModel.CompactMode && !ThemeManager.HasCompactMetrics)
                return;
            // Проходим по строке и в обычном режиме тоже. Строку могло сжать, пока режим был
            // включён, а потом её отсоединила виртуализация или пересборка дерева: обход окна
            // при выключении компактности такую строку не видит, и, вернувшись в видимую
            // область, она приходила сжатой (issue #214).
            // Сжимать надо строку целиком, от корня её шаблона, а не только внутреннюю
            // сетку: вертикальный отступ строки базы (Border Margin="0,1" в MainWindow.xaml)
            // задан на элементе, который сетке предок. Глобальный проход ApplyCompact его
            // сжимает, а обход от сетки не доставал, и строки, созданные позже (запуск с уже
            // включённым компактным режимом, пересборка дерева поиском), отличались
            // от строк после переключения тумблера (issue #214).
            ThemeManager.ApplyCompactTree(RowTemplateRoot(grid), _viewModel.CompactMode);
        }

        /// <summary>
        /// Возвращает корень шаблона строки: поднимается от сетки строки до элемента,
        /// который лежит непосредственно в <see cref="ContentPresenter"/> заголовка узла.
        /// Выше подниматься нельзя: там начинается шаблон контейнера с кнопкой разворота
        /// и <c>ItemsPresenter</c> дочерних строк, и обход захватывал бы соседние строки.
        /// Остановка на <see cref="TreeViewItem"/> — только страховка от бесконечного
        /// подъёма, если шаблон контейнера когда-нибудь останется без
        /// <see cref="ContentPresenter"/>; в нынешней разметке она недостижима.
        ///
        /// Метрики самого шаблона контейнера (кнопка разворота, обёртки строки) достаются
        /// только обходу всего окна. Сегодня это безразлично: отступы там либо нулевые,
        /// либо привязанные. Если в шаблон контейнера добавят ненулевой отступ константой,
        /// расхождение между путями вернётся, и его придётся учесть здесь.
        /// </summary>
        private static DependencyObject RowTemplateRoot(Grid grid)
        {
            DependencyObject current = grid;
            while (true)
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (parent is null or ContentPresenter or TreeViewItem)
                    return current;
                current = parent;
            }
        }

        /// <summary>
        /// Обработчик раскрытия/сворачивания узла дерева (issue #119). Раскрытие/сворачивание
        /// группы меняет глубину первой видимой базы, поэтому компенсатор сдвига заголовка
        /// (HeaderOffsetColumn) устаревает и колонки «уезжают» относительно содержимого.
        /// Пересчитывает выравнивание заголовка с данными после завершения компоновки.
        /// </summary>
        private void OnMainTree_GroupExpansionChanged(object sender, RoutedEventArgs e)
        {
            QueueHeaderAlign();
        }

        // Признак того, что в очереди диспетчера уже стоит пересчёт выравнивания заголовка.
        // Нужен, чтобы многие события за короткое время (материализация/рециклинг десятков
        // строк дерева при пересборке, прокрутке и поиске) склеивались в один пересчёт за
        // проход — как в Linux/Avalonia (QueueHeaderAlign).
        private bool _headerAlignQueued;
        // Счётчик итераций стабилизации текущего пересчёта (защита от бесконечного цикла).
        private int _headerAlignStabilizeCount;
        // Максимум итераций стабилизации, пока компенсатор не перестанет меняться.
        private const int HeaderAlignMaxStabilize = 8;

        /// <summary>
        /// Ставит пересчёт выравнивания заголовка (<see cref="AlignHeaderToData"/>) в очередь
        /// диспетчера на приоритет ApplicationIdle и зацикливает его до стабилизации значения
        /// колонки-компенсатора (см. <see cref="HeaderAlignStabilizeStep"/>). Повторные вызовы
        /// до выполнения объединяются в один пересчёт, чтобы частые события (появление каждой
        /// строки дерева, изменение размера списка) не вызывали лишние полные пересчёты.
        /// Выполнение на ApplicationIdle гарантирует, что раскладка уже завершена и
        /// виртуализированные контейнеры строк материализованы, — в отличие от Loaded, на
        /// котором они достраиваются уже после (issue #214).
        /// </summary>
        private void QueueHeaderAlign()
        {
            if (_headerAlignQueued)
                return;
            _headerAlignQueued = true;
            _headerAlignStabilizeCount = 0;
            Dispatcher.BeginInvoke(new Action(HeaderAlignStabilizeStep),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Один шаг стабилизации выравнивания заголовка: выполняет <see cref="AlignHeaderToData"/>
        /// и, пока значение компенсатора продолжает меняться либо строки ещё не материализованы
        /// (виртуализация достраивает их в проходе разметки после события), повторяет проверку
        /// на ApplicationIdle. Так фиксируется итоговое положение, а не промежуточное, по которому
        /// иконки заголовка «разъезжаются» относительно строк и требуют повторного переключения
        /// тумблера (issue #214). Число итераций ограничено как защита от бесконечного цикла.
        /// </summary>
        private void HeaderAlignStabilizeStep()
        {
            _headerAlignQueued = false;
            var before = HeaderOffsetColumn?.Width.Value ?? 0;
            var hadRows = FindFirstInfobaseItem(MainTree) is not null;
            AlignHeaderToData();
            var after = HeaderOffsetColumn?.Width.Value ?? 0;

            var changed = Math.Abs(after - before) > 0.5;
            if ((changed || !hadRows) && _headerAlignStabilizeCount++ < HeaderAlignMaxStabilize)
            {
                _headerAlignQueued = true;
                Dispatcher.BeginInvoke(new Action(HeaderAlignStabilizeStep),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        /// <summary>
        /// Подстраивает ширину колонки-компенсатора заголовка (HeaderOffsetColumn) так,
        /// чтобы первая колонка данных заголовка точно совпадала по горизонтали с первой
        /// колонкой данных строки базы. Строка базы и заголовок имеют одинаковый набор
        /// ведущих колонок, поэтому колонки данных всех строк (которые не смещаются
        /// отступами вложенности) оказываются на одной линии с заголовками.
        /// </summary>
        private void AlignHeaderToData()
        {
            if (HeaderGrid is null || HeaderOffsetColumn is null || MainTree is null)
                return;

            var item = FindFirstInfobaseItem(MainTree);
            if (item is null)
                return;

            var rowGrid = FindGridByMarker(item, RowGridMarker);
            if (rowGrid is null)
                return;

            // Фактическая ширина строк уже известна (первая строка материализована):
            // обновляем кэш минимума контента до синхронизации ширины заголовка, чтобы
            // полоса дотягивала до конца длинных названий, а не только до суммы колонок
            // (issue #309). Редкий путь — стабилизация выравнивания на ApplicationIdle.
            UpdateTreeMinWidthContent(rowGrid);

            // Позиция первой колонки данных строки базы (отсчитывается от левого края сетки).
            double rowStart = 0;
            for (var i = 0; i < RowFirstDataColumn; i++)
                rowStart += rowGrid.ColumnDefinitions[i].ActualWidth;
            var rowOrigin = rowGrid.TransformToAncestor(this).Transform(new Point(0, 0)).X;

            // Позиция первой колонки данных заголовка без учёта текущей ширины компенсатора Offset.
            double headerStart = 0;
            for (var i = 0; i < HeaderFirstDataColumn; i++)
            {
                if (!ReferenceEquals(HeaderGrid.ColumnDefinitions[i], HeaderOffsetColumn))
                    headerStart += HeaderGrid.ColumnDefinitions[i].ActualWidth;
            }
            var headerOrigin = HeaderGrid.TransformToAncestor(this).Transform(new Point(0, 0)).X;

            var offset = Math.Max(0, (rowOrigin + rowStart) - (headerOrigin + headerStart));
            if (Math.Abs(offset - HeaderOffsetColumn.Width.Value) > 0.5)
            {
                // Компенсатор управляется только этим методом — исключаем его из компактизации,
                // чтобы повторное применение компакт-режима не масштабировало уже выставленную
                // ширину и не «разъезжало» строки по горизонтали (issue #214).
                Themes.ThemeManager.ForgetCompactWidth(HeaderOffsetColumn);
                HeaderOffsetColumn.Width = new GridLength(offset);
            }

            SyncHeaderWidthWithList();
        }

        /// <summary>
        /// Выравнивает ширину сетки заголовка с контентом списка, чтобы горизонтальная
        /// прокрутка «до конца» не разъезжала колонки заголовка и строк.
        /// Важно: ширина заголовка не должна включать ширину вертикальной полосы прокрутки
        /// (sbw), иначе гибкая колонка «Название» в заголовке растянется шире, чем в данных,
        /// и фиксированные колонки данных окажутся смещёнными влево относительно заголовков.
        /// </summary>
        private void SyncHeaderWidthWithList()
        {
            if (HeaderGrid is null || MainTree is null)
                return;

            // Жёсткий минимум области списка = сумма ширин всех колонок (как в Linux).
            // Благодаря точной границе горизонтальная полоса появляется только когда
            // колонки реально не помещаются, а не «на волосок» раньше (issue про последнюю
            // пустую колонку). Пересчитывается при каждой синхронизации ширины заголовка.
            UpdateTreeMinWidth();

            var treeScroll = GetTreeScrollViewer();
            double extent = MainTree.ActualWidth;
            double viewport = MainTree.ActualWidth;
            if (treeScroll is not null)
            {
                extent = Math.Max(treeScroll.ExtentWidth, treeScroll.ViewportWidth);
                viewport = treeScroll.ViewportWidth;
            }

            // Не добавляем sbw: заголовок и данные должны иметь одинаковую общую ширину,
            // чтобы колонки данных совпадали с колонками заголовка.
            double target = Math.Max(extent, viewport);
            if (target > 0)
                HeaderGrid.Width = target;
        }

        /// <summary>
        /// Ищет первый реально созданный (видимый) элемент дерева с базой.
        /// </summary>
        private static TreeViewItem? FindFirstInfobaseItem(DependencyObject parent)
        {
            // Строка узла «Закреплённые» несёт обёртку PinnedInfobaseItem
            // (уникальные данные, issue #314) — распознаём и её как строку базы.
            if (parent is TreeViewItem tvi && UnwrapInfobase(tvi.DataContext) is not null)
                return tvi;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var result = FindFirstInfobaseItem(VisualTreeHelper.GetChild(parent, i));
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Находит текстовый элемент названия базы (x:Name=NameText) в строке.
        /// </summary>
        private static TextBlock? FindNameCell(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock tb && tb.Name == "NameText")
                    return tb;

                var result = FindNameCell(child);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Применяет сохранённые ширины колонок списка баз.
        /// Ширины уже загружены в модель (VersionColumnWidth и т.д.), а колонки заголовка
        /// и строки данных привязаны к ним через ColumnVisibilityConverter, поэтому ручная
        /// установка Width не требуется и лишь перебивала бы binding, рассинхронизируя
        /// заголовок с данными.
        /// </summary>
        private void ApplySavedColumnWidths()
        {
            // Колонка «Название» — гибкая (*), фиксированную ширину не задаём.
            // Остальные колонки применяют сохранённые ширины автоматически через binding.
        }

        /// <summary>
        /// Минимальная ширина гибкой колонки «Название» (*): на неё нельзя схлопываться,
        /// чтобы сумма колонок оставалась осмысленной при расчёте минимальной ширины списка.
        /// </summary>
        private const double NameColumnMinWidth = 220;

        /// <summary>
        /// Кэш фактической желаемой ширины строк дерева (issue #309). Сумма ширин колонок —
        /// нижний предел минимума контента, но реальная строка может быть шире: блок имени
        /// (горизонтальный StackPanel, ColumnSpan=5 в MainWindow.xaml) меряет NameText без
        /// ограничения ширины, и длинное название делает строку фактически шире суммы колонок.
        /// Внутренний ScrollViewer дерева (CanContentScroll) меряет панель вьюпортной шириной,
        /// поэтому ExtentWidth сам не растёт под контент, и минимум приходится досчитывать
        /// замером первых строк с бесконечной шириной. Обновляется только в редких точках
        /// (стабилизация выравнивания, применение ширин, смена верхней строки при прокрутке),
        /// чтобы не нагружать горячий путь <see cref="UpdateTreeMinWidth"/> (анти-регресс #255).
        /// Ноль означает «строки ещё нет или контент уже покрывается суммой колонок».
        /// </summary>
        private double _treeMinWidthContent;

        /// <summary>
        /// Флаг очереди пересчёта минимальной ширины списка (дебаунс, issue #309):
        /// ручное перетаскивание разделителя колонок генерирует MouseMove десятки раз
        /// в секунду, и полный пересчёт на каждое движение нагружал бы раскладку.
        /// Один отложенный вызов на проход диспетчера собирает все движения кадра.
        /// </summary>
        private bool _treeMinWidthQueued;

        /// <summary>
        /// Последняя установленная минимальная ширина контента (issue #309). Обход
        /// материализованных строк для дублирования MinWidth выполняется только при
        /// фактическом изменении значения, а не на каждый ScrollChanged.
        /// </summary>
        private double _lastTreeMinWidth = -1;

        /// <summary>Данные верхней видимой строки при последнем замере <see cref="_treeMinWidthContent"/>.</summary>
        private object? _treeMinWidthAnchorData;

        /// <summary>
        /// Используется ли обычный StackPanel вместо VirtualizingStackPanel (issue #309).
        /// VirtualizingStackPanel при CanContentScroll=True меряет детей вьюпортной шириной
        /// и «срезает» горизонтальный экстент — ExtentWidth не дотягивает до суммы колонок,
        /// полоса короткая, последние колонки недостижимы. Обычный StackPanel не реализует
        /// IScrollInfo, и ScrollViewer прокручивает контент по фактическому DesiredSize,
        /// поэтому горизонтальная полоса становится честной. Цена — потеря виртуализации
        /// на время, пока полоса реально нужна (колонки не помещаются).
        /// </summary>
        private bool _treeUsePlainStackPanel;

        /// <summary>
        /// Максимальное число строк, при котором допустим обычный StackPanel: на очень
        /// больших списках (тысячи баз) потеря виртуализации дороже горизонтальной
        /// прокрутки, и мы остаёмся на виртуализирующей панели (известное ограничение).
        /// </summary>
        private const int MaxRowsForPlainStackPanel = 4000;

        /// <summary>
        /// Минимальная ширина колонки «Действия»: совпадает с MinWidth=120, заданной трём
        /// ColumnDefinition этой колонки (заголовок, группа, база) в MainWindow.xaml, чтобы
        /// три кнопки-иконки (Запуск, Конфигуратор, Очистить кеш) оставались доступными.
        /// </summary>
        private const double ActionsColumnMinWidth = 120;

        /// <summary>
        /// Задаёт списку точную минимальную ширину, равную сумме ширин всех колонок
        /// заголовка (гибкое «Название» — по своему минимуму) вместе с ведущими отступами
        /// (колонки кнопок групп, компенсатор сдвига дерева, избранное, закрепление).
        /// Аналог <c>UpdateListMinWidth</c> из Linux/Avalonia: благодаря жёсткой границе
        /// горизонтальная прокрутка появляется ровно тогда, когда колонки реально не
        /// помещаются по ширине, а не «на волосок» раньше из-за округления последней
        /// (пустой) колонки «Конфигурация».
        /// </summary>
        private void UpdateTreeMinWidth()
        {
            if (MainTree is null || HeaderGrid is null)
                return;

            var defs = HeaderGrid.ColumnDefinitions;
            if (defs.Count <= HeaderFirstDataColumn)
                return;

            // «Название» стоит перед первой колонкой данных (HeaderFirstDataColumn):
            // 0–3 — ведущие, 4 — имя, 5+ — значения, включая «Действия». Тот же
            // порядок, что в Linux/Avalonia (MainWindow.Avalonia.Columns.cs).
            var nameIndex = HeaderFirstDataColumn - 1;

            var leading = new List<ListMinWidthCalculator.Column>(nameIndex);
            for (var i = 0; i < nameIndex; i++)
                leading.Add(HeaderColumnSpec(defs[i]));

            var nameDef = defs[nameIndex];
            var nameWidth = nameDef.Width.IsAbsolute ? nameDef.Width.Value : 0;

            var values = new List<ListMinWidthCalculator.Column>(defs.Count - HeaderFirstDataColumn);
            for (var i = HeaderFirstDataColumn; i < defs.Count; i++)
                values.Add(HeaderColumnSpec(defs[i]));

            // Сумма ВСЕХ видимых колонок заголовка: скрытая колонка получает нулевую
            // абсолютную ширину (конвертер ColumnVis) и места не занимает (issue #255).
            // Расчёт общий с Linux/Avalonia (ListMinWidthCalculator), чтобы сумма не
            // расходилась с фактическим набором колонок (issue #309).
            // К сумме добавляется фактическая желаемая ширина строк дерева
            // (_treeMinWidthContent, issue #309): длинное название в горизонтальном
            // StackPanel раздвигает строку шире суммы колонок, и прокрутка обязана
            // дотягивать до конца контента, а не до «расчётной» границы.
            var total = ListMinWidthCalculator.Compute(
                nameWidth, NameColumnMinWidth, leading, 0, values, _treeMinWidthContent);

            // Девятая попытка (issue #309): расчётная сумма (total) не учитывает фактическую
            // ширину звёздной колонки «Название» (для гибкой колонки nameWidth=0), поэтому
            // при растянутой «Название» последние колонки выталкиваются за правый край, а
            // минимум контента остаётся короче фактической ширины строк — полоса есть
            // (scrollable ~3 px в логе пользователя), но до конца колонок не дотягивает.
            // Берём фактическую сумму колонок заголовка, когда заголовок реально не
            // помещается; иначе полоса появлялась бы «на волосок» из-за округления.
            var headerSumNow = defs.Sum(d => d.ActualWidth);
            var scrollViewerNow = GetTreeScrollViewer();
            var viewportNow = scrollViewerNow?.ViewportWidth ?? MainTree.ActualWidth;
            var effective = headerSumNow > viewportNow + 1 ? Math.Max(total, headerSumNow) : total;

            // Эксперимент B (issue #309, 10-я попытка): последние колонки могут быть
            // недостижимыми, потому что их правый край уходит ПОД вертикальный скроллбар
            // внутреннего ScrollViewer дерева (в логе пользователя hdrRight ≈ 1102,8 при
            // viewport ≈ 1094,4). Когда вертикальная прокрутка активна и сумма колонок
            // вместе со скроллбаром не помещается во вьюпорт — расширяем минимум на ширину
            // полосы (+2 px запаса), чтобы прокрутка дотягивала до конца последней колонки.
            // Полоса не добавляется, когда контент помещается с запасом (анти-регресс #255).
            if (scrollViewerNow?.ComputedVerticalScrollBarVisibility == System.Windows.Visibility.Visible)
            {
                var sbw = SystemParameters.VerticalScrollBarWidth;
                if (sbw > 0 && headerSumNow + sbw > viewportNow + 1)
                    effective = Math.Max(effective, headerSumNow + sbw);
            }

            // Минимум задаём КОНТЕНТУ прокрутки (внутреннему ScrollContentPresenter дерева),
            // а не самому MainTree: у дерева собственный внутренний ScrollViewer, и MinWidth
            // на контроле лишь растянул бы область просмотра, а не заставил бы контент
            // переполняться. Задание минимума контенту даёт жёсткую границу (как в Linux):
            // горизонтальная полоса появляется ровно тогда, когда сумма колонок превышает
            // доступную ширину, а не «на волосок» раньше из-за округления последней
            // (пустой) колонки «Конфигурация».
            var presenter = GetTreeScrollContentPresenter();
            if (presenter is not null)
                presenter.MinWidth = effective;

            // Внутренний ScrollViewer TreeView работает с CanContentScroll=True и пиксельной
            // виртуализацией (MainWindow.xaml): виртуализирующая панель меряется ВЬЮПОРТНОЙ
            // шириной, поэтому MinWidth одного ScrollContentPresenter может не расширять
            // ExtentWidth — горизонтальная полоса остаётся короче суммы колонок, и последние
            // колонки («Конфигурация»/«№ релиза») недостижимы (issue #309, седьмая попытка).
            // Поэтому жёсткий минимум дублируется на корневые Grid материализованных строк:
            // DesiredSize панели становится >= effective, ExtentWidth дотягивает до конца колонок.
            // Обход выполняется только при фактическом изменении значения (не на каждый
            // ScrollChanged) — при пиксельной виртуализации новые строки перекрываются
            // ближайшим пересчётом (ExtentWidthChange обновляет минимум в OnTreeScroll).
            if (Math.Abs(effective - _lastTreeMinWidth) > 0.5)
            {
                _lastTreeMinWidth = effective;
                foreach (var row in GetVisibleTreeViewItems())
                {
                    var rowGrid = FindGridByMarker(row, RowGridMarker)
                                  ?? FindGridByMarker(row, GroupGridMarker);
                    if (rowGrid is not null)
                        rowGrid.MinWidth = effective;
                }

                // Инструментальный лог (issue #309): фактические значения в журнале приложения,
                // чтобы следующая итерация правки опиралась на данные, а не на предположения.
                LogColumnsDiagnostics(effective, presenter);
            }

            // Восьмая попытка (issue #309): VirtualizingStackPanel при CanContentScroll=True
            // измеряет строки вьюпортной шириной, и ExtentWidth не растёт до суммы колонок —
            // даже с MinWidth на строках полоса остаётся короткой (подтверждается логом
            // CM_COLUMNS: extent < total). Переключаем ItemsPanel на обычный StackPanel,
            // как только колонки реально не помещаются: ScrollViewer начинает вести
            // прокрутку по фактической ширине контента (DesiredSize), и последние колонки
            // становятся достижимыми. Обратное переключение — когда полоса не нужна.
            ApplyTreePanelStrategy(effective);
        }

        /// <summary>
        /// Выбирает панель списка: обычный <see cref="System.Windows.Controls.StackPanel"/>
        /// при необходимости горизонтальной прокрутки (честный ExtentWidth), иначе
        /// <see cref="VirtualizingStackPanel"/> (виртуализация). Повторная установка
        /// ItemsPanelTemplate каждый раз пересоздавала бы контейнеры — применяем только
        /// при фактической смене стратегии (issue #309, восьмая попытка).
        /// </summary>
        private void ApplyTreePanelStrategy(double total)
        {
            var treeScroll = GetTreeScrollViewer();
            double viewport = treeScroll?.ViewportWidth ?? MainTree.ActualWidth;
            var needHorizontal = total > viewport + 1;

            // На очень больших списках жертвовать виртуализацией нельзя —
            // остаёмся на VirtualizingStackPanel (колонки могут быть недостижимы).
            var wantPlain = needHorizontal && CountVisibleTreeItems() <= MaxRowsForPlainStackPanel;
            if (wantPlain == _treeUsePlainStackPanel)
                return;

            try
            {
                var template = new ItemsPanelTemplate(new FrameworkElementFactory(
                    wantPlain ? typeof(System.Windows.Controls.StackPanel) : typeof(VirtualizingStackPanel)));
                MainTree.ItemsPanel = template;
                _treeUsePlainStackPanel = wantPlain;

                // У виртуализирующей панели возвращаем режимы из разметки
                // (MainWindow.xaml:1362-1364): Recycling + пиксельный скролл.
                if (!wantPlain)
                {
                    VirtualizingPanel.SetIsVirtualizing(MainTree, true);
                    VirtualizingPanel.SetVirtualizationMode(MainTree, VirtualizationMode.Recycling);
                    VirtualizingPanel.SetScrollUnit(MainTree, ScrollUnit.Pixel);
                }

                MainTree.InvalidateMeasure();
                LogColumnsDiagnostics(total, GetTreeScrollContentPresenter());
            }
            catch
            {
                // Сбой переключения не должен ломать раскладку: остаёмся на прежней панели.
            }
        }

        /// <summary>Число строк дерева (группы + базы), приблизительно — по корням.</summary>
        private int CountVisibleTreeItems()
        {
            if (_viewModel is null)
                return 0;
            try
            {
                var count = 0;
                foreach (var root in _viewModel.GroupNodes)
                {
                    count++;
                    count += root.TotalInfobaseCount;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Пишет в журнал приложения фактические значения расчёта минимальной ширины
        /// (issue #309): сумму колонок заголовка, замер контента строк, MinWidth
        /// презентера, viewport/extent внутреннего ScrollViewer дерева и DPI окна.
        /// Выключено по умолчанию — включается постоянным логированием только при
        /// изменении рассчитанной суммы (редкий путь).
        /// </summary>
        private void LogColumnsDiagnostics(double total, ScrollContentPresenter? presenter)
        {
            try
            {
                var treeScroll = GetTreeScrollViewer();
                var defs = HeaderGrid?.ColumnDefinitions;
                var sumActual = defs is null ? 0 : defs.Sum(d => d.ActualWidth);
                var viewport = treeScroll?.ViewportWidth ?? 0;
                var extent = treeScroll?.ExtentWidth ?? 0;
                var scrollable = treeScroll?.ScrollableWidth ?? 0;
                var vSb = treeScroll?.ComputedVerticalScrollBarVisibility ?? System.Windows.Visibility.Collapsed;
                var dpi = VisualTreeHelper.GetDpi(this);

                // Девятая попытка (issue #309): фактические ширины колонок заголовка.
                // Растянутая звёздная «Название» может выталкивать последние колонки за
                // правый край, и сумма колонок (sumActualHeader) оказывается больше
                // расчётной (total) — это прямое объяснение обрезания при scrollable ~0.
                var headerCols = defs is null ? string.Empty
                    : string.Join(",", defs.Select((d, i) => $"{i}:{d.ActualWidth:F1}"));
                var headerOrigin = 0d;
                var headerRight = 0d;
                if (HeaderGrid is not null)
                {
                    headerOrigin = HeaderGrid.TransformToAncestor(this).Transform(new Point(0, 0)).X;
                    headerRight = headerOrigin + sumActual;
                }

                // Эксперимент A: сравнение фактической ширины строк с заголовком — разные
                // сетки могут дать разный остаток звёздной «Название», и правая граница
                // строки уходит дальше правой границы заголовка (колонки данных обрезаются
                // заголовком или наоборот).
                var (rowSum, rowOrigin, rowRight, rowCols, rowCount) = MeasureFirstRowDiagnostics();

                // Эксперимент B: ширина окна и вертикального скроллбара — вьюпорт
                // внутреннего ScrollViewer дерева может быть меньше доступной ширины
                // списка на ширину вертикальной полосы (последняя колонка «не влезает»
                // именно из-за неё, а не из-за ширины заголовка).
                var sbw = SystemParameters.VerticalScrollBarWidth;
                var winW = ActualWidth;
                var winH = ActualHeight;
                var treeW = MainTree?.ActualWidth ?? 0;
                var headerW = HeaderGrid?.ActualWidth ?? 0;

                AppServices.GetRequiredService<IAppLogger>().Info(
                    $"CM_COLUMNS: total={total:F1}, sumActualHeader={sumActual:F1}, " +
                    $"content={_treeMinWidthContent:F1}, presenterMin={presenter?.MinWidth ?? 0:F1}, " +
                    $"viewport={viewport:F1}, extent={extent:F1}, scrollable={scrollable:F1}, vSb={vSb}, " +
                    $"panel={(_treeUsePlainStackPanel ? "StackPanel" : "VirtualizingStackPanel")}, " +
                    $"dpiScale={dpi.DpiScaleX:F2}, win={winW:F1}x{winH:F1}, treeW={treeW:F1}, " +
                    $"headerW={headerW:F1}, sbw={sbw:F1}, viewportSbw={viewport + sbw:F1}, " +
                    $"hdrOrigin={headerOrigin:F1}, hdrRight={headerRight:F1}, " +
                    $"rows={rowCount}, rowSum={rowSum:F1}, rowOrigin={rowOrigin:F1}, rowRight={rowRight:F1}, " +
                    $"cols=[{headerCols}], rowCols=[{rowCols}]");
            }
            catch
            {
                // Диагностика не должна ломать раскладку.
            }
        }

        /// <summary>
        /// Замер фактической ширины первой материализованной строки базы (или заголовка
        /// группы, если базы ещё не созданы): сумма <c>ActualWidth</c> колонок сетки строки,
        /// её левая координата в окне и правый край последней колонки (issue #309,
        /// эксперимент A — сравнение строк с заголовком). Возвращает нули и пустую строку,
        /// если ни одна строка ещё не материализована либо обход визуального дерева не
        /// удался. Диагностика не должна падать в горячем пути.
        /// </summary>
        private (double Sum, double OriginX, double RightEdgeX, string Columns, int Count) MeasureFirstRowDiagnostics()
        {
            if (MainTree is null)
                return (0, 0, 0, string.Empty, 0);
            try
            {
                var item = FindFirstInfobaseItem(MainTree);
                var grid = item is null ? null : FindGridByMarker(item, RowGridMarker)
                           ?? FindGridByMarker(item, GroupGridMarker);
                if (grid is null || grid.ColumnDefinitions.Count == 0)
                    return (0, 0, 0, string.Empty, 0);

                var sum = grid.ColumnDefinitions.Sum(d => d.ActualWidth);
                var origin = grid.TransformToAncestor(this).Transform(new Point(0, 0)).X;
                var cols = string.Join(",", grid.ColumnDefinitions
                    .Select((d, i) => $"{i}:{d.ActualWidth:F1}"));
                return (sum, origin, origin + sum, cols, grid.ColumnDefinitions.Count);
            }
            catch
            {
                return (0, 0, 0, string.Empty, 0);
            }
        }

        /// <summary>
        /// Пересчитывает <see cref="_treeMinWidthContent"/> замером первых видимых строк
        /// дерева с бесконечной шириной (issue #309). Горизонтальный StackPanel блока имени
        /// (MainWindow.xaml, ColumnSpan=5) меряет NameText без ограничения ширины, поэтому
        /// при бесконечном constraint Grid строки отдаёт звёздной колонке «Название»
        /// желание контента — DesiredSize совпадает с фактической шириной строки, а не
        /// с суммой колонок. Внутренний ScrollViewer дерева сам этого не учитывает
        /// (CanContentScroll меряет панель вьюпортной шириной), поэтому минимум контента
        /// досчитывается здесь. Вызов дороже обычного пересчёта суммы (замер до трёх
        /// строк), поэтому выполняется только в редких точках — стабилизация выравнивания
        /// (AlignHeaderToData), применение ширин/порядка колонок и смена верхней строки
        /// при прокрутке, а не в горячем пути <see cref="UpdateTreeMinWidth"/> (#255).
        /// </summary>
        private void UpdateTreeMinWidthContent(Grid? knownRowGrid = null)
        {
            _treeMinWidthContent = 0;
            if (knownRowGrid is not null)
                MeasureRow(knownRowGrid);

            if (MainTree is null)
                return;

            var measured = knownRowGrid is null ? 0 : 1;
            foreach (var row in GetVisibleTreeViewItems())
            {
                // Лимит поднят с 3 до 12 (issue #309, седьмая попытка): широкая строка ниже
                // первых трёх не учитывалась, и полоса не дотягивала до конца контента.
                // Замер по 12 строкам покрывает видимую область при типичных высотах строк
                // и остаётся дешёвым (вызов не в горячем пути прокрутки).
                if (measured >= 12)
                    break;
                var grid = FindGridByMarker(row, RowGridMarker)
                           ?? FindGridByMarker(row, GroupGridMarker);
                if (grid is null || ReferenceEquals(grid, knownRowGrid))
                    continue;
                MeasureRow(grid);
                measured++;
            }

            void MeasureRow(Grid g)
            {
                // Замер «как хотела бы строка без ограничений»: ширина контента при
                // бесконечном constraint (текст названия не обрезается). Высота берётся
                // текущая, чтобы не ломать вертикальную раскладку строки.
                g.Measure(new Size(double.PositiveInfinity, g.ActualHeight));
                if (g.DesiredSize.Width > _treeMinWidthContent)
                    _treeMinWidthContent = g.DesiredSize.Width;
            }
        }

        /// <summary>
        /// Ставит пересчёт минимальной ширины списка в очередь диспетчера (дебаунс,
        /// issue #309): вызывается из MouseMove ручного перетаскивания разделителя
        /// колонок и исполняется один раз на проход (Background), когда binding уже
        /// применил новую ширину колонок к заголовку и строкам. Так горизонтальная
        /// полоса прокрутки расширяется/сужается ЖИВЬЁМ во время перетаскивания,
        /// а не только после отпускания мыши (раньше полный пересчёт выполнялся лишь
        /// в MouseUp). Дорогой замер фактической ширины строк
        /// (<see cref="UpdateTreeMinWidthContent"/>) остаётся только на завершении
        /// перетаскивания — горячий путь не нагружается (анти-регресс #255).
        /// </summary>
        private void QueueTreeMinWidthRefresh()
        {
            if (_treeMinWidthQueued)
                return;
            _treeMinWidthQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _treeMinWidthQueued = false;
                UpdateTreeMinWidth();
                SyncHeaderWidthWithList();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Спецификация колонки заголовка для расчёта минимальной ширины.</summary>
        private static ListMinWidthCalculator.Column HeaderColumnSpec(ColumnDefinition d)
            => new(
                d.Width.IsAbsolute ? d.Width.Value : d.MinWidth,
                d.Width.IsAbsolute && d.Width.Value > 0);

        /// <summary>
        /// Внутренний ScrollContentPresenter шаблона TreeView — контент его ScrollViewer.
        /// </summary>
        private ScrollContentPresenter? GetTreeScrollContentPresenter()
        {
            // Кэш презентера рядом с _treeScrollViewer (issue #255): полный обход визуального
            // дерева FindVisualChild на каждый вызов при прокрутке добавлял лишнюю нагрузку.
            // Презентер находится во вложенном шаблоне внутреннего ScrollViewer дерева, поэтому
            // захват в OnApplyTemplate самого TreeView (GetTemplateChild) его не достаёт — кэшируем
            // здесь, в главном окне, и сбрасываем при Unloaded вместе с _treeScrollViewer.
            if (_treeScrollContentPresenter is not null)
                return _treeScrollContentPresenter;
            var treeScroll = GetTreeScrollViewer();
            _treeScrollContentPresenter = treeScroll is null
                ? null
                : FindVisualChild<ScrollContentPresenter>(treeScroll);
            return _treeScrollContentPresenter;
        }

        /// <summary>
        /// Определяет колонку, ширину которой меняет данный разделитель.
        /// Разделитель расположен на правом краю своей колонки (Grid.Column=N),
        /// поэтому он меняет ширину колонки с тем же индексом N.
        /// </summary>
        private ColumnDefinition? GetSplitterTargetColumn(object sender)
        {
            if (ReferenceEquals(sender, NameSplitter))
                return NameColumn;
            if (ReferenceEquals(sender, VersionSplitter))
                return VersionColumn;
            if (ReferenceEquals(sender, ConfigurationSplitter))
                return ConfigurationColumn;
            if (ReferenceEquals(sender, ConfigurationVersionSplitter))
                return ConfigurationVersionColumn;
            if (ReferenceEquals(sender, LaunchModeSplitter))
                return LaunchModeColumn;
            if (ReferenceEquals(sender, ActionsSplitter))
                return ActionsColumn;
            if (ReferenceEquals(sender, ServerSplitter))
                return ServerColumn;
            if (ReferenceEquals(sender, LastLaunchSplitter))
                return LastLaunchColumn;
            if (ReferenceEquals(sender, SizeSplitter))
                return SizeColumn;
            if (ReferenceEquals(sender, ModifiedSplitter))
                return ModifiedColumn;
            if (ReferenceEquals(sender, LastBackupSplitter))
                return LastBackupColumn;
            return null;
        }

        /// <summary>
        /// Начинает перетаскивание разделителя: захватывает мышь и запоминает стартовые значения.
        /// </summary>
        private void OnColumnResize_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var column = GetSplitterTargetColumn(sender);
            if (column is null)
                return;

            _resizeColumn = column;
            _resizeStartWidth = column.ActualWidth;
            _resizeStartMouse = e.GetPosition(this);

            if (sender is UIElement element)
                element.CaptureMouse();

            e.Handled = true;
        }

        /// <summary>
        /// Меняет ширину только целевой колонки при движении мыши.
        /// Ширина записывается в модель (VersionColumnWidth и т.д.), к которой привязаны
        /// и колонка заголовка, и колонки данных — поэтому заголовок и данные синхронно
        /// изменяются. Прямая установка Width перебивала бы binding и рассинхронизировала их.
        /// </summary>
        private void OnColumnResize_MouseMove(object sender, MouseEventArgs e)
        {
            if (_resizeColumn is null || sender is not UIElement element || !element.IsMouseCaptured)
                return;

            var current = e.GetPosition(this);
            var delta = current.X - _resizeStartMouse.X;

            var newWidth = _resizeStartWidth + delta;
            if (ReferenceEquals(_resizeColumn, ActionsColumn) && newWidth < ActionsColumnMinWidth)
                newWidth = ActionsColumnMinWidth;
            else if (newWidth < 40)
                newWidth = 40;

            if (ReferenceEquals(_resizeColumn, SizeColumn))
            {
                // SizeColumnWidth имеет публичный сеттер и авто-сохраняется при изменении.
                _viewModel.SizeColumnWidth = newWidth;
                QueueTreeMinWidthRefresh();
                return;
            }
            if (ReferenceEquals(_resizeColumn, ModifiedColumn))
            {
                // ModifiedColumnWidth имеет публичный сеттер и авто-сохраняется при изменении.
                _viewModel.ModifiedColumnWidth = newWidth;
                QueueTreeMinWidthRefresh();
                return;
            }
            if (ReferenceEquals(_resizeColumn, LastBackupColumn))
            {
                // LastBackupColumnWidth имеет публичный сеттер и авто-сохраняется при изменении.
                _viewModel.LastBackupColumnWidth = newWidth;
                QueueTreeMinWidthRefresh();
                return;
            }

            _viewModel.UpdateColumnWidths(
                ReferenceEquals(_resizeColumn, NameColumn) ? newWidth : NameColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, VersionColumn) ? newWidth : VersionColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ConfigurationColumn) ? newWidth : ConfigurationColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ConfigurationVersionColumn) ? newWidth : ConfigurationVersionColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, LaunchModeColumn) ? newWidth : LaunchModeColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ServerColumn) ? newWidth : ServerColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, LastLaunchColumn) ? newWidth : LastLaunchColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ActionsColumn) ? newWidth : ActionsColumn?.ActualWidth ?? 0);

            // Живой пересчёт минимума во время перетаскивания (дебаунс, issue #309):
            // полоса горизонтальной прокрутки расширяется сразу, пока колонку тянут
            // за край видимой области, а не только после отпускания мыши.
            QueueTreeMinWidthRefresh();
        }

        /// <summary>
        /// Завершает перетаскивание разделителя и сохраняет ширины колонок.
        /// </summary>
        private void OnColumnResize_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement element)
                element.ReleaseMouseCapture();

            if (_resizeColumn is not null)
            {
                _viewModel.SaveColumnWidths(
                    NameColumn?.ActualWidth ?? 0,
                    VersionColumn?.ActualWidth ?? 0,
                    ConfigurationColumn?.ActualWidth ?? 0,
                    ConfigurationVersionColumn?.ActualWidth ?? 0,
                    LaunchModeColumn?.ActualWidth ?? 0,
                    ServerColumn?.ActualWidth ?? 0,
                    LastLaunchColumn?.ActualWidth ?? 0,
                    ActionsColumn?.ActualWidth ?? 0);
                // Новая ширина колонки меняет желаемую ширину строки (звёздная «Название»
                // при бесконечном замере получает остаток) — пересчитываем фактическую
                // ширину контента до синхронизации минимума (issue #309).
                UpdateTreeMinWidthContent();
                SyncHeaderWidthWithList();
            }

            _resizeColumn = null;
            e.Handled = true;
        }

    }
}
#endif
