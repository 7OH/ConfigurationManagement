using System;
using System.ComponentModel;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно «Хранилище конфигурации…» (0.3.9.128, этап 2, Windows/WPF): панель подключения
    /// (адрес и пользователь readonly из свойств базы, пароль — в памяти окна), список версий
    /// хранилища (№/дата/автор/комментарий) и состав выбранной версии (тип/имя/владелец),
    /// статус-строка и подсказка. Вся логика — в чистой ViewModel
    /// <see cref="RepositoryBrowserViewModel"/>; окно прогресса операций —
    /// <see cref="RepositoryProgressWindow"/>. PasswordBox в WPF не биндится — пароль
    /// передаётся в VM из кода при нажатии «Подключить».
    /// </summary>
    public partial class RepositoryBrowserWindow : Window
    {
        private readonly RepositoryBrowserViewModel _vm;
        private RepositoryProgressWindow? _progress;

        /// <param name="infobase">Выбранная база с заполненным адресом хранилища.</param>
        public RepositoryBrowserWindow(Infobase infobase)
        {
            InitializeComponent();

            var service = AppServices.GetRequiredService<IRepositoryStorageService>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();

            _vm = new RepositoryBrowserViewModel(
                infobase,
                service,
                dialogs,
                action => Application.Current?.Dispatcher.BeginInvoke(action));

            DataContext = _vm;
            Title = LocalizationManager.T("RepositoryBrowser.Title");
            VersionsGrid.ItemsSource = _vm.Versions;
            ObjectsGrid.ItemsSource = _vm.Objects;

            // Окно прогресса: показывается, пока идёт подключение/загрузка состава версии;
            // текст этапа обновляется событиями StageChanged (IProgress сервиса).
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.StageChanged += OnStageChanged;
            Closed += OnWindowClosed;
        }

        private void OnConnect_Click(object sender, RoutedEventArgs e)
        {
            // PasswordBox не поддерживает привязку — передаём пароль в VM перед командой.
            _vm.Password = RepositoryPasswordBox.Password ?? string.Empty;
            _vm.ConnectCommand.Execute(null);
        }

        private void OnRefresh_Click(object sender, RoutedEventArgs e) => _vm.RefreshCommand.Execute(null);

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(RepositoryBrowserViewModel.IsBusy) &&
                e.PropertyName != nameof(RepositoryBrowserViewModel.IsObjectsLoading))
                return;

            if (_vm.IsBusy || _vm.IsObjectsLoading)
            {
                ShowProgress(_vm.IsObjectsLoading
                    ? LocalizationManager.T("RepositoryBrowser.Status.ObjectsLoading")
                    : LocalizationManager.T("RepositoryBrowser.Status.Connecting"));
            }
            else
            {
                HideProgress();
            }
        }

        private void OnStageChanged(string stage) => _progress?.SetStage(stage);

        private void ShowProgress(string stage)
        {
            if (_progress is null)
            {
                _progress = new RepositoryProgressWindow { Owner = this };
                _progress.Show();
                IsEnabled = false;
            }
            _progress.SetStage(stage);
        }

        private void HideProgress()
        {
            _progress?.Close();
            _progress = null;
            IsEnabled = true;
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.StageChanged -= OnStageChanged;
            HideProgress();
        }
    }
}