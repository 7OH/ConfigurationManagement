#if WINDOWS
using System;
using System.Windows;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно справочника «Учетные данные ИТС» (issue #333): список учётных записей с кнопками
/// «Добавить», «Изменить», «Удалить», «Задать основным». Флажок «Основная» в списке —
/// только для чтения; смена основной выполняется ТОЛЬКО кнопкой «Задать основным»
/// (нельзя поставить две основные из UI). Правка/добавление — в отдельном модальном
/// окне <see cref="ItsAccountEditWindow"/> с фокусом на первом поле.
/// Изменения сохраняются сразу в файл <c>its_accounts.json</c> через
/// <see cref="IItsAccountsStore"/>.
/// </summary>
public partial class ItsAccountsWindow : Window
{
    private readonly IItsAccountsStore _store = AppServices.GetRequiredService<IItsAccountsStore>();
    private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly ItsAccountsViewModel _viewModel;

    public ItsAccountsWindow()
    {
        InitializeComponent();
        _viewModel = new ItsAccountsViewModel(_store);
        _viewModel.EditRequested += OnEditRow;
        _viewModel.DeleteRequested += OnDeleteRow;
        AccountsGrid.ItemsSource = _viewModel.Rows;

        // Закрытие окна по Esc (issue #265).
        PreviewKeyDown += OnWindow_PreviewKeyDown;
    }

    private void OnWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>Открывает модальный редактор новой учётной записи.</summary>
    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var edit = new ItsAccountEditWindow() { Owner = this };
        if (edit.ShowDialog() != true || edit.Result is not { } created)
            return;

        try
        {
            _viewModel.Add(created);
            _logger.Info($"[ITS] Добавлена учётная запись «{MaskName(created)}».");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка добавления учётной записи ИТС", ex);
        }
    }

    /// <summary>Открывает модальный редактор выбранной записи (правка на копии).</summary>
    private void OnEditRow(ItsAccountItemViewModel row)
    {
        var edit = new ItsAccountEditWindow(CloneAccount(row.Model)) { Owner = this };
        if (edit.ShowDialog() != true || edit.Result is not { } updated)
            return; // Отмена — модель не изменялась.

        try
        {
            _viewModel.Update(updated);
            _logger.Info($"[ITS] Изменена учётная запись «{MaskName(updated)}».");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка изменения учётной записи ИТС", ex);
        }
    }

    /// <summary>Удаляет запись с подтверждением; основная после удаления — первая запись списка.</summary>
    private void OnDeleteRow(ItsAccountItemViewModel row)
    {
        if (!_dialogs.Confirm(LocalizationManager.T("ItsAccounts.ConfirmDelete"),
                LocalizationManager.T("ItsAccounts.Title")))
            return;

        try
        {
            var wasPrimary = row.IsPrimary;
            _viewModel.Delete(row.Id);
            _logger.Info(wasPrimary
                ? "[ITS] Удалена основная учётная запись — основной стала первая запись списка."
                : $"[ITS] Удалена учётная запись «{MaskName(row.Model)}».");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка удаления учётной записи ИТС", ex);
        }
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>Глубокая копия записи (для правки без мутации исходного экземпляра).</summary>
    private static ItsAccount CloneAccount(ItsAccount source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Login = source.Login,
        Password = source.Password,
        IsPrimary = source.IsPrimary,
    };

    /// <summary>Маскирует пароль при журналировании (issue #333): пароль не попадает в лог.</summary>
    private static string MaskName(ItsAccount account)
        => ItsAccountsStore.MaskPasswordForLog(account.Name ?? string.Empty, account.Password);
}
#endif