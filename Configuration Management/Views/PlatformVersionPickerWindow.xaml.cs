#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора версии платформы 1С (как в стартере):
    /// фильтр Все / x32 / x64, сортировка A→Z / Z→A,
    /// дерево 8.3 → 8.3.27 → 8.3.27.2214 (x64).
    /// </summary>
    public partial class PlatformVersionPickerWindow : Window
    {
        private string _selectedVersion = string.Empty;
        private List<PlatformVersionInfo> _allInfos = new();
        private string _currentVersion = string.Empty;
        private bool _sortAscending = false; // по умолчанию — свежие версии сверху
        private string _archFilter = "all"; // all | x32 | x64
        private bool _isRestoringSelection; // перестроение дерева: гасим SelectionChanged(null) (#304)

        public PlatformVersionPickerWindow(IEnumerable<string> installedPlatformVersions, string currentVersion)
        {
            InitializeComponent();
            _currentVersion = currentVersion ?? "";

            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            _allInfos = PlatformVersionService.FindInstalledVersionInfos(extras);
            if (_allInfos.Count == 0 && installedPlatformVersions != null)
            {
                _allInfos = installedPlatformVersions
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => new PlatformVersionInfo { Display = s.Trim(), Path = "" })
                    .ToList();
            }
            else if (installedPlatformVersions != null)
            {
                var known = new HashSet<string>(_allInfos.Select(i => i.Display), StringComparer.OrdinalIgnoreCase);
                foreach (var s in installedPlatformVersions)
                {
                    if (string.IsNullOrWhiteSpace(s) || known.Contains(s.Trim())) continue;
                    _allInfos.Add(new PlatformVersionInfo { Display = s.Trim(), Path = "" });
                }
            }

            RefreshTree();
        }

        public string Result => _selectedVersion;

        private void RefreshTree()
        {
            // Дерево строится из полного списка версий, а фильтр разрядности скрывает
            // только листья: папки (линии/группы сборок) сохраняются, пока в линии есть
            // хоть один видимый вариант, — выбор «8.5.1» не перескакивает на линию 8.5
            // при переключении разрядности (issue #304).
            var tree = PlatformVersionService.BuildGroupedTree(_allInfos, _archFilter);
            if (_sortAscending)
                tree = ReverseTreeOrder(tree);

            // Во время перестроения SelectedItemChanged приходит с null (старый узел удалён
            // из Items): гасим его, чтобы не сбрасывать _selectedVersion до восстановления
            // выбора (issue #304).
            _isRestoringSelection = true;
            PlatformsTree.ItemsSource = tree;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Полностью разворачиваем дерево (линии → группы сборок → сборки), как в стартере
                    ExpandAll(PlatformsTree);
                    if (!string.IsNullOrWhiteSpace(_currentVersion))
                        SelectCurrent(_currentVersion);
                }
                finally
                {
                    _isRestoringSelection = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static List<PlatformVersionGroup> ReverseTreeOrder(List<PlatformVersionGroup> roots)
        {
            var list = roots.AsEnumerable().Reverse().ToList();
            foreach (var node in list)
                ReverseChildren(node);
            return list;
        }

        private static void ReverseChildren(PlatformVersionGroup node)
        {
            if (node.Children.Count == 0) return;
            node.Children = node.Children.AsEnumerable().Reverse().ToList();
            foreach (var c in node.Children)
                ReverseChildren(c);
        }

        private static void ExpandAll(ItemsControl parent)
        {
            parent.UpdateLayout();
            foreach (var item in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
                {
                    tvi.IsExpanded = true;
                    tvi.UpdateLayout();
                    ExpandAll(tvi);
                }
            }
        }

        private void OnArchFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (FilterX32.IsChecked == true) _archFilter = "x32";
            else if (FilterX64.IsChecked == true) _archFilter = "x64";
            else _archFilter = "all";
            RefreshTree();
        }

        private void OnSortAsc_Click(object sender, RoutedEventArgs e)
        {
            _sortAscending = true;
            SortAsc.IsChecked = true;
            SortDesc.IsChecked = false;
            RefreshTree();
        }

        private void OnSortDesc_Click(object sender, RoutedEventArgs e)
        {
            _sortAscending = false;
            SortDesc.IsChecked = true;
            SortAsc.IsChecked = false;
            RefreshTree();
        }

        private void OnPlatformsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Разрешаем выбор и листьев (полная версия), и узлов линии/группы сборок
            // (частичная версия «8.3» / «8.3.27», issue #142). Разрядность подставляется
            // по активному фильтру или из однотипных листьев узла.
            if (e.NewValue is PlatformVersionGroup node)
            {
                _selectedVersion = BuildResult(node);
                // Запоминаем выбранную версию и как «текущую»: RefreshTree() (переключение
                // фильтра разрядности, сортировка) повторно выделяет узел по _currentVersion,
                // и без обновления выбор «перескакивал» на старую версию (issue #304).
                _currentVersion = _selectedVersion;
                SelectButton.IsEnabled = !string.IsNullOrWhiteSpace(_selectedVersion);
            }
            else if (!_isRestoringSelection)
            {
                // Реальный сброс выбора пользователем — очищаем результат. Во время же
                // перестроения дерева (RefreshTree) сюда приходит null из-за удаления
                // старого узла из Items: выбор уже будет восстановлен отдельно, поэтому
                // _selectedVersion не трогаем (issue #304).
                _selectedVersion = string.Empty;
                SelectButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// Формирует строку выбора для узла дерева. Лист отдаёт полный вариант
        /// («8.3.27.1688 (64)»), узел линии/группы сборок — частичную версию
        /// с суффиксом разрядности, если он однозначен (issue #142).
        /// </summary>
        private string BuildResult(PlatformVersionGroup node)
        {
            if (node.IsLeaf && !string.IsNullOrEmpty(node.Variant))
                return node.Variant!;

            var name = node.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var archSuffix = GetNodeArchSuffix(node, _archFilter);
            return string.IsNullOrEmpty(archSuffix) ? name : $"{name} ({archSuffix})";
        }

        /// <summary>
        /// Определяет суффикс разрядности для частичной версии: берётся из активного
        /// фильтра (x32/x64) либо из однотипных листьев узла. При неоднозначности
        /// возвращает null — тогда разрядность остаётся на усмотрение лаунчера/настроек.
        /// </summary>
        private static string? GetNodeArchSuffix(PlatformVersionGroup node, string archFilter)
        {
            if (archFilter == "x32") return "32";
            if (archFilter == "x64") return "64";

            // Режим «Все» (авто): разрядность для частичной версии не подставляем, чтобы
            // выбор папки/линии дерева давал чистую версию без суффикса (issue #251) —
            // как её обычно показывает родной стартер и колонка списка («8.3.27»).
            // Разрядность в этом случае разрешается при запуске (сессия / приоритет базы).
            return null;
        }

        private void OnPlatformsTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedVersion))
                OnSelect_Click(sender, e);
        }

        private void OnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedVersion))
                return;
            DialogResult = true;
        }

        private void SelectCurrent(string currentVersion)
        {
            if (PlatformsTree.ItemsSource is not IEnumerable<PlatformVersionGroup> roots)
                return;
            var node = PlatformVersionService.FindBestNode(roots, currentVersion);
            if (node is null) return;
            node.IsCurrent = true; // подсветка жирным
            SelectNodeInTree(PlatformsTree, node);
        }

        private static bool SelectNodeInTree(ItemsControl parent, PlatformVersionGroup target)
        {
            foreach (var item in parent.Items)
            {
                if (item is not PlatformVersionGroup node) continue;
                var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (ReferenceEquals(node, target))
                {
                    if (container is not null)
                    {
                        node.IsSelected = true;
                        container.IsSelected = true;
                        container.BringIntoView();
                    }
                    return true;
                }
                if (container is not null)
                {
                    container.IsExpanded = true;
                    container.UpdateLayout();
                    if (SelectNodeInTree(container, target))
                        return true;
                }
            }
            return false;
        }
    }
}
#endif
