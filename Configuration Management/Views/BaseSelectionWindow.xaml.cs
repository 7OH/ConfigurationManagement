using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно выбора баз (выборочный экспорт и добавляющий импорт): список строк
    /// с флажками, «Выделить все/Снять все», OK/Отмена. После ShowDialog выбранные
    /// строки доступны через <see cref="GetSelected"/>; отмена — пустой список.
    /// </summary>
    public partial class BaseSelectionWindow : Window
    {
        private readonly List<BaseSelectionItem> _items;

        public BaseSelectionWindow(string title, string hint, IEnumerable<BaseSelectionItem> items)
        {
            InitializeComponent();
            Title = title;
            HintText.Text = hint;
            _items = items.ToList();
            ItemsList.ItemsSource = _items;
        }

        /// <summary>Отмеченные строки (в порядке списка).</summary>
        public List<BaseSelectionItem> GetSelected() =>
            _items.Where(i => i.IsChecked).ToList();

        private void OnSelectAll_Click(object sender, RoutedEventArgs e) =>
            SetAll(true);

        private void OnSelectNone_Click(object sender, RoutedEventArgs e) =>
            SetAll(false);

        private void SetAll(bool value)
        {
            foreach (var item in _items)
                item.IsChecked = value;
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = GetSelected().Count > 0;
            if (DialogResult != true)
            {
                System.Windows.MessageBox.Show(
                    LocalizationManager.T("Selection.NothingSelected"),
                    Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Close();
        }
    }
}
