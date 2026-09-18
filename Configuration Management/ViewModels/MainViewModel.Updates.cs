#if WINDOWS
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Команды и свойства функции «Проверка обновлений конфигураций 1С» (функции №21/№22):
/// F9 — проверка обновлений выбранной ИБ, ALT+F9 — окно «Актуальные релизы».
/// Частичный класс <see cref="MainViewModel"/> (Windows/WPF).
/// </summary>
public partial class MainViewModel
{
    private ICommand? _checkUpdateCommand;
    private ICommand? _showActualReleasesCommand;

    /// <summary>Горячая клавиша «Проверить обновления» для выбранной ИБ (по умолчанию F9).</summary>
    public string HotkeyCheckUpdate
    {
        get => _hotkeyCheckUpdate;
        set
        {
            if (SetProperty(ref _hotkeyCheckUpdate, NormalizeHotkey(value, "F9")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Горячая клавиша окна «Актуальные релизы» (по умолчанию Alt+F9).</summary>
    public string HotkeyActualReleases
    {
        get => _hotkeyActualReleases;
        set
        {
            if (SetProperty(ref _hotkeyActualReleases, NormalizeHotkey(value, "Alt+F9")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Логин учётной записи сайта 1С для авторизации (HTTP Basic Auth) при проверке обновлений конфигураций.</summary>
    public string UpdatesLogin
    {
        get => _updatesLogin;
        set
        {
            if (SetProperty(ref _updatesLogin, value ?? ""))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Пароль учётной записи сайта 1С для авторизации (HTTP Basic Auth) при проверке обновлений конфигураций.</summary>
    public string UpdatesPassword
    {
        get => _updatesPassword;
        set
        {
            if (SetProperty(ref _updatesPassword, value ?? ""))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Команда «Проверить обновления» для выбранной ИБ (F9).</summary>
    public ICommand CheckUpdateCommand =>
        _checkUpdateCommand ??= new RelayCommand(
            () => ExecuteCheckUpdate(),
            () => SelectedInfobase is not null);

    /// <summary>Команда открытия окна «Актуальные релизы» (ALT+F9).</summary>
    public ICommand ShowActualReleasesCommand =>
        _showActualReleasesCommand ??= new RelayCommand(ExecuteShowActualReleases);

    /// <summary>Открывает окно проверки обновлений для выбранной ИБ (F9).</summary>
    private void ExecuteCheckUpdate()
    {
        if (SelectedInfobase is null)
        {
            _dialogs.ShowInfo(
                LocalizationManager.T("Updates.PleaseSelectInfobase"),
                LocalizationManager.T("Updates.CheckTitle"));
            return;
        }

        var win = new Configuration_Management.UpdateCheckWindow(SelectedInfobase);
        win.ShowDialog();
    }

    /// <summary>Открывает окно «Актуальные релизы» (ALT+F9).</summary>
    private void ExecuteShowActualReleases()
    {
        var win = new Configuration_Management.ActualReleasesWindow();
        win.ShowDialog();
    }

    /// <summary>Открывает окно настройки связи ИБ ↔ конфигурация для указанной базы.</summary>
    public void OpenConfigUpdateLink(Infobase infobase)
    {
        if (infobase is null)
            return;
        var win = new Configuration_Management.ConfigUpdateLinkWindow(infobase);
        win.ShowDialog();
    }

    /// <summary>Открывает окно редактирования списка типовых конфигураций 1С.</summary>
    public void OpenConfigTypesEdit()
    {
        var win = new Configuration_Management.ConfigTypesEditWindow();
        win.ShowDialog();
    }
}
#endif