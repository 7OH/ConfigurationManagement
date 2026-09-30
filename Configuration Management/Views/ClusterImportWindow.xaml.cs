using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно «Импорт из кластера 1С» (0.3.9.174, Windows/WPF): импорт информационных
    /// баз из кластера сервера 1С через утилиту rac — подключение к ragent/RAS
    /// (адрес/порт/логин/пароль), выбор кластера, чеклист найденных баз с пометкой
    /// дубликатов и файловых баз, сводка «будет добавлено» и импорт отмеченных баз
    /// в список приложения. Вся логика — в чистой ViewModel
    /// <see cref="ClusterImportViewModel"/>; окно — тонкая обёртка: префилл настроек
    /// RAS, пароль передаётся в VM вручную из PasswordBox (на диск не сохраняется,
    /// см. решения планирования цикла 0.3.9.172–0.3.9.175), адрес/порт/логин
    /// сохраняются при успешном подключении. После <c>ShowDialog()==true</c> выбранные
    /// базы доступны через <see cref="SelectedBases"/>.
    /// </summary>
    public partial class ClusterImportWindow : Window
    {
        private readonly ClusterImportViewModel _vm;
        private readonly IInfobaseRepository _repository;

        /// <param name="rac">Клиент rac (кластеры, «cluster info», базы кластера).</param>
        /// <param name="repository">Репозиторий настроек (адрес/порт/логин RAS сохраняются при успешном подключении).</param>
        /// <param name="existingInfobases">Базы, уже присутствующие в списке приложения — для пометки дубликатов.</param>
        public ClusterImportWindow(
            IRacClient rac,
            IInfobaseRepository repository,
            IReadOnlyList<Infobase> existingInfobases)
        {
            InitializeComponent();
            _repository = repository;

            _vm = new ClusterImportViewModel(
                rac,
                existingInfobases,
                action => Application.Current?.Dispatcher.BeginInvoke(action));

            // Начальные адрес/порт/логин — из настроек приложения (пароль НЕ сохраняется).
            LoadSavedConnectionSettings();

            DataContext = _vm;
            Title = LocalizationManager.T("ClusterImport.Title");
            BasesGrid.ItemsSource = _vm.Rows;

            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;
            UpdateSummary();
        }

        /// <summary>
        /// Результат импорта: отмеченные новые базы (без дубликатов и файловых).
        /// Читается после <c>ShowDialog() == true</c>.
        /// </summary>
        public IReadOnlyList<Infobase> SelectedBases => _vm.SelectedBases;

        private async void OnConnect_Click(object sender, RoutedEventArgs e)
        {
            // PasswordBox не биндится (пароль живёт только в памяти окна) — передаём вручную.
            _vm.Password = PasswordBox.Password;
            await _vm.ConnectAsync();
            if (_vm.Clusters.Count > 0)
                SaveRacSettings();
        }

        private void OnLoadBases_Click(object sender, RoutedEventArgs e) =>
            _vm.LoadBasesCommand.Execute(null);

        private void OnSelectAll_Click(object sender, RoutedEventArgs e) =>
            _vm.SelectAllCommand.Execute(null);

        private void OnSelectNone_Click(object sender, RoutedEventArgs e) =>
            _vm.SelectNoneCommand.Execute(null);

        /// <summary>
        /// Переключатель «Создавать группу по имени кластера»: строки строятся с группой
        /// из текущего значения <see cref="ClusterImportViewModel.UseClusterGrouping"/>,
        /// поэтому после смены значения чеклист перечитывается (перестроение строк).
        /// </summary>
        private void OnGroupByCluster_Toggled(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _vm.Clusters.Count == 0 || _vm.SelectedClusterId is null)
                return;
            _vm.LoadBasesCommand.Execute(null);
        }

        private void OnImport_Click(object sender, RoutedEventArgs e)
        {
            _vm.ImportCommand.Execute(null);
            if (_vm.SelectedBases.Count == 0)
            {
                MessageBox.Show(
                    LocalizationManager.T("Selection.NothingSelected"),
                    Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ClusterImportViewModel.IsBusy):
                    var busy = _vm.IsBusy;
                    ConnectButton.IsEnabled = !busy;
                    LoadBasesButton.IsEnabled = !busy;
                    SelectAllButton.IsEnabled = !busy;
                    SelectNoneButton.IsEnabled = !busy;
                    ImportButton.IsEnabled = !busy;
                    ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case nameof(ClusterImportViewModel.ReadyToImportCount):
                case nameof(ClusterImportViewModel.DuplicateCount):
                    UpdateSummary();
                    break;
            }
        }

        private void UpdateSummary()
        {
            SummaryText.Text = string.Format(
                LocalizationManager.T("ClusterImport.SummaryFormat"), _vm.ReadyToImportCount);

            var duplicates = _vm.DuplicateCount;
            DuplicateSummaryText.Text = duplicates > 0
                ? string.Format(LocalizationManager.T("ClusterImport.DuplicatesFormat"), duplicates)
                : string.Empty;
        }

        /// <summary>
        /// Сохраняет адрес/порт/логин после успешного подключения (без пароля — см.
        /// решения планирования; пароль rac не хранится на диске).
        /// </summary>
        private void SaveRacSettings()
        {
            try
            {
                var settings = _repository.LoadSettings();
                settings.RacServerAddress = _vm.ServerAddress;
                settings.RacServerPort = _vm.ServerPort;
                settings.RacUserName = _vm.UserName;
                _repository.SaveSettings(settings);
            }
            catch
            {
                // Сохранение настроек не критично для работы окна.
            }
        }

        private void LoadSavedConnectionSettings()
        {
            try
            {
                var settings = _repository.LoadSettings();
                _vm.ServerAddress = string.IsNullOrWhiteSpace(settings.RacServerAddress)
                    ? "localhost"
                    : settings.RacServerAddress;
                if (settings.RacServerPort > 0)
                    _vm.ServerPort = settings.RacServerPort;
                _vm.UserName = settings.RacUserName ?? string.Empty;
            }
            catch
            {
                // Без сохранённых настроек остаются дефолты VM.
            }
        }
    }
}