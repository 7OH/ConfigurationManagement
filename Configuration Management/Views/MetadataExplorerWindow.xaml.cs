#if WINDOWS
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно «Обозреватель метаданных…» (цикл 0.3.9.132–0.3.9.136, этап 2, Windows/WPF):
    /// просмотр дерева метаданных конфигурации 1С БЕЗ интерактивного конфигуратора —
    /// источник (база или файл .cf) выгружается в XML через /DumpConfigToFiles
    /// (<see cref="MetadataExplorerService"/>), дерево «Конфигурация → подсистемы → типы →
    /// объекты» строится лениво (<see cref="MetadataTreeNodeViewModel"/>, дети подгружаются
    /// при раскрытии узла), панель справа показывает детали выбранного объекта.
    /// Вся логика — в чистой ViewModel <see cref="MetadataExplorerViewModel"/>;
    /// окно открывает окно прогресса на время выгрузки и удаляет временный каталог
    /// выгрузки в Closed (Dispose).
    /// </summary>
    public partial class MetadataExplorerWindow : Window
    {
        private readonly MetadataExplorerViewModel _vm;

        /// <param name="bases">Все информационные базы списка (для ComboBox).</param>
        /// <param name="selectedBase">Предвыбранная база (выбранная в главном окне).</param>
        public MetadataExplorerWindow(IReadOnlyList<Infobase> bases, Infobase? selectedBase)
        {
            InitializeComponent();

            var service = AppServices.GetRequiredService<IMetadataExplorerService>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();

            _vm = new MetadataExplorerViewModel(
                bases,
                selectedBase,
                service,
                dialogs,
                action => Application.Current?.Dispatcher.BeginInvoke(action));

            DataContext = _vm;
            Title = LocalizationManager.T("MetadataExplorer.Title");

            // Раскрытие узла → ленивая подгрузка детей (TreeViewItem.Expanded всплывает
            // от любого вложенного контейнера — подписка один раз на дерево).
            MetadataTree.AddHandler(
                TreeViewItem.ExpandedEvent,
                new RoutedEventHandler(OnTreeItemExpanded));

            MetadataTree.SelectedItemChanged += OnTreeSelectionChanged;

            Closed += (_, _) => _vm.Dispose();
        }

        /// <summary>«Загрузить»: окно прогресса (индитерминант, этапы из StageChanged) +
        /// выполнение выгрузки; результат — дерево, ошибки — в статус-строку VM.</summary>
        private async void OnLoad_Click(object sender, RoutedEventArgs e)
        {
            var progress = new MetadataExplorerProgressWindow { Owner = this };
            progress.SetStage(LocalizationManager.T("MetadataExplorer.Status.Dumping"));
            Action<string> onStage = progress.SetStage;
            _vm.StageChanged += onStage;
            progress.Show();
            try
            {
                await _vm.LoadAsync();
            }
            finally
            {
                _vm.StageChanged -= onStage;
                progress.Close();
            }
        }

        private void OnTreeItemExpanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem item &&
                item.DataContext is MetadataTreeNodeViewModel node)
            {
                node.EnsureLoaded();
            }
        }

        private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _vm.SelectedNode = MetadataTree.SelectedItem as MetadataTreeNodeViewModel;
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
#endif