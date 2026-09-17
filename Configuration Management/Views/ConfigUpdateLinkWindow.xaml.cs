#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management;

/// <summary>
/// Окно настройки связи ИБ ↔ типовая конфигурация 1С: выбор типовой конфигурации и редакции,
/// формирование адреса каталога релизов (автоматически по правилу 1С либо вручную) и кнопка
/// «Определить версию», которая читает имя и версию конфигурации базы через
/// <see cref="ConfigurationInfoService.ReadAndApply"/>. Открывается модально; при сохранении
/// записывает <see cref="Infobase.UpdateConfigCode"/> и <see cref="Infobase.UpdateUrlOverride"/>.
/// </summary>
public partial class ConfigUpdateLinkWindow : Window
{
    private readonly IOneCUpdatesService _updates = AppServices.GetRequiredService<IOneCUpdatesService>();
    private readonly IInfobaseRepository _repository = AppServices.GetRequiredService<IInfobaseRepository>();
    private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    private readonly Infobase _infobase;
    private readonly List<OneCConfigType> _configs = new();

    private bool _initializing = true;
    private bool _definingVersion;

    /// <param name="infobase">Информационная база, для которой настраивается связь с конфигурацией.</param>
    public ConfigUpdateLinkWindow(Infobase infobase)
    {
        _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));

        InitializeComponent();
        BaseNameText.Text = _infobase.Name;

        LoadConfigs();
        ConfigCombo.ItemsSource = _configs;
        SelectInitialConfig();

        // Режим URL: ручная ссылка, если она была задана, иначе автоформирование.
        var hasOverride = !string.IsNullOrWhiteSpace(_infobase.UpdateUrlOverride);
        if (hasOverride)
        {
            ManualUrlRadio.IsChecked = true;
            UrlBox.IsReadOnly = false;
            UrlBox.Text = _infobase.UpdateUrlOverride;
        }
        else
        {
            AutoUrlRadio.IsChecked = true;
            UrlBox.IsReadOnly = true;
        }

        _initializing = false;
        RebuildUrl();

        ShowCurrentConfigSummary();
    }

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

        ConfigCombo.SelectedItem = selected ?? (_configs.Count > 0 ? _configs[0] : null);
    }

    private void OnConfigSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;

        var config = ConfigCombo.SelectedItem as OneCConfigType;
        var editions = config?.Editions ?? new List<OneCConfigEdition>();

        var previous = EditionCombo.SelectedItem;
        EditionCombo.ItemsSource = editions;
        if (editions.Count > 0)
            EditionCombo.SelectedItem = previous ?? config?.DefaultEdition ?? editions[0];

        if (!_initializing)
            RebuildUrl();
    }

    private void OnEditionSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;
        RebuildUrl();
    }

    private void OnUrlModeChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;

        var manual = ManualUrlRadio.IsChecked == true;
        UrlBox.IsReadOnly = !manual;
        if (!manual)
            RebuildUrl();
    }

    /// <summary>Пересчитывает адрес каталога релизов в автоматическом режиме.</summary>
    private void RebuildUrl()
    {
        if (_initializing)
            return;
        if (ManualUrlRadio.IsChecked == true)
            return;

        var config = ConfigCombo.SelectedItem as OneCConfigType;
        var edition = EditionCombo.SelectedItem as OneCConfigEdition;
        UrlBox.Text = _updates.BuildUpdateUrl(config, edition, null);
    }

    private async void OnDefineVersionClick(object sender, RoutedEventArgs e)
    {
        if (_definingVersion)
            return;

        _definingVersion = true;
        DefineVersionButton.IsEnabled = false;
        ResultText.Text = string.Empty;

        try
        {
            await Task.Run(() => ConfigurationInfoService.ReadAndApply(
                _infobase, overwriteExisting: true, mode: OneCLaunchMode.Configurator));
            ShowCurrentConfigSummary();
            ResultText.Text = LocalizationManager.T("Updates.VersionDefined");
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка определения версии конфигурации базы «{_infobase.Name}»", ex);
            _dialogs.ShowError(LocalizationManager.T("Updates.CheckFailed"),
                LocalizationManager.T("Updates.EditConfig"));
        }
        finally
        {
            _definingVersion = false;
            DefineVersionButton.IsEnabled = true;
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
        CurrentConfigText.Text = $"{name} · {version}";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var config = ConfigCombo.SelectedItem as OneCConfigType;
        if (config is null)
        {
            _dialogs.ShowWarning(LocalizationManager.T("Updates.NoConfigSelected"),
                LocalizationManager.T("Updates.EditConfig"));
            return;
        }

        _infobase.UpdateConfigCode = config.Code;
        _infobase.UpdateUrlOverride = ManualUrlRadio.IsChecked == true
            ? (UrlBox.Text?.Trim() ?? string.Empty)
            : string.Empty;

        PersistLink();
        DialogResult = true;
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

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
#endif