using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно «Центр обслуживания» (0.3.9.89, Windows/WPF): таблица всех баз со сводкой
    /// состояния (доступность, последняя копия, размер ИБ, кэш, конфигурация, возраст
    /// данных, проверка обновлений), фильтр-переключатель «Только проблемы» и кнопки
    /// «Проверить доступность» / «Открыть базу». Двойной клик по строке — переход к
    /// базе в главном окне (FindInList-механика). Вся логика — в чистой ViewModel
    /// <see cref="MaintenanceCenterViewModel"/>.
    /// </summary>
    public partial class MaintenanceCenterWindow : Window
    {
        private readonly MaintenanceCenterViewModel _vm;

        /// <param name="infobases">Все базы списка.</param>
        /// <param name="checkAvailability">Запуск общей проверки доступности (команда главного окна).</param>
        /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
        public MaintenanceCenterWindow(
            IEnumerable<Infobase> infobases,
            Action checkAvailability,
            Action<Infobase> openBase)
        {
            InitializeComponent();
            _vm = new MaintenanceCenterViewModel(infobases, checkAvailability, openBase);
            DataContext = _vm;

            Title = LocalizationManager.T("Maintenance.Title");
            BasesGrid.ItemsSource = _vm.Rows;

            if (!_vm.HasBases)
                HintText.Text = LocalizationManager.T("Maintenance.NoBases");
        }

        private void OnCheckAvailability_Click(object sender, RoutedEventArgs e) => _vm.CheckAvailability();

        private void OnOpenBase_Click(object sender, RoutedEventArgs e) => OpenSelectedBase();

        private void OnGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (BasesGrid.SelectedItem is MaintenanceCenterRowViewModel row)
                OpenBaseAndClose(row);
        }

        private void OpenSelectedBase()
        {
            if (BasesGrid.SelectedItem is MaintenanceCenterRowViewModel row)
                OpenBaseAndClose(row);
        }

        /// <summary>«Открыть базу»: переход к строке в главном окне и закрытие дашборда.</summary>
        private void OpenBaseAndClose(MaintenanceCenterRowViewModel row)
        {
            _vm.OpenBase(row);
            Close();
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}