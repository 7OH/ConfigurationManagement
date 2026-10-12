#if WINDOWS
using System.Linq;
using System.Windows;
using Configuration_Management.ViewModels;

namespace Configuration_Management.Services;

/// <summary>
/// <see cref="IMainWindowNotifier"/> для Windows/WPF (issue #366): находит открытое
/// главное окно (<see cref="MainWindow"/>) и выводит сообщение в строку состояния его
/// ViewModel (<see cref="MainViewModel.ShowStatusBarMessage"/>). Вызов marshalling-ится
/// в UI-поток через dispatcher приложения; если окна ещё нет — тихий no-op.
/// </summary>
public sealed class MainWindowNotifier : IMainWindowNotifier
{
    /// <inheritdoc/>
    public void ShowMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var app = Application.Current;
        if (app is null)
            return;

        app.Dispatcher.InvokeAsync(() =>
        {
            if (app.Windows.OfType<MainWindow>().FirstOrDefault() is { DataContext: MainViewModel vm })
                vm.ShowStatusBarMessage(message);
        });
    }
}
#endif
