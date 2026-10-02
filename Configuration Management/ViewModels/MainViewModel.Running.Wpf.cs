#if WINDOWS
using System;
using System.Windows;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Windows-часть монитора запущенных баз: доставка результата в UI-поток WPF.</summary>
public partial class MainViewModel
{
    partial void DispatchOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    /// <summary>Активирует окно процесса 1С базы через Win32 (issue #339).</summary>
    partial void ActivateRunningInfobaseCore(Infobase infobase)
    {
        // GetRunningDetails может занять время (обход процессов) — запускаем вне UI-потока.
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                OneCWindowActivator.Activate(infobase);
            }
            catch
            {
                // Активация не должна ломать интерфейс.
            }
        });
    }
}
#endif
