#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

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
    private readonly ICustomConfigTypesStore _store = AppServices.GetRequiredService<ICustomConfigTypesStore>();
    private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    private readonly Infobase _infobase;
    private readonly List<OneCConfigType> _configs = new();

    /// <summary>Строки выпадающего списка «Конфигурация» (обёртки с пометкой происхождения,
    /// issue #322) — рабочий список остаётся в <see cref="_configs"/>.</summary>
    private readonly List<ConfigLinkItemViewModel> _configItems = new();

    private bool _initializing = true;
    private bool _definingVersion;

    /// <param name="infobase">Информационная база, для которой настраивается связь с конфигурацией.</param>
    public ConfigUpdateLinkWindow(Infobase infobase)
    {
        _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));

        InitializeComponent();
        BaseNameText.Text = _infobase.Name;

        LoadConfigs();
        ConfigCombo.ItemsSource = _configItems;
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

        // Автоопределение при открытии (issue #322): если у базы уже определены свойства
        // конфигурации (вкладка «Платформа» / «Определить версию»), подставляем совпавшую
        // типовую конфигурацию сразу — связь строится из свойств, ручной выбор — явный override.
        if (!string.IsNullOrWhiteSpace(_infobase.ConfigurationName))
            TryAutoMatchConfig(_infobase.ConfigurationName, _infobase.ConfigurationVersion);
    }

    private void LoadConfigs()
    {
        try
        {
            // Общий список типовых = встроенные + пользовательские из файла
            // custom_config_types.json (единый загрузчик — issue #321).
            _configs.Clear();
            _configs.AddRange(_store.LoadAll());
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка загрузки списка типовых конфигураций для связи ИБ", ex);
            _configs.Clear();
            _configs.AddRange(BuiltInConfigTypes.All);
        }

        _configItems.Clear();
        _configItems.AddRange(_configs.Select(c => new ConfigLinkItemViewModel(c)));
    }

    private void SelectInitialConfig()
    {
        var code = _infobase.UpdateConfigCode;
        ConfigLinkItemViewModel? selected = null;
        if (!string.IsNullOrWhiteSpace(code))
            selected = _configItems.FirstOrDefault(i =>
                string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));

        ConfigCombo.SelectedItem = selected ?? (_configItems.Count > 0 ? _configItems[0] : null);
    }

    /// <summary>Выбранная в списке типовая конфигурация (через обёртку строки списка).</summary>
    private OneCConfigType? SelectedConfig => (ConfigCombo.SelectedItem as ConfigLinkItemViewModel)?.Model;

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
        var config = SelectedConfig;
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
        var config = SelectedConfig;
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
        var code = SelectedConfig?.Code;
        LoadConfigs();
        ConfigCombo.ItemsSource = _configItems;
        var restored = _configItems.FirstOrDefault(i =>
            string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));
        ConfigCombo.SelectedItem = restored ?? (_configItems.Count > 0 ? _configItems[0] : null);
        ApplyConfigSelection(rebuildUrl: true);
    }

    /// <summary>Пересчитывает адрес каталога релизов в автоматическом режиме.</summary>
    private void RebuildUrl()
    {
        if (_initializing)
            return;
        if (ManualUrlRadio.IsChecked == true)
            return;

        var config = SelectedConfig;
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
        var result = FindConfigByInfobaseName(configName);
        if (result is null)
        {
            ResultText.Text = string.Format(LocalizationManager.T("Updates.ConfigNotMatched"), configName);
            return;
        }

        var match = result.Config;
        ConfigCombo.SelectedItem = _configItems.FirstOrDefault(i => ReferenceEquals(i.Model, match));
        ApplyConfigSelection(rebuildUrl: true);
        TrySelectEditionByVersion(match, version);

        // Поясняем, по какому полю найдена запись (issue #322): точное имя / внутреннее имя
        // конфигурации / сегмент адреса / вхождение имени — чтобы было видно, почему выбрана
        // именно эта запись.
        var reason = result.Kind switch
        {
            ConfigMatchKind.ExactUrlCode => LocalizationManager.T("Updates.ReasonUrlCode"),
            ConfigMatchKind.ExactConfigName => LocalizationManager.T("Updates.ReasonConfigName"),
            ConfigMatchKind.NameContainedInBaseName => LocalizationManager.T("Updates.ReasonNameContains"),
            ConfigMatchKind.BaseNameContainedInConfigName => LocalizationManager.T("Updates.ReasonBaseContains"),
            _ => LocalizationManager.T("Updates.ReasonExactName"),
        };
        ResultText.Text = string.Format(LocalizationManager.T("Updates.ConfigMatchedReason"), match.Name, reason);
    }

    private ConfigMatchResult? FindConfigByInfobaseName(string configName) =>
        ConfigTypeMatcher.FindMatch(_configs, configName);


    /// <summary>Выбирает редакцию по префиксу версии базы («3.0.142.32» → редакция «3.0») —
    /// единая логика в <see cref="ConfigTypeMatcher.FindEditionByVersion"/> (issue #346).</summary>
    private void TrySelectEditionByVersion(OneCConfigType config, string version)
    {
        var edition = ConfigTypeMatcher.FindEditionByVersion(config, version);
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
        var config = SelectedConfig;
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