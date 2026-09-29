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
/// <see cref="ConfigurationInfoService.ReadAndApply"/> и автоматически сопоставляет базу с типовой
/// конфигурацией по имени (issue #322). Открывается модально; при сохранении записывает
/// <see cref="Infobase.UpdateConfigCode"/>, <see cref="Infobase.UpdateUrlOverride"/> и персональный
/// сегмент (ник) адреса <see cref="Infobase.UpdateUrlSegment"/>.
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
        ApplyConfigSelection(rebuildUrl: false);

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
        ApplyConfigSelection(rebuildUrl: true);
    }

    /// <summary>
    /// Применяет выбранную конфигурацию: заполняет список редакций и поле сегмента адреса.
    /// Вызывается и при смене конфигурации, и при инициализации окна (раньше при старте
    /// редакции не заполнялись — событие SelectionChanged подавлялось флагом инициализации,
    /// из-за чего «выбор версии не работал» при автоопределённой конфигурации, issue #322).
    /// </summary>
    private void ApplyConfigSelection(bool rebuildUrl)
    {
        var config = ConfigCombo.SelectedItem as OneCConfigType;
        var editions = config?.Editions ?? new List<OneCConfigEdition>();

        var previous = EditionCombo.SelectedItem;
        EditionCombo.ItemsSource = editions;
        if (editions.Count > 0)
        {
            var previousValid = previous is OneCConfigEdition prev && editions.Contains(prev);
            EditionCombo.SelectedItem = previousValid ? previous : (config?.DefaultEdition ?? editions[0]);
        }
        else
        {
            EditionCombo.SelectedItem = null;
        }

        UpdateSegmentControls();

        if (rebuildUrl)
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

    /// <summary>
    /// Показывает в поле сегмента действующее значение для базы: персональный сегмент базы,
    /// либо (если не задан) ник выбранной типовой конфигурации как исходное значение.
    /// </summary>
    private void UpdateSegmentControls()
    {
        var config = ConfigCombo.SelectedItem as OneCConfigType;
        var personal = _infobase.UpdateUrlSegment;
        if (!string.IsNullOrWhiteSpace(personal))
        {
            SegmentBox.Text = personal.Trim();
            return;
        }

        SegmentBox.Text = config?.Nick?.Trim() ?? string.Empty;
    }

    private void OnSegmentChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_initializing)
            return;
        RebuildUrl();
    }

    /// <summary>Открывает окно «Типовые конфигурации» (модально поверх текущего), где хранится
    /// ник конфигурации, по умолчанию подставляемый в адрес каталога релизов.</summary>
    private void OnOpenConfigTypesClick(object sender, RoutedEventArgs e)
    {
        var win = new ConfigTypesEditWindow();
        win.Owner = this;
        win.ShowDialog();

        // После правки пользовательских конфигураций перечитываем список и обновляем выбор.
        var code = (ConfigCombo.SelectedItem as OneCConfigType)?.Code;
        LoadConfigs();
        ConfigCombo.ItemsSource = _configs;
        var restored = _configs.FirstOrDefault(c =>
            string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        ConfigCombo.SelectedItem = restored ?? (_configs.Count > 0 ? _configs[0] : null);
        ApplyConfigSelection(rebuildUrl: true);
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
        var segment = SegmentBox.Text?.Trim() ?? string.Empty;
        UrlBox.Text = _updates.BuildUpdateUrl(config, edition, null, segment);
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
            var info = await Task.Run(() => ConfigurationInfoService.ReadAndApply(
                _infobase, overwriteExisting: true, mode: OneCLaunchMode.Configurator));
            ShowCurrentConfigSummary();
            if (info is { Name.Length: > 0 })
                TryAutoMatchConfig(info.Value.Name, info.Value.Version);
            else
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

    /// <summary>
    /// Сопоставляет имя конфигурации базы со списком типовых (встроенные + пользовательские) и
    /// при совпадении подставляет конфигурацию и (по версии) редакцию — «прописал в базе — и
    /// связь работает» (issue #322). Если имя не найдено — текущий выбор сохраняется, в результат
    /// выводится понятное сообщение.
    /// </summary>
    private void TryAutoMatchConfig(string configName, string version)
    {
        var match = FindConfigByInfobaseName(configName);
        if (match is null)
        {
            ResultText.Text = string.Format(LocalizationManager.T("Updates.ConfigNotMatched"), configName);
            return;
        }

        ConfigCombo.SelectedItem = match;
        ApplyConfigSelection(rebuildUrl: true);
        TrySelectEditionByVersion(match, version);

        ResultText.Text = string.Format(LocalizationManager.T("Updates.ConfigMatched"), match.Name);
    }

    private OneCConfigType? FindConfigByInfobaseName(string configName)
    {
        var trimmed = configName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return null;

        // 1) Точное совпадение по имени (без учёта регистра).
        var exact = _configs.FirstOrDefault(c =>
            string.Equals(c.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        // 2) Имя типовой конфигурации содержится в имени базы («Бухгалтерия предприятия, ред. 3.0»).
        var contained = _configs.FirstOrDefault(c =>
            c.Name.Trim().Length > 0 &&
            trimmed.Contains(c.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (contained is not null)
            return contained;

        // 3) Имя базы содержится в имени типовой конфигурации.
        return _configs.FirstOrDefault(c =>
            trimmed.Length > 0 &&
            c.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Выбирает редакцию по префиксу версии базы («3.0.142.32» → редакция «3.0»).</summary>
    private void TrySelectEditionByVersion(OneCConfigType config, string version)
    {
        var ver = version?.Trim() ?? string.Empty;
        if (ver.Length == 0 || config.Editions.Count == 0)
            return;

        var edition = config.Editions.FirstOrDefault(ed =>
            !string.IsNullOrWhiteSpace(ed.Red) &&
            (ver.Equals(ed.Red.Trim(), StringComparison.OrdinalIgnoreCase) ||
             ver.StartsWith(ed.Red.Trim() + ".", StringComparison.OrdinalIgnoreCase)));
        if (edition is not null)
            EditionCombo.SelectedItem = edition;
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

        // Персональный сегмент (ник): если он совпадает с ником типовой конфигурации — не
        // закрепляем его за базой (пустое значение означает «наследовать ник конфигурации»).
        var segment = SegmentBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(config.Nick) &&
            string.Equals(segment, config.Nick.Trim(), StringComparison.OrdinalIgnoreCase))
            segment = string.Empty;
        _infobase.UpdateUrlSegment = segment;

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
            match.UpdateUrlSegment = _infobase.UpdateUrlSegment;
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