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
    /// Окно «Инспектор процессов 1С» (0.3.9.93, Windows/WPF): таблица всех запущенных
    /// процессов платформы 1С (база/режим/пользователь/время старта/PID/строка
    /// подключения/значок сопоставления) с автообновлением по таймеру, кнопкой
    /// «Обновить» и завершением выбранного процесса с подтверждением. Двойной клик
    /// по строке с известной базой — переход к базе в главном окне (FindInList-механика).
    /// Вся логика — в чистой ViewModel <see cref="ProcessInspectorViewModel"/>.
    /// </summary>
    public partial class ProcessInspectorWindow : Window
    {
        private readonly ProcessInspectorViewModel _vm;

        /// <param name="infobases">Все базы списка (для сопоставления командных строк).</param>
        /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
        public ProcessInspectorWindow(IEnumerable<Infobase> infobases, Action<Infobase> openBase)
        {
            InitializeComponent();

            var service = AppServices.GetRequiredService<IRunningInfobasesService>();
            var killer = AppServices.GetRequiredService<IOneCProcessKiller>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();

            _vm = new ProcessInspectorViewModel(
                service,
                killer,
                dialogs,
                infobases,
                openBase,
                action => Application.Current?.Dispatcher.BeginInvoke(action));

            DataContext = _vm;
            Title = LocalizationManager.T("ProcessInspector.Title");
            ProcessesGrid.ItemsSource = _vm.Processes;
            Closed += (_, _) => _vm.Dispose();
        }

        private void OnRefresh_Click(object sender, RoutedEventArgs e) => _vm.Refresh();

        private void OnKillProcess_Click(object sender, RoutedEventArgs e) => _vm.KillSelected();

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void OnGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ProcessesGrid.SelectedItem is ProcessRowViewModel row)
                _vm.OpenBase(row);
        }
    }
}