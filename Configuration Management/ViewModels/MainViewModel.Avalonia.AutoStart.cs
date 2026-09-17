#if LINUX
using System;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Основная модель представления (Avalonia/Linux, partial): автозапуск при старте ОС
/// (функция №31 StartManager) и сохранение копии экрана по хоткею (функция №30).
/// Значения хранятся в общем объекте <see cref="MainViewModel._settings"/>.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Автозапуск при старте ОС (функция №31): включён ли.</summary>
    public bool AutoStartEnabled
    {
        get => _settings.AutoStartEnabled;
        set
        {
            if (_settings.AutoStartEnabled == value)
                return;
            _settings.AutoStartEnabled = value;
            OnPropertyChanged(nameof(AutoStartEnabled));
        }
    }

    /// <summary>Горячая клавиша сохранения копии экрана (функция №30). Пусто — не назначена.</summary>
    public string ScreenshotHotkey
    {
        get => _settings.ScreenshotHotkey;
        set
        {
            if (_settings.ScreenshotHotkey == value)
                return;
            _settings.ScreenshotHotkey = value;
            OnPropertyChanged(nameof(ScreenshotHotkey));
        }
    }

    /// <summary>Каталог сохранения копий экрана (функция №30). Пусто — «Изображения».</summary>
    public string ScreenshotSaveDirectory
    {
        get => _settings.ScreenshotSaveDirectory;
        set
        {
            if (_settings.ScreenshotSaveDirectory == value)
                return;
            _settings.ScreenshotSaveDirectory = value;
            OnPropertyChanged(nameof(ScreenshotSaveDirectory));
        }
    }

    /// <summary>Команда сохранения копии экрана в PNG (функция №30).</summary>
    public ICommand TakeScreenshotCommand { get; private set; } = null!;

    /// <summary>
    /// Применяет настройку автозапуска при старте ОС (функция №31): при включении создаёт
    /// файл автозапуска десктоп-окружения, при выключении — удаляет. Затем сохраняет настройку.
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
        SaveSettingsSilently();
    }

    /// <summary>
    /// Сохраняет копию экрана в PNG (функция №30) в настроенный каталог. На Linux/Avalonia —
    /// снимок главного окна приложения. При неудаче показывает сообщение об ошибке.
    /// </summary>
    public void TakeScreenshot()
    {
        try
        {
            var service = AppServices.GetRequiredService<IScreenshotService>();
            var path = service.Capture(ScreenshotSaveDirectory);
            _dialog.ShowInfo(string.Format(
                LocalizationManager.T("Settings.Screenshot.Saved"), path),
                LocalizationManager.T("Settings.Screenshot.Title"));
        }
        catch (Exception ex)
        {
            _dialog.ShowError(string.Format(
                LocalizationManager.T("Settings.Screenshot.Failed"), ex.Message),
                LocalizationManager.T("Settings.Screenshot.Title"));
        }
    }
}
#endif