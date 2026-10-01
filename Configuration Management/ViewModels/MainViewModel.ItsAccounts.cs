using System.Collections.Generic;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Учётные записи ИТС 1С (issue #333): список записей справочника <c>its_accounts.json</c>
/// для выбора в настройках и команды открытия окна справочника. Частичный класс
/// <see cref="MainViewModel"/>. Платформенная часть (модальный показ окна) — в
/// MainViewModel.Updates.cs (Windows/WPF) и MainViewModel.Avalonia.Updates.cs (Linux/Avalonia).
/// </summary>
public partial class MainViewModel
{
    private List<ItsAccount> _itsAccounts = new();

    /// <summary>Учётные записи ИТС из справочника (для ComboBox в настройках и окнах).</summary>
    public IReadOnlyList<ItsAccount> ItsAccounts => _itsAccounts;

    /// <summary>
    /// Перечитывает список учётных записей ИТС из хранилища (вызывается при старте и после
    /// закрытия окна справочника, чтобы изменения сразу отразились в настройках).
    /// </summary>
    public void RefreshItsAccounts()
    {
        try
        {
            var store = AppServices.GetRequiredService<IItsAccountsStore>();
            _itsAccounts = store.Load().ToList();
        }
        catch
        {
            // Ошибка чтения справочника не должна ронять главное окно: список остаётся пустым.
            _itsAccounts = new List<ItsAccount>();
        }
        OnPropertyChanged(nameof(ItsAccounts));
    }
}