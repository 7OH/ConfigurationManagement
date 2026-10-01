#if WINDOWS
using System.Windows;
using System.Windows.Input;
using Configuration_Management.Localization;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Команда и горячая клавиша функции «Автообновление платформы 1С» (функция 9,
/// этап 0.3.9.214): Ctrl+F9 открывает окно «Обновление платформы». Частичный
/// класс <see cref="MainViewModel"/> (Windows/WPF).
/// </summary>
public partial class MainViewModel
{
    private ICommand? _showPlatformUpdateCommand;

    /// <summary>Горячая клавиша окна «Обновление платформы 1С» (по умолчанию Ctrl+F9).</summary>
    public string HotkeyPlatformUpdate
    {
        get => _hotkeyPlatformUpdate;
        set
        {
            if (SetProperty(ref _hotkeyPlatformUpdate, NormalizeHotkey(value, "Ctrl+F9")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Команда открытия окна «Обновление платформы 1С» (Ctrl+F9).</summary>
    public ICommand ShowPlatformUpdateCommand =>
        _showPlatformUpdateCommand ??= new RelayCommand(ExecuteShowPlatformUpdate);

    /// <summary>Открывает окно «Обновление платформы 1С» (Ctrl+F9).</summary>
    private void ExecuteShowPlatformUpdate()
    {
        var win = new Configuration_Management.PlatformUpdateWindow();
        // Настоящая модальность: блокируем владельца, окно поверх и по центру (паттерн
        // ExecuteShowActualReleases, issue #264/#288).
        win.Owner = Application.Current.MainWindow;
        // Открываем окно ОТЛОЖЕННО (DispatcherPriority.Input), как подменю «Утилиты»:
        // при вызове из пункта меню контекстное меню ещё не успело закрыться, и его
        // попап остаётся поверх нового модального диалога (issue #288).
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => win.ShowDialog()),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private ICommand? _showPlatformDownloadCommand;

    /// <summary>Команда открытия окна «Скачивание версии платформы 1С» (issue #330):
    /// выбор версии/разрядности и скачивание дистрибутива без автоматической установки.</summary>
    public ICommand ShowPlatformDownloadCommand =>
        _showPlatformDownloadCommand ??= new RelayCommand(ExecuteShowPlatformDownload);

    /// <summary>Открывает окно «Скачивание версии платформы 1С» (issue #330).</summary>
    private void ExecuteShowPlatformDownload()
    {
        var win = new Configuration_Management.PlatformDownloadWindow();
        win.Owner = Application.Current.MainWindow;
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => win.ShowDialog()),
            System.Windows.Threading.DispatcherPriority.Input);
    }
}
#endif