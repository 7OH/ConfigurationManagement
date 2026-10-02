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
/// Окно «Типовые конфигурации» (issue #321): список предопределённых и пользовательских
/// конфигураций 1С. Добавление/правка выполняются в отдельном модальном окне
/// <see cref="ConfigTypeEditWindow"/>, «Закрыть» закрывает только список. Изменения
/// сохраняются сразу в файл <c>custom_config_types.json</c> через
/// <see cref="ICustomConfigTypesStore"/> и переживают перезапуск. Предопределённые
/// конфигурации можно дополнить правкой (создаётся пользовательская копия-переопределение,
/// статический набор <see cref="BuiltInConfigTypes"/> не мутируется), но не удалить;
/// кнопка «Восстановить типовые» возвращает предопределённый набор к исходному виду.
/// Несколько записей одной конфигурации (ЗУП 3.0 и 3.1) сосуществуют.
/// </summary>
public partial class ConfigTypesEditWindow : Window
{
    private readonly ICustomConfigTypesStore _store = AppServices.GetRequiredService<ICustomConfigTypesStore>();
    private readonly IItsAccountsStore _itsAccounts = AppServices.GetRequiredService<IItsAccountsStore>();
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

        // Закрытие окна по Esc (issue #265); DEL удаляет выделенную пользовательскую строку
        // (issue #321: раньше клавиша только снимала выделение).
        PreviewKeyDown += OnWindow_PreviewKeyDown;
    }

    /// <summary>Обрабатывает Esc (закрыть окно) и DEL (удалить выбранную пользовательскую строку).</summary>
    private void OnWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None
            && ConfigGrid.SelectedItem is ConfigTypeItemViewModel row && !row.IsBuiltIn)
        {
            e.Handled = true;
            OnDeleteRow(row);
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

    /// <summary>Формирует строки таблицы единым правилом
    /// <see cref="CustomConfigTypesStore.MergeAll"/>: пользовательская копия предопределённой
    /// заменяет встроенную с тем же кодом — дублей строк после правки встроенной не возникает
    /// (issue #321); обычные пользовательские записи добавляются следом.</summary>
    private void RebuildRows()
    {
        _rows.Clear();
        foreach (var ct in CustomConfigTypesStore.MergeAll(BuiltInConfigTypes.All, _customTypes))
            AddRow(ct);
    }

    private void AddRow(OneCConfigType config)
    {
        _rows.Add(new ConfigTypeItemViewModel(config, OnEditRow, OnDeleteRow, _itsAccounts));
    }

    /// <summary>
    /// Открывает отдельное окно правки конфигурации. Правка предопределённой строки создаёт
    /// пользовательскую копию-переопределение с тем же кодом (статические экземпляры
    /// <see cref="BuiltInConfigTypes"/> не мутируются — issue #321); обычная пользовательская
    /// строка правится на месте.
    /// </summary>
    private void OnEditRow(ConfigTypeItemViewModel row)
    {
        var edit = new ConfigTypeEditWindow(CloneType(row.Model)) { Owner = this };
        if (edit.ShowDialog() != true || edit.Result is not { } updated)
            return; // Отмена — модель не изменялась (правка велась на копии).

        if (row.IsBuiltIn)
        {
            // Правка встроенной строки: сохраняем как пользовательскую копию (тот же код),
            // которая в общем списке LoadAll() заменяет предопределённую.
            updated.IsBuiltIn = false;
            updated.OverridesBuiltIn = true;
            _customTypes.RemoveAll(c => SameCode(c.Code, updated.Code) && c.OverridesBuiltIn);
            _customTypes.Add(updated);
        }
        else
        {
            ApplyTo(row.Model, updated);
        }

        Save();
        RebuildRows();
    }

    /// <summary>Открывает отдельное окно создания новой пользовательской конфигурации.</summary>
    private void OnAddConfigClick(object sender, RoutedEventArgs e)
    {
        var edit = new ConfigTypeEditWindow() { Owner = this };
        if (edit.ShowDialog() != true || edit.Result is not { } created)
            return;

        created.IsBuiltIn = false;
        created.IsTracked = true;
        created.OverridesBuiltIn = false;

        // Уникальность по составному ключу «наименование + редакции» (issue #321): несколько
        // записей одной конфигурации допустимы (ЗУП 3.0 и 3.1), точные дубли — нет.
        if (HasDuplicate(created))
        {
            _dialogs.ShowWarning(LocalizationManager.T("Updates.ConfigExists"),
                LocalizationManager.T("Updates.ConfigTypesTitle"));
            return;
        }

        _customTypes.Add(created);
        Save();
        RebuildRows();
    }

    private void OnDeleteRow(ConfigTypeItemViewModel row)
    {
        if (row.IsBuiltIn)
            return; // Предопределённые конфигурации нельзя удалить (только восстановить).

        if (!_dialogs.Confirm(LocalizationManager.T("Updates.ConfirmDelete"),
                LocalizationManager.T("Updates.ConfigTypesTitle")))
            return;
        try
        {
            _customTypes.Remove(row.Model);
            Save();
            RebuildRows();
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка удаления конфигурации «{row.Name}»", ex);
        }
    }

    /// <summary>
    /// «Восстановить типовые» (issue #321): удаляет пользовательские копии предопределённых
    /// конфигураций (<see cref="OneCConfigType.OverridesBuiltIn"/>) — предопределённый набор
    /// возвращается к <see cref="BuiltInConfigTypes.All"/>, пользовательские записи не трогаются.
    /// </summary>
    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (_customTypes.Count == 0 || !_customTypes.Any(c => c.OverridesBuiltIn))
        {
            _dialogs.ShowInfo(LocalizationManager.T("Updates.NothingToRestore"),
                LocalizationManager.T("Updates.ConfigTypesTitle"));
            return;
        }

        if (!_dialogs.Confirm(LocalizationManager.T("Updates.RestoreDefaultsConfirm"),
                LocalizationManager.T("Updates.ConfigTypesTitle")))
            return;
        try
        {
            _store.RestoreDefaults();
            LoadCustomTypes();
            RebuildRows();
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка восстановления предопределённого набора типовых конфигураций", ex);
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
        target.AccountId = source.AccountId ?? string.Empty;
        target.Editions.Clear();
        target.Editions.AddRange(source.Editions);
    }

    /// <summary>Глубокая копия конфигурации (для правки без мутации исходного экземпляра).</summary>
    private static OneCConfigType CloneType(OneCConfigType source) => new()
    {
        Code = source.Code,
        Name = source.Name,
        UrlCode = source.UrlCode,
        Nick = source.Nick,
        AccountId = source.AccountId ?? string.Empty,
        IsBuiltIn = source.IsBuiltIn,
        IsTracked = source.IsTracked,
        OverridesBuiltIn = source.OverridesBuiltIn,
        Editions = source.Editions.Select(e => new OneCConfigEdition
        {
            Name = e.Name,
            Red = e.Red,
            SubRed = e.SubRed,
            UrlOverride = e.UrlOverride,
        }).ToList(),
    };

    /// <summary>Сравнивает коды конфигураций без учёта регистра.</summary>
    private static bool SameCode(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Проверка дубля по составному ключу «наименование + редакции» (issue #321): дублем
    /// считается запись с тем же наименованием и тем же набором редакций (в любом порядке).
    /// Разные редакции одной конфигурации (ЗУП 3.0 и 3.1) дублями не считаются.
    /// </summary>
    private bool HasDuplicate(OneCConfigType candidate)
    {
        // Проверяем только наименование + набор редакций: несколько записей одной конфигурации
        // с разными редакциями (ЗУП 3.0 и 3.1) допустимы, точные дубли — нет.
        foreach (var existing in BuiltInConfigTypes.All.Concat(_customTypes))
        {
            if (string.Equals(existing.Name.Trim(), candidate.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                && SameEditions(existing.Editions, candidate.Editions))
                return true;
        }
        return false;
    }

    private static bool SameEditions(IReadOnlyCollection<OneCConfigEdition> a, IReadOnlyCollection<OneCConfigEdition> b)
    {
        if (a.Count != b.Count)
            return false;
        var namesA = a.Select(e => e.Name.Trim()).Where(n => n.Length > 0)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var namesB = b.Select(e => e.Name.Trim()).Where(n => n.Length > 0)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        return namesA.SequenceEqual(namesB, StringComparer.OrdinalIgnoreCase);
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
#endif