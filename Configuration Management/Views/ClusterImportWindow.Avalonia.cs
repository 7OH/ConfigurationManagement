#if LINUX
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Configuration_Management.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно «Импорт из кластера 1С» (0.3.9.174, Avalonia/Linux): импорт информационных
    /// баз из кластера сервера 1С через утилиту rac — подключение к ragent/RAS
    /// (адрес/порт/логин/пароль), выбор кластера, чеклист найденных баз с пометкой
    /// дубликатов и файловых баз, сводка «будет добавлено» и импорт отмеченных баз
    /// в список приложения. Вся логика — в чистой ViewModel
    /// <see cref="ClusterImportViewModel"/>; окно — тонкая обёртка (аналогично
    /// <see cref="ServerMonitorWindow"/> и <see cref="BaseSelectionWindow"/>): префилл
    /// настроек RAS, пароль передаётся в VM вручную из PasswordBox (на диск не
    /// сохраняется), адрес/порт/логин сохраняются при успешном подключении.
    /// После <c>ShowDialogSync()==true</c> выбранные базы доступны через
    /// <see cref="SelectedBases"/>.
    /// </summary>
    public partial class ClusterImportWindow : ModalWindowBase
    {
        private readonly ClusterImportViewModel _vm;
        private readonly IInfobaseRepository _repository;
        private readonly ComboBox _clusterCombo;
        private readonly TextBlock _summaryText;
        private readonly TextBlock _duplicateSummaryText;
        private readonly ProgressBar _progressBar;
        private readonly Button _connectButton;
        private readonly Button _loadBasesButton;
        private readonly Button _selectAllButton;
        private readonly Button _selectNoneButton;
        private readonly Button _importButton;

        /// <param name="rac">Клиент rac (кластеры, «cluster info», базы кластера).</param>
        /// <param name="repository">Репозиторий настроек (адрес/порт/логин RAS сохраняются при успешном подключении).</param>
        /// <param name="existingInfobases">Базы, уже присутствующие в списке приложения — для пометки дубликатов.</param>
        public ClusterImportWindow(
            IRacClient rac,
            IInfobaseRepository repository,
            IReadOnlyList<Infobase> existingInfobases)
        {
            AvaloniaXamlLoader.Load(this);

            _repository = repository;

            _vm = new ClusterImportViewModel(
                rac,
                existingInfobases,
                action => Dispatcher.UIThread.Post(action));

            Title = LocalizationManager.T("ClusterImport.Title");
            DataContext = _vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;

            // Начальные адрес/порт/логин — из настроек приложения (пароль НЕ сохраняется).
            LoadSavedConnectionSettings();

            // ---- Поля подключения (двухсторонняя привязка к VM). ----
            this.FindControl<TextBox>("AddressBox")!
                .Bind(TextBox.TextProperty, new Binding("ServerAddress", BindingMode.TwoWay));
            this.FindControl<TextBox>("PortBox")!
                .Bind(TextBox.TextProperty, new Binding("ServerPort", BindingMode.TwoWay));
            this.FindControl<TextBox>("UserBox")!
                .Bind(TextBox.TextProperty, new Binding("UserName", BindingMode.TwoWay));

            // ---- Выбор кластера: список из VM, синхронизация выбора в обе стороны. ----
            _clusterCombo = this.FindControl<ComboBox>("ClusterCombo")!;
            _clusterCombo.Bind(ItemsControl.ItemsSourceProperty, new Binding("Clusters"));
            _clusterCombo.ItemTemplate = new FuncDataTemplate<RacCluster>((cluster, _) =>
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = cluster.Name, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"[{cluster.Port}]", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
            _clusterCombo.SelectionChanged += (_, _) =>
            {
                if (_clusterCombo.SelectedItem is RacCluster cluster)
                    _vm.SelectedClusterId = cluster.Id;
            };

            // ---- Чеклист баз и статусные строки. ----
            this.FindControl<ListBox>("BasesList")!.ItemsSource = _vm.Rows;
            this.FindControl<TextBlock>("StatusText")!
                .Bind(TextBlock.TextProperty, new Binding("StatusText"));
            this.FindControl<TextBlock>("ErrorText")!
                .Bind(TextBlock.TextProperty, new Binding("ErrorMessage"));

            _summaryText = this.FindControl<TextBlock>("SummaryText")!;
            _duplicateSummaryText = this.FindControl<TextBlock>("DuplicateSummaryText")!;
            _progressBar = this.FindControl<ProgressBar>("BusyProgress")!;
            UpdateSummary();

            // ---- Переключатель «Создавать группу по имени кластера»: строки строятся
            // с группой из текущего значения UseClusterGrouping, поэтому после смены
            // значения чеклист перечитывается (перестроение строк). ----
            var groupCheck = this.FindControl<CheckBox>("GroupByClusterCheck")!;
            groupCheck.IsChecked = _vm.UseClusterGrouping;
            groupCheck.IsCheckedChanged += (_, _) =>
            {
                _vm.UseClusterGrouping = groupCheck.IsChecked == true;
                if (_vm.Clusters.Count > 0 && _vm.SelectedClusterId is not null)
                    _vm.LoadBases();
            };

            // ---- Кнопки (паттерн BaseSelectionWindow: строятся в коде, хост — в разметке). ----
            var passwordBox = this.FindControl<PasswordBox>("PasswordInput")!;
            passwordBox.Styled(ControlThemes.ModernPasswordBox);
            _connectButton = BuildActionButton(LocalizationManager.T("ClusterImport.Connect"), "🔌",
                () =>
                {
                    // PasswordBox не биндится (пароль живёт только в памяти окна) — вручную.
                    _vm.Password = passwordBox.Password ?? string.Empty;
                    _ = ConnectAndSaveAsync();
                });
            this.FindControl<ContentControl>("ConnectHost")!.Content = _connectButton;

            _loadBasesButton = BuildActionButton(LocalizationManager.T("ClusterImport.LoadBases"), "⟳",
                () => _vm.LoadBases());
            this.FindControl<ContentControl>("LoadHost")!.Content = _loadBasesButton;

            var bulk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _selectAllButton = BuildSecondaryButton(LocalizationManager.T("Selection.SelectAll"),
                () => _vm.SelectAllCommand.Execute(null));
            _selectNoneButton = BuildSecondaryButton(LocalizationManager.T("Selection.None"),
                () => _vm.SelectNoneCommand.Execute(null));
            bulk.Children.Add(_selectAllButton);
            bulk.Children.Add(_selectNoneButton);
            this.FindControl<ContentControl>("BulkHost")!.Content = bulk;

            _importButton = BuildImportButton();
            this.FindControl<ContentControl>("ImportHost")!.Content = _importButton;
        }

        /// <summary>
        /// Результат импорта: отмеченные новые базы (без дубликатов и файловых).
        /// Читается после <c>ShowDialogSync() == true</c>.
        /// </summary>
        public IReadOnlyList<Infobase> SelectedBases => _vm.SelectedBases;

        /// <summary>Показывает окно модально (синхронно) — обёртка над <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>.</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private async System.Threading.Tasks.Task ConnectAndSaveAsync()
        {
            await _vm.ConnectAsync();
            if (_vm.Clusters.Count > 0)
                SaveRacSettings();
        }

        private void OnImportClick()
        {
            _vm.ImportCommand.Execute(null);
            if (_vm.SelectedBases.Count == 0)
            {
                AppServices.GetRequiredService<IDialogService>()
                    .ShowInfo(LocalizationManager.T("Selection.NothingSelected"), Title);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ClusterImportViewModel.IsBusy):
                    var busy = _vm.IsBusy;
                    _connectButton.IsEnabled = !busy;
                    _loadBasesButton.IsEnabled = !busy;
                    _selectAllButton.IsEnabled = !busy;
                    _selectNoneButton.IsEnabled = !busy;
                    _importButton.IsEnabled = !busy;
                    _progressBar.IsVisible = busy;
                    break;
                case nameof(ClusterImportViewModel.SelectedClusterId):
                    SyncSelectedCluster();
                    break;
                case nameof(ClusterImportViewModel.ReadyToImportCount):
                case nameof(ClusterImportViewModel.DuplicateCount):
                    UpdateSummary();
                    break;
            }
        }

        /// <summary>
        /// Автовыбор первого кластера VM синхронизируется с комбобоксом (комбобокс
        /// не биндится на SelectedValue: коллекция Clusters заменяется после каждого
        /// подключения, а явная синхронизация исключает обратные срабатывания).
        /// </summary>
        private void SyncSelectedCluster()
        {
            if (_vm.SelectedClusterId is not Guid id)
                return;
            if (_clusterCombo.SelectedItem is RacCluster current && current.Id == id)
                return;
            _clusterCombo.SelectedItem = _vm.Clusters.FirstOrDefault(c => c.Id == id);
        }

        private void UpdateSummary()
        {
            _summaryText.Text = string.Format(
                LocalizationManager.T("ClusterImport.SummaryFormat"), _vm.ReadyToImportCount);

            var duplicates = _vm.DuplicateCount;
            _duplicateSummaryText.Text = duplicates > 0
                ? string.Format(LocalizationManager.T("ClusterImport.DuplicatesFormat"), duplicates)
                : string.Empty;
        }

        private Button BuildImportButton()
        {
            var button = BuildActionButton(LocalizationManager.T("ClusterImport.ImportButton"), "⇩", OnImportClick);
            button.Background = new SolidColorBrush(Color.Parse("#06B6D4"));
            button.Foreground = Brushes.White;
            return button;
        }

        private Button BuildActionButton(string text, string icon, Action onClick)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = icon, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                Padding = new Thickness(12, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button BuildSecondaryButton(string text, Action onClick)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 6) };
            button.Styled(ControlThemes.SecondaryButton);
            button.Click += (_, _) => onClick();
            return button;
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
#endif