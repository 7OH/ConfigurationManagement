#if LINUX
using System.Windows.Input;
using Avalonia.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Команды и методы функции «Проверка обновлений конфигураций 1С» (функции №21/№22)
/// для Avalonia/Linux: F9 — проверка обновлений выбранной ИБ, ALT+F9 — окно
/// «Актуальные релизы». Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _checkUpdateCommand;
    private ICommand? _showActualReleasesCommand;

    /// <summary>Команда «Проверить обновления» для выбранной ИБ (F9).</summary>
    public ICommand CheckUpdateCommand =>
        _checkUpdateCommand ??= new RelayCommand(
            ExecuteCheckUpdate,
            () => SelectedInfobase is not null);

    /// <summary>Команда открытия окна «Актуальные релизы» (ALT+F9).</summary>
    public ICommand ShowActualReleasesCommand =>
        _showActualReleasesCommand ??= new RelayCommand(ExecuteShowActualReleases);

    /// <summary>Открывает окно проверки обновлений для выбранной ИБ (F9).</summary>
    private void ExecuteCheckUpdate()
    {
        if (SelectedInfobase is null)
        {
            _dialog.ShowInfo(
                LocalizationManager.T("Updates.PleaseSelectInfobase"),
                LocalizationManager.T("Updates.CheckTitle"));
            return;
        }

        var win = new Configuration_Management.UpdateCheckWindow(SelectedInfobase);
        win.ShowSync(OwnerWindow());
    }

    /// <summary>Открывает окно «Актуальные релизы» (ALT+F9).</summary>
    private void ExecuteShowActualReleases()
    {
        var win = new Configuration_Management.ActualReleasesWindow();
        win.ShowSync(OwnerWindow());
    }

    /// <summary>Открывает окно настройки связи ИБ ↔ конфигурация для указанной базы.</summary>
    public void OpenConfigUpdateLink(Infobase infobase)
    {
        if (infobase is null)
            return;
        var win = new Configuration_Management.ConfigUpdateLinkWindow(infobase);
        win.ShowSync(OwnerWindow());
    }

    /// <summary>Открывает окно редактирования списка типовых конфигураций 1С.</summary>
    public void OpenConfigTypesEdit()
    {
        var win = new Configuration_Management.ConfigTypesEditWindow();
        win.ShowSync(OwnerWindow());
    }
}
#endif