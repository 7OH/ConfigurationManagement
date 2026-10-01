#if LINUX
using System.Windows.Input;
using Avalonia.Controls;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Команда функции «Автообновление платформы 1С» (функция 9, этап 0.3.9.214)
/// для Avalonia/Linux: Ctrl+F9 открывает окно «Обновление платформы». Частичный
/// класс <see cref="MainViewModel"/>. Значение хоткея читается из настроек
/// (<see cref="MainViewModel.Avalonia.Display.cs"/>, общий файл настроек с WPF).
/// </summary>
public partial class MainViewModel
{
    private ICommand? _showPlatformUpdateCommand;

    /// <summary>Команда открытия окна «Обновление платформы 1С» (Ctrl+F9).</summary>
    public ICommand ShowPlatformUpdateCommand =>
        _showPlatformUpdateCommand ??= new RelayCommand(ExecuteShowPlatformUpdate);

    /// <summary>Открывает окно «Обновление платформы 1С» (Ctrl+F9).</summary>
    private void ExecuteShowPlatformUpdate()
    {
        var win = new Configuration_Management.PlatformUpdateWindow();
        win.ShowSync(OwnerWindow());
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
        win.ShowSync(OwnerWindow());
    }
}
#endif