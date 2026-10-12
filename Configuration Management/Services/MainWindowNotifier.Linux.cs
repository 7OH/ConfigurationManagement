#if LINUX
using Avalonia.Controls.ApplicationLifetimes;
using Configuration_Management.ViewModels;

namespace Configuration_Management.Services;

/// <summary>
/// <see cref="IMainWindowNotifier"/> для Linux/Avalonia (issue #366): находит главное
/// окно приложения через lifetime и выводит сообщение в строку состояния его ViewModel
/// (<see cref="MainViewModel.ShowStatusBarMessage"/>). Показ marshalling-ится в UI-поток
/// через dispatcher Avalonia; если окна ещё нет — тихий no-op.
/// </summary>
public sealed class MainWindowNotifier : IMainWindowNotifier
{
    /// <inheritdoc/>
    public void ShowMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                    is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.DataContext is MainViewModel vm)
            {
                vm.ShowStatusBarMessage(message);
            }
        });
    }
}
#endif
