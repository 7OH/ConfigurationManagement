#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management;

/// <summary>
/// Отдельное модальное окно создания/редактирования пользовательской типовой конфигурации 1С
/// (issue #321): код, наименование, сегмент URL, ник на releases.1c.ru и список редакций.
/// Работает на копии данных: результат возвращается через <see cref="Result"/> при
/// подтверждении и применяется вызывающим окном — правка одной строки не влияет на другие
/// конфигурации. Предопределённые конфигурации сюда не передаются (окно списка скрывает
/// для них кнопки «Изменить»/«Удалить»).
/// </summary>
public partial class ConfigTypeEditWindow : Window
{
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly List<OneCConfigEdition> _editions = new();
    private readonly string _originalCode;
    private readonly List<ViewModels.ItsAccountSelectionItem> _accountItems = new();

    /// <summary>Готовая конфигурация при подтверждении, иначе <c>null</c>.</summary>
    public OneCConfigType? Result { get; private set; }

    /// <param name="model">Редактируемая конфигурация или <c>null</c> для новой.</param>
    public ConfigTypeEditWindow(OneCConfigType? model = null)
    {
        InitializeComponent();

        _originalCode = model?.Code ?? string.Empty;
        var isNew = model is null;

        EditorTitle.Text = isNew
            ? LocalizationManager.T("Updates.AddConfig")
            : LocalizationManager.T("Updates.EditConfig");
        Title = EditorTitle.Text;

        CodeBox.Text = model?.Code ?? string.Empty;
        NameBox.Text = model?.Name ?? string.Empty;
        UrlCodeBox.Text = model?.UrlCode ?? string.Empty;
        NickBox.Text = model?.Nick ?? string.Empty;

        // Учётная запись ИТС (issue #333): «Основная» + записи справочника.
        _accountItems.AddRange(ViewModels.ItsAccountSelectionBuilder.Build(
            AppServices.GetRequiredService<IItsAccountsStore>()));
        AccountCombo.ItemsSource = _accountItems;
        AccountCombo.SelectedIndex = Math.Max(0, Math.Min(
            ViewModels.ItsAccountSelectionBuilder.IndexOf(_accountItems, model?.AccountId),
            _accountItems.Count - 1));

        if (model is not null)
            _editions.AddRange(model.Editions);
        EditionsList.ItemsSource = _editions;

        // Фокус в поле «Наименование» (issue #299): отложенный вызов после показа окна —
        // иначе при ShowDialog() фокус «съедается» до активации окна.
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    NameBox.Focus();
                    if (isNew)
                        NameBox.SelectAll();
                }));
        };
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        CommitEditionFields();

        var name = NameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _dialogs.ShowWarning(LocalizationManager.T("Updates.NameRequired"),
                LocalizationManager.T("Updates.EditConfig"));
            NameBox.Focus();
            return;
        }

        var code = CodeBox.Text?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            // Для связи ИБ ↔ конфигурация нужен стабильный код: сохраняем прежний,
            // а для новой конфигурации генерируем из наименования.
            code = _originalCode.Length > 0 ? _originalCode : GenerateCode(name);
        }

        var selectedAccount = AccountCombo.SelectedItem as ViewModels.ItsAccountSelectionItem;

        Result = new OneCConfigType
        {
            Code = code,
            Name = name,
            UrlCode = UrlCodeBox.Text?.Trim() ?? string.Empty,
            Nick = NickBox.Text?.Trim() ?? string.Empty,
            // Учётная запись ИТС: пусто — «Основная» (либо выбранная в настройках).
            AccountId = selectedAccount?.Id ?? string.Empty,
            IsBuiltIn = false,
            Editions = new List<OneCConfigEdition>(_editions),
        };
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
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

    /// <summary>Генерирует стабильный код из наименования (латиница/цифры/подчёркивания).</summary>
    private static string GenerateCode(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            if (ch < 128 && (char.IsLetterOrDigit(ch)))
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