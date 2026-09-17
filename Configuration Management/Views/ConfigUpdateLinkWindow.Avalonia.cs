#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    /// <summary>
    /// Окно настройки связи ИБ ↔ типовая конфигурация 1С: выбор типовой конфигурации и редакции,
    /// формирование адреса каталога релизов (автоматически по правилу 1С либо вручную) и кнопка
    /// «Определить версию», которая читает имя и версию конфигурации базы через
    /// <see cref="ConfigurationInfoService.ReadAndApply"/>. Открывается модально; при сохранении
    /// записывает <see cref="Infobase.UpdateConfigCode"/> и <see cref="Infobase.UpdateUrlOverride"/>.
    /// Avalonia/Linux-версия WPF-окна <see cref="ConfigUpdateLinkWindow"/>.
    /// </summary>
    public sealed class ConfigUpdateLinkWindow : ModalWindowBase
    {
        private readonly Services.IOneCUpdatesService _updates = AppServices.GetRequiredService<Services.IOneCUpdatesService>();
        private readonly Services.IInfobaseRepository _repository = AppServices.GetRequiredService<Services.IInfobaseRepository>();
        private readonly Services.IAppLogger _logger = AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private readonly Infobase _infobase;
        private readonly List<OneCConfigType> _configs = new();

        private bool _initializing = true;
        private bool _definingVersion;

        private readonly TextBlock _baseNameText = new();
        private ComboBox _configCombo = new();
        private ComboBox _editionCombo = new();
        private RadioButton _autoUrlRadio = new();
        private RadioButton _manualUrlRadio = new();
        private TextBox _urlBox = new();
        private Button _defineVersionButton = new();
        private readonly TextBlock _currentConfigText = new();
        private readonly TextBlock _resultText = new();

        /// <param name="infobase">Информационная база, для которой настраивается связь с конфигурацией.</param>
        public ConfigUpdateLinkWindow(Infobase infobase)
        {
            _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));

            Title = LocalizationManager.T("Updates.EditConfig");
            Width = 620;
            Height = 600;
            MinWidth = 540;
            MinHeight = 520;
            FontSize = 13;
            CanResize = true;

            _baseNameText.Text = _infobase.Name;
            Themes.ThemeBrushes.Bind(_baseNameText, TextBlock.ForegroundProperty, "AccentBrush");

            LoadConfigs();
            _configCombo.ItemsSource = _configs;
            SelectInitialConfig();

            // Режим URL: ручная ссылка, если она была задана, иначе автоформирование.
            var hasOverride = !string.IsNullOrWhiteSpace(_infobase.UpdateUrlOverride);
            if (hasOverride)
            {
                _manualUrlRadio.IsChecked = true;
                _urlBox.IsReadOnly = false;
                _urlBox.Text = _infobase.UpdateUrlOverride;
            }
            else
            {
                _autoUrlRadio.IsChecked = true;
                _urlBox.IsReadOnly = true;
            }

            _initializing = false;
            RebuildUrl();

            Content = BuildRoot();
            ShowCurrentConfigSummary();
        }

        /// <summary>
        /// Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>, чтобы диалог можно было
        /// вызывать из ViewModel (не наследника окна).
        /// </summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private void LoadConfigs()
        {
            try
            {
                var settings = _repository.LoadSettings();
                var custom = settings.CustomConfigTypes ?? new List<OneCConfigType>();
                _configs.Clear();
                _configs.AddRange(BuiltInConfigTypes.All);
                _configs.AddRange(custom);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка загрузки списка типовых конфигураций для связи ИБ", ex);
                _configs.Clear();
                _configs.AddRange(BuiltInConfigTypes.All);
            }
        }

        private void SelectInitialConfig()
        {
            var code = _infobase.UpdateConfigCode;
            OneCConfigType? selected = null;
            if (!string.IsNullOrWhiteSpace(code))
                selected = _configs.FirstOrDefault(c =>
                    string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

            _configCombo.SelectedItem = selected ?? (_configs.Count > 0 ? _configs[0] : null);
        }

        private void OnConfigSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_initializing)
                return;

            var config = _configCombo.SelectedItem as OneCConfigType;
            var editions = config?.Editions ?? new List<OneCConfigEdition>();

            var previous = _editionCombo.SelectedItem;
            _editionCombo.ItemsSource = editions;
            if (editions.Count > 0)
                _editionCombo.SelectedItem = previous ?? config?.DefaultEdition ?? editions[0];

            RebuildUrl();
        }

        private void OnEditionSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_initializing)
                return;
            RebuildUrl();
        }

        private void OnUrlModeChanged(object? sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;

            var manual = _manualUrlRadio.IsChecked == true;
            _urlBox.IsReadOnly = !manual;
            if (!manual)
                RebuildUrl();
        }

        /// <summary>Пересчитывает адрес каталога релизов в автоматическом режиме.</summary>
        private void RebuildUrl()
        {
            if (_initializing)
                return;
            if (_manualUrlRadio.IsChecked == true)
                return;

            var config = _configCombo.SelectedItem as OneCConfigType;
            var edition = _editionCombo.SelectedItem as OneCConfigEdition;
            _urlBox.Text = _updates.BuildUpdateUrl(config, edition, null);
        }

        private async void OnDefineVersionClick()
        {
            if (_definingVersion)
                return;

            _definingVersion = true;
            _defineVersionButton.IsEnabled = false;
            _resultText.Text = string.Empty;

            try
            {
                await Task.Run(() => ConfigurationInfoService.ReadAndApply(
                    _infobase, overwriteExisting: true, mode: OneCLaunchMode.Configurator));
                ShowCurrentConfigSummary();
                _resultText.Text = T("Updates.VersionDefined");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка определения версии конфигурации базы «{_infobase.Name}»", ex);
                _dialogs.ShowError(T("Updates.CheckFailed"), T("Updates.EditConfig"));
            }
            finally
            {
                _definingVersion = false;
                _defineVersionButton.IsEnabled = true;
            }
        }

        private void ShowCurrentConfigSummary()
        {
            var name = string.IsNullOrWhiteSpace(_infobase.ConfigurationName)
                ? "—"
                : _infobase.ConfigurationName;
            var version = string.IsNullOrWhiteSpace(_infobase.ConfigurationVersion)
                ? "—"
                : _infobase.ConfigurationVersion;
            _currentConfigText.Text = $"{name} · {version}";
        }

        private void OnSaveClick()
        {
            var config = _configCombo.SelectedItem as OneCConfigType;
            if (config is null)
            {
                _dialogs.ShowWarning(T("Updates.NoConfigSelected"), T("Updates.EditConfig"));
                return;
            }

            _infobase.UpdateConfigCode = config.Code;
            _infobase.UpdateUrlOverride = _manualUrlRadio.IsChecked == true
                ? (_urlBox.Text?.Trim() ?? string.Empty)
                : string.Empty;

            PersistLink();
            DialogResult = true;
            Close();
        }

        private void PersistLink()
        {
            try
            {
                var all = _repository.Load();
                var match = all.FirstOrDefault(x => ReferenceEquals(x, _infobase));
                if (match is null)
                    return;
                match.UpdateConfigCode = _infobase.UpdateConfigCode;
                match.UpdateUrlOverride = _infobase.UpdateUrlOverride;
                _repository.Save(all);
            }
            catch (Exception ex)
            {
                _logger.Warn("Не удалось сохранить связь ИБ ↔ конфигурация в репозиторий: " + ex.Message);
            }
        }

        private void OnCancelClick()
        {
            DialogResult = false;
            Close();
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = T("Updates.EditConfig"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            _baseNameText.FontSize = 13;
            _baseNameText.FontWeight = FontWeight.SemiBold;
            _baseNameText.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(_baseNameText, 1);
            grid.Children.Add(_baseNameText);

            var form = new StackPanel { Margin = new Thickness(0, 4, 0, 0), Spacing = 4 };

            // Типовая конфигурация.
            form.Children.Add(MakeFieldLabel(T("Updates.Name")));
            _configCombo = new ComboBox
            {
                ItemsSource = _configs,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 36
            };
            _configCombo.SelectionChanged += OnConfigSelectionChanged;
            form.Children.Add(_configCombo);

            // Редакция.
            form.Children.Add(MakeFieldLabel(T("Updates.SelectEdition")));
            _editionCombo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 36
            };
            _editionCombo.SelectionChanged += OnEditionSelectionChanged;
            form.Children.Add(_editionCombo);

            // Режим URL: авто / вручную.
            form.Children.Add(MakeFieldLabel(T("Updates.Url")));
            var radioRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
            _autoUrlRadio = new RadioButton { Content = T("Updates.UrlAuto"), GroupName = "UrlMode" };
            _autoUrlRadio.IsCheckedChanged += OnUrlModeChanged;
            _manualUrlRadio = new RadioButton { Content = T("Updates.UrlManual"), GroupName = "UrlMode" };
            _manualUrlRadio.IsCheckedChanged += OnUrlModeChanged;
            radioRow.Children.Add(_autoUrlRadio);
            radioRow.Children.Add(_manualUrlRadio);
            form.Children.Add(radioRow);

            _urlBox = new TextBox
            {
                IsReadOnly = true,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Center,
                MinHeight = 36
            };
            _urlBox.Styled(ControlThemes.ModernTextBox);
            form.Children.Add(_urlBox);

            // Определить версию.
            _defineVersionButton = new Button
            {
                Content = T("Updates.DefineVersion"),
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 14, 0, 0)
            };
            _defineVersionButton.Styled(ControlThemes.SecondaryButton);
            _defineVersionButton.Click += (_, _) => OnDefineVersionClick();
            form.Children.Add(_defineVersionButton);

            _currentConfigText.FontSize = 12;
            _currentConfigText.TextWrapping = TextWrapping.Wrap;
            _currentConfigText.Margin = new Thickness(0, 10, 0, 0);
            Themes.ThemeBrushes.Bind(_currentConfigText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            form.Children.Add(_currentConfigText);

            _resultText.FontSize = 12;
            _resultText.TextWrapping = TextWrapping.Wrap;
            _resultText.Margin = new Thickness(0, 4, 0, 0);
            _resultText.Foreground = new SolidColorBrush(Color.Parse("#16A34A"));
            form.Children.Add(_resultText);

            var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 2);
            grid.Children.Add(scroll);

            // Нижняя панель: Сохранить / Отмена.
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10
            };
            var save = new Button { Content = T("Updates.Save"), Width = 150, Height = 36, IsDefault = true };
            save.Styled(ControlThemes.DialogConfirmButton);
            save.Click += (_, _) => OnSaveClick();

            var cancel = BuildCancelActionButton(120);
            cancel.Click += (_, _) => OnCancelClick();

            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            Grid.SetColumn(buttons, 1);
            bottom.Children.Add(buttons);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            return grid;
        }

        private static TextBlock MakeFieldLabel(string text)
        {
            var label = new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 14, 0, 4)
            };
            Themes.ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            return label;
        }
    }
}
#endif