using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно «Диагностика подключения» (0.3.9.232, Windows/WPF, функция 12): карточка
    /// хоста (DNS, IP-адреса, пинг), таблица портов со состояниями и задержками,
    /// выводы-подсказки и кнопки «Проверить» / «Проверить порты 1С» / «Повторить».
    /// Вся логика — в чистой ViewModel <see cref="NetworkDiagnosticsViewModel"/>;
    /// окно тонкое: привязки и запуск первого прогона.
    /// </summary>
    public partial class NetworkDiagnosticsWindow : Window
    {
        private readonly NetworkDiagnosticsViewModel _vm;

        public NetworkDiagnosticsWindow(NetworkDiagnosticsViewModel vm)
        {
            InitializeComponent();

            _vm = vm ?? throw new System.ArgumentNullException(nameof(vm));
            DataContext = _vm;
            Title = LocalizationManager.T("Diagnostics.Title");

            // «Проверить порты 1С» открывает диалог портов сервисов (issue #335);
            // после подтверждения — сканирование с выбранными портами.
            _vm.EditPortsRequested += OnEditPortsRequested;

            // Автоматический первый прогон (решение п. 8.5 плана): окно сразу
            // показывает карточку, кнопки перезапускают проверку.
            Loaded += (_, _) => _ = _vm.RunAsync();
        }

        /// <summary>Открывает диалог портов 1С и запускает сканирование по подтверждению.</summary>
        private void OnEditPortsRequested()
        {
            var dialog = new PortsEditWindow(_vm.BuildPortsForEdit()) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not { } ports)
                return;
            _ = _vm.ApplyEditedPortsAndCheckAsync(ports);
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}