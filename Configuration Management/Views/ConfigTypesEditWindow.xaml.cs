#if WINDOWS
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно редактирования списка типовых конфигураций 1С: позволяет добавить, изменить и удалить
/// пользовательские конфигурации (имя, сегмент URL и редакции/каталоги релизов). Предопределённые
/// конфигурации из <see cref="BuiltInConfigTypes"/> показываются только для чтения. Список
/// пользовательских конфигураций загружается из и сохраняется в <see cref="AppSettings.CustomConfigTypes"/>
/// через репозиторий при закрытии окна.
/// </summary>
public partial class ConfigTypesEditWindow : Window
{
    private readonly IInfobaseRepository _repository = AppServices.GetRequiredService<IInfobaseRepository>();
    private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    private readonly List<OneCConfigType> _customTypes = new();
    private readonly ObservableCollection<ConfigTypeItemViewModel> _rows = new();
    private readonly ObservableCollection<OneCConfigEdition> _editions = new();

    private ConfigTypeItemViewModel? _editingRow;
    private bool _editingIsNew;

    /// <summary>
    /// Открывает окно редактирования списка типовых конфигураций.
    /// </summary>
    /// <param name="initialCustomTypes">Необязательные начальные пользовательские конфигурации.
    /// Если не заданы — загружаются из настроек репозитория.</param>
    public ConfigTypesEditWindow(IEnumerable<OneCConfigType>? initialCustomTypes = null)
    {
        InitializeComponent();
        ConfigGrid.ItemsSource = _rows;
        EditionsList.ItemsSource = _editions;

        if (initialCustomTypes is not null)
        {
            foreach (var ct in initialCustomTypes)
                _customTypes.Add(ct);
        }
        else
        {
            LoadCustomTypes();
        }

        RebuildRows();
        Closing += (_, _) => SaveCustomTypes();

        // Закрытие окна по Esc (issue #265): единообразно с Avalonia-базой ModalWindowBase.
        PreviewKeyDown += OnWindow_PreviewKeyDown;
    }

    /// <summary>Закрывает окно по Esc без модификаторов (issue #265).</summary>
    private void OnWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Close();
        }
    }

    private void LoadCustomTypes()
    {
        try
        {
            var settings = _repository.LoadSettings();
            _customTypes.Clear();
            foreach (var ct in settings.CustomConfigTypes ?? new List<OneCConfigType>())
                _customTypes.Add(ct);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка загрузки списка пользовательских типовых конфигураций", ex);
        }
    }

    /// <summary>Формирует строки таблицы: сначала предопределённые (только для чтения), затем пользовательские.</summary>
    private void RebuildRows()
    {
        _rows.Clear();
        foreach (var ct in BuiltInConfigTypes.All)
            AddRow(ct);
        foreach (var ct in _customTypes)
            AddRow(ct);
    }

    private void AddRow(OneCConfigType config)
    {
        _rows.Add(new ConfigTypeItemViewModel(config, OnEditRow, OnDeleteRow));
    }

    private void OnEditRow(ConfigTypeItemViewModel row)
    {
        OpenEditor(row, isNew: false);
    }

    private void OnDeleteRow(ConfigTypeItemViewModel row)
    {
        if (!_dialogs.Confirm(LocalizationManager.T("Updates.ConfirmDelete"),
                LocalizationManager.T("Updates.ConfigTypesTitle")))
            return;
        try
        {
            _customTypes.Remove(row.Model);
            _rows.Remove(row);
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка удаления конфигурации «{row.Name}»", ex);
        }
    }

    private void OnAddConfigClick(object sender, RoutedEventArgs e)
    {
        var config = new OneCConfigType { IsBuiltIn = false, IsTracked = true };
        _customTypes.Add(config);
        var row = new ConfigTypeItemViewModel(config, OnEditRow, OnDeleteRow);
        _rows.Add(row);
        OpenEditor(row, isNew: true);
    }

    private void OpenEditor(ConfigTypeItemViewModel row, bool isNew)
    {
        _editingRow = row;
        _editingIsNew = isNew;

        EditorTitle.Text = isNew
            ? LocalizationManager.T("Updates.AddConfig")
            : LocalizationManager.T("Updates.EditConfig");

        NameBox.Text = row.Model.Name;
        UrlCodeBox.Text = row.Model.UrlCode;

        _editions.Clear();
        foreach (var ed in row.Model.Editions)
            _editions.Add(ed);
        EditionNameBox.Text = string.Empty;
        EditionRedBox.Text = string.Empty;
        EditionSubRedBox.Text = string.Empty;
        EditionUrlOverrideBox.Text = string.Empty;

        ConfigGrid.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        NameBox.Focus();
    }

    private void OnEditorSaveClick(object sender, RoutedEventArgs e)
    {
        if (_editingRow is null)
            return;

        CommitEditionFields();

        var name = NameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _dialogs.ShowWarning(LocalizationManager.T("Updates.NoConfigSelected"),
                LocalizationManager.T("Updates.ConfigTypesTitle"));
            NameBox.Focus();
            return;
        }

        var model = _editingRow.Model;
        model.Name = name;
        model.UrlCode = UrlCodeBox.Text?.Trim() ?? string.Empty;
        model.Editions.Clear();
        foreach (var ed in _editions)
            model.Editions.Add(ed);

        _editingRow.Refresh();
        CloseEditor();
    }

    private void OnEditorCancelClick(object sender, RoutedEventArgs e)
    {
        if (_editingIsNew && _editingRow is not null)
        {
            // Новая конфигурация, добавленная на «Добавить», но не сохранённая — убрать её.
            _customTypes.Remove(_editingRow.Model);
            _rows.Remove(_editingRow);
        }
        CloseEditor();
    }

    private void CloseEditor()
    {
        _editingRow = null;
        _editingIsNew = false;
        ConfigGrid.Visibility = Visibility.Visible;
        EditorPanel.Visibility = Visibility.Collapsed;
    }

    private void OnAddEditionClick(object sender, RoutedEventArgs e)
    {
        CommitEditionFields();
        var edition = new OneCConfigEdition { Name = LocalizationManager.T("Updates.Name") };
        _editions.Add(edition);
        EditionsList.SelectedItem = edition;
    }

    private void OnRemoveEditionClick(object sender, RoutedEventArgs e)
    {
        if (EditionsList.SelectedItem is not OneCConfigEdition edition)
            return;
        var index = _editions.IndexOf(edition);
        _editions.Remove(edition);
        EditionNameBox.Text = string.Empty;
        EditionRedBox.Text = string.Empty;
        EditionSubRedBox.Text = string.Empty;
        EditionUrlOverrideBox.Text = string.Empty;
        if (_editions.Count > 0)
            EditionsList.SelectedIndex = Math.Min(index, _editions.Count - 1);
    }

    private void OnEditionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitEditionFields();
        if (EditionsList.SelectedItem is OneCConfigEdition edition)
            LoadEditionFields(edition);
        else
        {
            EditionNameBox.Text = string.Empty;
            EditionRedBox.Text = string.Empty;
            EditionSubRedBox.Text = string.Empty;
            EditionUrlOverrideBox.Text = string.Empty;
        }
    }

    /// <summary>Переносит значения полей редактора в выбранную редакцию.</summary>
    private void CommitEditionFields()
    {
        if (EditionsList.SelectedItem is not OneCConfigEdition edition)
            return;
        edition.Name = EditionNameBox.Text?.Trim() ?? string.Empty;
        edition.Red = EditionRedBox.Text?.Trim() ?? string.Empty;
        edition.SubRed = EditionSubRedBox.Text?.Trim() ?? string.Empty;
        edition.UrlOverride = EditionUrlOverrideBox.Text?.Trim() ?? string.Empty;
    }

    private void LoadEditionFields(OneCConfigEdition edition)
    {
        EditionNameBox.Text = edition.Name;
        EditionRedBox.Text = edition.Red;
        EditionSubRedBox.Text = edition.SubRed;
        EditionUrlOverrideBox.Text = edition.UrlOverride;
    }

    private void SaveCustomTypes()
    {
        try
        {
            var settings = _repository.LoadSettings();
            settings.CustomConfigTypes = new List<OneCConfigType>(_customTypes);
            _repository.SaveSettings(settings);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка сохранения списка типовых конфигураций", ex);
        }
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
#endif