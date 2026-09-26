#if LINUX
using System.Windows.Input;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Команды функций №19 (временная блокировка приложения паролем) и №20
/// (блокировка сеансов файловой ИБ без открытия «1С:Предприятия», CTRL+ALT+L)
/// StartManager для Avalonia/Linux. Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _showSessionLockCommand;
    private ICommand? _lockAppCommand;

    /// <summary>Горячая клавиша «Блокировка сеансов ИБ» (по умолчанию Ctrl+Alt+L).</summary>
    public string HotkeySessionLock => _settings.HotkeySessionLock;

    /// <summary>Горячая клавиша «Временная блокировка приложения».</summary>
    public string HotkeyLockApp => _settings.HotkeyLockApp;

    /// <summary>Команда открытия окна «Блокировка сеансов информационной базы» (Ctrl+Alt+L).</summary>
    public ICommand ShowSessionLockCommand =>
        _showSessionLockCommand ??= new RelayCommand(
            ExecuteShowSessionLock,
            () => SelectedInfobase is not null);

    /// <summary>Команда временной блокировки приложения паролем.</summary>
    public ICommand LockAppCommand =>
        _lockAppCommand ??= new RelayCommand(ExecuteLockApp);

    private void ExecuteShowSessionLock()
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
            return;
        var win = new Configuration_Management.SessionLockWindow(infobase);
        win.ShowDialogSync(OwnerWindow());
    }

    private void ExecuteLockApp()
    {
        // Если пароль ещё не задан — сначала предложить его установить.
        // При отмене установки блокировка не запускается (issue #294).
        if (!HasAppLockPassword)
        {
            var setupWin = new Configuration_Management.AppLockWindow(this, setupMode: true);
            setupWin.ShowDialogSync(OwnerWindow());
            if (!HasAppLockPassword)
                return;
        }

        LockNow();
    }

    /// <summary>
    /// Включает блокировку приложения (issue #294): состояние <see cref="IsAppLocked"/>
    /// закрывает главное окно оверлеем, поверх открывается закрываемое окно ввода пароля.
    /// Закрытие окна ввода блокировку не снимает — блокировка держится до верного пароля.
    /// Повторный вызов при активной блокировке просто показывает окно ввода пароля.
    /// </summary>
    public void LockNow()
    {
        if (!HasAppLockPassword)
            return;

        IsAppLocked = true;
        ShowAppUnlockDialog();
    }

    /// <summary>Снимает блокировку приложения (после верного пароля, issue #294).</summary>
    public void UnlockApp() => IsAppLocked = false;

    private Configuration_Management.AppLockWindow? _unlockWindow;

    /// <summary>
    /// Показывает окно ввода пароля в немодальном режиме с владельцем — главное окно
    /// остаётся недоступным из-за оверлея и перехвата ввода (issue #294). Повторный
    /// вызов активирует уже открытое окно, а не создаёт новое.
    /// </summary>
    public void ShowAppUnlockDialog()
    {
        if (_unlockWindow != null)
        {
            _unlockWindow.Activate();
            return;
        }

        var win = new Configuration_Management.AppLockWindow(this, setupMode: false);
        _unlockWindow = win;
        win.UnlockSucceeded += (_, _) => UnlockApp();
        win.Closed += (_, _) => _unlockWindow = null;
        win.Show(OwnerWindow());
    }

    /// <summary>Пароль блокировки приложения (PBKDF2-хэш) из настроек.</summary>
    public bool HasAppLockPassword => !string.IsNullOrEmpty(_settings.AppLockPasswordHash);

    /// <summary>Задаёт/меняет пароль блокировки приложения.</summary>
    public void SetAppLockPassword(string? password)
    {
        _settings.AppLockPasswordHash = PasswordHasher.Hash(password ?? string.Empty);
        SaveSettingsSilently();
    }

    /// <summary>Проверяет пароль для снятия блокировки приложения.</summary>
    public bool VerifyAppLockPassword(string password) =>
        PasswordHasher.Verify(password, _settings.AppLockPasswordHash);
}
#endif