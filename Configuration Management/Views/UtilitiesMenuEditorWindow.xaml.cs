#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Microsoft.Win32;

namespace Configuration_Management
{
    /// <summary>
    /// Редактор структуры меню «Утилиты» (issue #374): дерево компоновки с перемещением
    /// пунктов, отключением, группировкой, назначением иконок (из каталога/внешнего файла),
    /// сбросом к стандартной компоновке и сохранением/загрузкой в файл
    /// <c>utilities_menu_layout.json</c>.
    /// </summary>
    public partial class UtilitiesMenuEditorWindow : Window
    {
        private readonly UtilitiesMenuEditorViewModel _vm;

        /// <param name="store">Хранилище компоновки (или null — редактирование без сохранения).</param>
        public UtilitiesMenuEditorWindow(IUtilitiesMenuLayoutStore? store)
        {
            InitializeComponent();
            _vm = new UtilitiesMenuEditorViewModel(store);
            DataContext = _vm;
            Owner = Application.Current?.MainWindow;
            // Сразу выбираем первую группу-назначение (если она есть), чтобы «В группу»
            // работал без лишнего клика.
            if (_vm.Groups.Count > 0)
                _vm.SelectedGroup = _vm.Groups[0];
            Loaded += (_, _) => ExpandAll(MenuTree.Items);
        }

        /// <summary>Компоновка после редактирования (актуальна после «Готово»).</summary>
        public Configuration_Management.Models.UtilitiesMenuLayout Layout => _vm.Layout;

        /// <summary>Синхронизирует выбранный узел дерева с моделью представления.</summary>
        private void OnTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _vm.SelectedItem = e.NewValue as UtilitiesMenuNodeItem;
        }

        /// <summary>Выбор иконки из внешнего файла: диалог → команда модели представления.</summary>
        private void OnExternalIcon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.T("Utilities.Editor.ExternalIconDialogTitle"),
                Filter = LocalizationManager.T("Utilities.Editor.ExternalIconFilter"),
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FileName))
            {
                _vm.SetExternalIconCommand.Execute(dialog.FileName);
            }
        }

        /// <summary>«Готово»: сохраняет компоновку и закрывает окно.</summary>
        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            _vm.SaveCommand.Execute(null);
            DialogResult = true;
        }

        private void ExpandAll(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is TreeViewItem item)
                {
                    item.IsExpanded = true;
                    ExpandAll(item.Items);
                }
            }
        }
    }
}
#endif