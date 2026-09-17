#if WINDOWS
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Команды администрирования ИБ (Этап 6, функция №29 + консоль серверов) для
/// Windows/WPF: быстрый запуск <c>chdbfl.exe</c> для проверки целостности файловой
/// ИБ и запуск консоли администрирования серверов 1С для клиент-серверных баз.
/// Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _checkIntegrityCommand;
    private ICommand? _openServerConsoleCommand;
    private string _hotkeyCheckIntegrity = "Ctrl+Alt+Q";
    private string _hotkeyServerConsole = "Ctrl+Alt+S";

    /// <summary>Горячая клавиша «Проверка целостности файловой ИБ (chdbfl)».</summary>
    public string HotkeyCheckIntegrity
    {
        get => _hotkeyCheckIntegrity;
        set
        {
            if (SetProperty(ref _hotkeyCheckIntegrity, NormalizeHotkey(value, "Ctrl+Alt+Q")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Горячая клавиша «Консоль администрирования серверов 1С».</summary>
    public string HotkeyServerConsole
    {
        get => _hotkeyServerConsole;
        set
        {
            if (SetProperty(ref _hotkeyServerConsole, NormalizeHotkey(value, "Ctrl+Alt+S")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Команда проверки целостности файловой ИБ через chdbfl (функция №29).</summary>
    public ICommand CheckIntegrityCommand =>
        _checkIntegrityCommand ??= new RelayCommand(
            ExecuteCheckIntegrity,
            () => SelectedInfobase?.Connection.Type == ConnectionType.File);

    /// <summary>Команда открытия консоли администрирования серверов 1С (для клиент-серверных баз).</summary>
    public ICommand OpenServerConsoleCommand =>
        _openServerConsoleCommand ??= new RelayCommand(
            ExecuteOpenServerConsole,
            () => SelectedInfobase?.Connection.Type == ConnectionType.ClientServer);

    private void ExecuteCheckIntegrity()
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
            return;

        var service = AppServices.GetRequiredService<IInfobaseAdminService>();
        if (!service.CheckIntegrity(infobase))
        {
            _dialogs.ShowError(
                LocalizationManager.T("Admin.CheckIntegrityFailed"),
                LocalizationManager.T("Admin.CheckIntegrityTitle"));
        }
    }

    private void ExecuteOpenServerConsole()
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
            return;

        var service = AppServices.GetRequiredService<IInfobaseAdminService>();
        if (!service.OpenServerAdminConsole(infobase))
        {
            _dialogs.ShowError(
                LocalizationManager.T("Admin.ServerConsoleFailed"),
                LocalizationManager.T("Admin.ServerConsoleTitle"));
        }
    }
}
#endif