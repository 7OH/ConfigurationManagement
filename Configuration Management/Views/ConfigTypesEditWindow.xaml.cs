#if WINDOWS
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно «Типовые конфигурации» (issue #321): список предопределённых (только для чтения)
/// и пользовательских конфигураций 1С. Добавление/правка выполняются в отдельном модальном
/// окне <see cref="ConfigTypeEditWindow"/> — «Правка» больше не переключает это окно «два в
/// одном», а «Закрыть» закрывает только список. Изменения сохраняются сразу в файл
/// <c>custom_config_types.json</c> через <see cref="ICustomConfigTypesStore"/> и переживают
/// перезапуск. Предопределённые конфигурации из <see cref="BuiltInConfigTypes"/> нельзя
/// изменять или удалять (общие статические экземпляры — их мутация «расползалась» по другим
/// окнам: актуальные релизы, проверка обновлений, связь с конфигурацией).
/// </summary>
public partial class ConfigTypesEditWindow : Window
{
    private readonly ICustomConfigTypesStore _store = AppServices.GetRequiredService<ICustomConfigTypesStore>();
    private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    private readonly List<OneCConfigType> _customTypes = new();
    private readonly ObservableCollection<ConfigTypeItemViewModel> _rows = new();

    /// <summary>
    /// Открывает окно редактирования списка типовых конфигураций.
    /// </summary>
    public ConfigTypesEditWindow()
    {
        InitializeComponent();
        ConfigGrid.ItemsSource = _rows;

        LoadCustomTypes();
        RebuildRows();

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
            _customTypes.Clear();
            foreach (var ct in _store.Load())
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

    /// <summary>Открывает отдельное окно правки пользовательской конфигурации.</summary>
    private void OnEditRow(ConfigTypeItemViewModel row)
    {
        if (row.IsBuiltIn)
            return; // Предопределённые конфигурации только для чтения.

        var edit = new ConfigTypeEditWindow(row.Model) { Owner = this };
        if (edit.ShowDialog() != true || edit.Result is not { } updated)
            return; // Отмена — модель не изменялась (правка велась на копии).

        ApplyTo(row.Model, updated);
        row.Refresh();
        Save();
    }

    /// <summary>Открывает отдельное окно создания новой пользовательской конфигурации.</summary>
    private void OnAddConfigClick(object sender, RoutedEventArgs e)
    {
        var edit = new ConfigTypeEditWindow() { Owner = this };
        if (edit.ShowDialog() != true || edit.Result is not { } created)
            return;

        created.IsBuiltIn = false;
        created.IsTracked = true;
        _customTypes.Add(created);
        AddRow(created);
        Save();
    }

    private void OnDeleteRow(ConfigTypeItemViewModel row)
    {
        if (row.IsBuiltIn)
            return; // Предопределённые конфигурации нельзя удалить.

        if (!_dialogs.Confirm(LocalizationManager.T("Updates.ConfirmDelete"),
                LocalizationManager.T("Updates.ConfigTypesTitle")))
            return;
        try
        {
            _customTypes.Remove(row.Model);
            _rows.Remove(row);
            Save();
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка удаления конфигурации «{row.Name}»", ex);
        }
    }

    /// <summary>Сразу сохраняет пользовательские конфигурации в файл custom_config_types.json.</summary>
    private void Save()
    {
        try
        {
            _store.Save(_customTypes);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка сохранения списка типовых конфигураций", ex);
        }
    }

    /// <summary>Переносит отредактированную копию в модель строки (изменение одной строки не
    /// влияет на другие конфигурации — правка велась на отдельном экземпляре).</summary>
    private static void ApplyTo(OneCConfigType target, OneCConfigType source)
    {
        target.Code = source.Code;
        target.Name = source.Name;
        target.UrlCode = source.UrlCode;
        target.Nick = source.Nick;
        target.Editions.Clear();
        target.Editions.AddRange(source.Editions);
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
#endif