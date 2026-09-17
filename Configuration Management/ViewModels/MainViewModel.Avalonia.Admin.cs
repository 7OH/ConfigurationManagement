#if LINUX
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Команды администрирования ИБ (Этап 6, функция №29 + консоль серверов) для
/// Avalonia/Linux: быстрый запуск <c>chdbfl</c> для проверки целостности файловой
/// ИБ и запуск консоли администрирования серверов 1С для клиент-серверных баз.
/// Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _checkIntegrityCommand;
    private ICommand? _openServerConsoleCommand;

    /// <summary>Горячая клавиша «Проверка целостности файловой ИБ (chdbfl)».</summary>
    public string HotkeyCheckIntegrity => _settings.HotkeyCheckIntegrity;

    /// <summary>Горячая клавиша «Консоль администрирования серверов 1С».</summary>
    public string HotkeyServerConsole => _settings.HotkeyServerConsole;

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
            _dialog.ShowError(
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
            _dialog.ShowError(
                LocalizationManager.T("Admin.ServerConsoleFailed"),
                LocalizationManager.T("Admin.ServerConsoleTitle"));
        }
    }
}
#endif