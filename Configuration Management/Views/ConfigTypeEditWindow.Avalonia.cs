#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management;

/// <summary>
/// Отдельное модальное окно создания/редактирования пользовательской типовой конфигурации 1С
/// (issue #321): код, наименование, сегмент URL, ник на releases.1c.ru и список редакций.
/// Работает на копии данных: результат возвращается через <see cref="Result"/> при
/// подтверждении — правка одной строки не влияет на другие конфигурации. Предопределённые
/// конфигурации сюда не передаются (окно списка скрывает для них кнопки «Изменить»/«Удалить»).
/// Avalonia/Linux-версия WPF-окна <see cref="ConfigTypeEditWindow"/>.
/// </summary>
public sealed class ConfigTypeEditWindow : ModalWindowBase
{
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly List<OneCConfigEdition> _editions = new();
    private readonly string _originalCode;

    private readonly TextBox _codeBox = MakeTextBox(string.Empty);
    private readonly TextBox _nameBox = MakeTextBox(string.Empty);
    private readonly TextBox _urlCodeBox = MakeTextBox(string.Empty);
    private readonly TextBox _nickBox = MakeTextBox(string.Empty);
    private readonly ComboBox _accountBox = new() { Height = 32, DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ViewModels.ItsAccountSelectionItem.Name)) };
    private readonly List<ViewModels.ItsAccountSelectionItem> _accountItems = new();
    private readonly ComboBox _editionsList = new();
    private readonly TextBox _editionNameBox = MakeTextBox(string.Empty);
    private readonly TextBox _editionRedBox = MakeTextBox(string.Empty);
    private readonly TextBox _editionSubRedBox = MakeTextBox(string.Empty);
    private readonly TextBox _editionUrlOverrideBox = MakeTextBox(string.Empty);

    private bool _editionSyncing;

    /// <summary>Готовая конфигурация при подтверждении, иначе <c>null</c>.</summary>
    public OneCConfigType? Result { get; private set; }

    /// <param name="model">Редактируемая конфигурация или <c>null</c> для новой.</param>
    public ConfigTypeEditWindow(OneCConfigType? model = null)
    {
        _originalCode = model?.Code ?? string.Empty;
        var isNew = model is null;

        Title = T(isNew ? "Updates.AddConfig" : "Updates.EditConfig");
        Width = 680;
        Height = 560;
        MinWidth = 560;
        MinHeight = 480;
        FontSize = 13;
        CanResize = true;

        _codeBox.Text = model?.Code ?? string.Empty;
        _nameBox.Text = model?.Name ?? string.Empty;
        _urlCodeBox.Text = model?.UrlCode ?? string.Empty;
        _nickBox.Text = model?.Nick ?? string.Empty;

        // Учётная запись ИТС (issue #333): записи справочника; виртуальный пункт «Основная»
        // добавляется только если в справочнике нет реальной записи с таким именем.
        _accountItems.AddRange(ViewModels.ItsAccountSelectionBuilder.Build(
            AppServices.GetRequiredService<Services.IItsAccountsStore>()));
        _accountBox.ItemsSource = _accountItems;
        var accountIndex = ViewModels.ItsAccountSelectionBuilder.IndexOf(_accountItems, model?.AccountId);
        _accountBox.SelectedIndex = accountIndex >= 0 ? accountIndex : 0;

        if (model is not null)
            _editions.AddRange(model.Editions);
        if (_editions.Count > 0)
            _editionsList.SelectedIndex = 0;
        ClearEditionFields();

        Content = BuildRoot();

        // Фокус в поле «Наименование» (issue #299): отложенно после показа окна —
        // синхронная установка в Opened слетает до активации модального диалога.
        Opened += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _nameBox.Focus();
                if (isNew)
                    _nameBox.SelectAll();
            }, Avalonia.Threading.DispatcherPriority.Background);
        };
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    private Control BuildRoot()
    {
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };

        var title = new TextBlock
        {
            Text = Title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        };
        panel.Children.Add(title);

        panel.Children.Add(MakeFieldRow(T("Updates.ConfigCode"), _codeBox));
        panel.Children.Add(MakeFieldRow(T("Updates.Name"), _nameBox));
        panel.Children.Add(MakeFieldRow(T("Updates.UrlCode"), _urlCodeBox));
        panel.Children.Add(MakeFieldRow(T("Updates.Nick"), _nickBox));
        panel.Children.Add(MakeFieldRow(T("ItsAccounts.AccountLabel"), _accountBox));

        // Список редакций и поля выбранной редакции.
        _editionsList.ItemsSource = _editions;
        _editionsList.HorizontalAlignment = HorizontalAlignment.Stretch;
        _editionsList.MinHeight = 34;
        _editionsList.SelectionChanged += OnEditionSelectionChanged;

        var editionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var addEd = new Button { Content = T("Updates.Add"), Height = 30 };
        addEd.Styled(ControlThemes.SelectAllButton);
        addEd.Click += (_, _) => OnAddEditionClick();
        var removeEd = new Button { Content = T("Updates.Delete"), Height = 30 };
        removeEd.Styled(ControlThemes.SelectAllButton);
        removeEd.Click += (_, _) => OnRemoveEditionClick();
        editionButtons.Children.Add(addEd);
        editionButtons.Children.Add(removeEd);

        var editionRow = new Grid();
        editionRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        editionRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(_editionsList, 0);
        editionRow.Children.Add(_editionsList);
        Grid.SetColumn(editionButtons, 1);
        editionButtons.Margin = new Thickness(8, 0, 0, 0);
        editionRow.Children.Add(editionButtons);
        panel.Children.Add(MakeFieldRow(T("Updates.Editions"), editionRow));

        // Поля редакции одним рядом: имя, «Ред», «Подред» и переопределённый URL.
        _editionNameBox.Watermark = T("Updates.Name");
        _editionRedBox.Watermark = T("Updates.EditionRed");
        _editionSubRedBox.Watermark = T("Updates.EditionSubRed");
        _editionUrlOverrideBox.Watermark = T("Updates.UrlOverride");
        panel.Children.Add(BuildEditionFieldsRow());

        // Кнопки: Сохранить / Отмена.
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var save = new Button { Content = T("Updates.Save"), Width = 130, Height = 36, IsDefault = true };
        save.Styled(ControlThemes.DialogConfirmButton);
        save.Click += (_, _) => OnSaveClick();
        var cancel = BuildCancelActionButton(130);
        cancel.Click += (_, _) => OnCancelClick();
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        return panel;
    }

    private void OnSaveClick()
    {
        CommitEditionFields();

        var name = _nameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _dialogs.ShowWarning(T("Updates.NameRequired"), T("Updates.EditConfig"));
            _nameBox.Focus();
            return;
        }

        var code = _codeBox.Text?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            // Для связи ИБ ↔ конфигурация нужен стабильный код: сохраняем прежний,
            // а для новой конфигурации генерируем из наименования.
            code = _originalCode.Length > 0 ? _originalCode : GenerateCode(name);
        }

        var selectedAccount = _accountBox.SelectedItem as ViewModels.ItsAccountSelectionItem;

        Result = new OneCConfigType
        {
            Code = code,
            Name = name,
            UrlCode = _urlCodeBox.Text?.Trim() ?? string.Empty,
            Nick = _nickBox.Text?.Trim() ?? string.Empty,
            // Учётная запись ИТС: пусто — «Основная» (либо выбранная в настройках).
            AccountId = selectedAccount?.Id ?? string.Empty,
            IsBuiltIn = false,
            Editions = new List<OneCConfigEdition>(_editions),
        };
        DialogResult = true;
        Close();
    }

    private void OnCancelClick()
    {
        DialogResult = false;
        Close();
    }

    private void OnAddEditionClick()
    {
        CommitEditionFields();
        var edition = new OneCConfigEdition { Name = T("Updates.Name") };
        _editions.Add(edition);
        _editionsList.SelectedIndex = _editions.Count - 1;
    }

    private void OnRemoveEditionClick()
    {
        if (_editionsList.SelectedItem is not OneCConfigEdition edition)
            return;
        var index = _editions.IndexOf(edition);
        _editions.Remove(edition);
        ClearEditionFields();
        if (_editions.Count > 0)
            _editionsList.SelectedIndex = Math.Min(index, _editions.Count - 1);
    }

    private void OnEditionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_editionSyncing)
            return;
        CommitEditionFields();
        if (_editionsList.SelectedItem is OneCConfigEdition edition)
            LoadEditionFields(edition);
        else
            ClearEditionFields();
    }

    /// <summary>Переносит значения полей редактора в выбранную редакцию.</summary>
    private void CommitEditionFields()
    {
        if (_editionsList.SelectedItem is not OneCConfigEdition edition)
            return;
        edition.Name = _editionNameBox.Text?.Trim() ?? string.Empty;
        edition.Red = _editionRedBox.Text?.Trim() ?? string.Empty;
        edition.SubRed = _editionSubRedBox.Text?.Trim() ?? string.Empty;
        edition.UrlOverride = _editionUrlOverrideBox.Text?.Trim() ?? string.Empty;
    }

    private void LoadEditionFields(OneCConfigEdition edition)
    {
        _editionSyncing = true;
        try
        {
            _editionNameBox.Text = edition.Name;
            _editionRedBox.Text = edition.Red;
            _editionSubRedBox.Text = edition.SubRed;
            _editionUrlOverrideBox.Text = edition.UrlOverride;
        }
        finally
        {
            _editionSyncing = false;
        }
    }

    private void ClearEditionFields()
    {
        _editionSyncing = true;
        try
        {
            _editionNameBox.Text = string.Empty;
            _editionRedBox.Text = string.Empty;
            _editionSubRedBox.Text = string.Empty;
            _editionUrlOverrideBox.Text = string.Empty;
        }
        finally
        {
            _editionSyncing = false;
        }
    }

    private static TextBox MakeTextBox(string watermark)
    {
        var tb = new TextBox
        {
            Watermark = watermark,
            Margin = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 34
        };
        tb.Styled(ControlThemes.ModernTextBox);
        return tb;
    }

    /// <summary>Строка «подпись / поле» формы редактора.</summary>
    private static Grid MakeFieldRow(string label, Control field)
    {
        var grid = new Grid { Margin = new Thickness(0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(170)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        return grid;
    }

    /// <summary>Ряд из четырёх полей редакции: имя, «Ред», «Подред» и переопределённый URL.</summary>
    private Grid BuildEditionFieldsRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 3) };
        for (var i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        var fields = new[] { _editionNameBox, _editionRedBox, _editionSubRedBox, _editionUrlOverrideBox };
        for (var i = 0; i < fields.Length; i++)
        {
            fields[i].Margin = i < fields.Length - 1 ? new Thickness(0, 0, 6, 0) : new Thickness(0);
            Grid.SetColumn(fields[i], i);
            grid.Children.Add(fields[i]);
        }
        return grid;
    }

    /// <summary>Генерирует стабильный код из наименования (латиница/цифры/подчёркивания).</summary>
    private static string GenerateCode(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name)
        {
            if (ch < 128 && char.IsLetterOrDigit(ch))
                sb.Append(char.ToUpperInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
            if (sb.Length >= 12)
                break;
        }
        var result = sb.ToString().Trim('_');
        return result.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : result;
    }
}
#endif