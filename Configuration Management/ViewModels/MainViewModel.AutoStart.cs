#if WINDOWS
using System;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Основная модель представления (Windows/WPF, partial): автозапуск при старте ОС
/// (функция №31 StartManager) и сохранение копии экрана по хоткею (функция №30).
/// </summary>
public partial class MainViewModel
{
    /// <summary>Автозапуск при старте ОС (функция №31): включён ли.</summary>
    public bool AutoStartEnabled
    {
        get => _autoStartEnabled;
        set => SetProperty(ref _autoStartEnabled, value);
    }

    /// <summary>Горячая клавиша сохранения копии экрана (функция №30). Пусто — не назначена.</summary>
    public string ScreenshotHotkey
    {
        get => _screenshotHotkey;
        set => SetProperty(ref _screenshotHotkey, value);
    }

    /// <summary>Каталог сохранения копий экрана (функция №30). Пусто — «Изображения».</summary>
    public string ScreenshotSaveDirectory
    {
        get => _screenshotSaveDirectory;
        set => SetProperty(ref _screenshotSaveDirectory, value);
    }

    /// <summary>Команда сохранения копии экрана в PNG (функция №30).</summary>
    public ICommand TakeScreenshotCommand { get; private set; } = null!;

    /// <summary>
    /// Применяет настройку автозапуска при старте ОС (функция №31): при включении пишет
    /// путь к приложению в ключ реестра <c>HKCU\...\Run</c>, при выключении — удаляет.
    /// Затем сохраняет настройку. Ошибки реестра не роняют приложение.
    /// </summary>
    public void ApplyAutoStart(bool enabled)
    {
        try
        {
            var service = AppServices.GetRequiredService<IAutoStartService>();
            if (enabled) service.Enable();
            else service.Disable();
        }
        catch (Exception ex)
        {
            try { _logger.Warn("[autostart] Не удалось изменить автозапуск: " + ex.Message); }
            catch { /* ignore */ }
        }

        AutoStartEnabled = enabled;
        ScheduleSaveSettings();
    }

    /// <summary>
    /// Сохраняет копию экрана в PNG (функция №30) в настроенный каталог. При неудаче
    /// показывает сообщение об ошибке, при успехе — путь к сохранённому файлу.
    /// </summary>
    public void TakeScreenshot()
    {
        try
        {
            var service = AppServices.GetRequiredService<IScreenshotService>();
            var path = service.Capture(ScreenshotSaveDirectory);
            _dialogs.ShowInfo(string.Format(
                LocalizationManager.T("Settings.Screenshot.Saved"), path),
                LocalizationManager.T("Settings.Screenshot.Title"));
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(string.Format(
                LocalizationManager.T("Settings.Screenshot.Failed"), ex.Message),
                LocalizationManager.T("Settings.Screenshot.Title"));
        }
    }
}
#endif