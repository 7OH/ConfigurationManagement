#if LINUX
using System;
using Avalonia.Threading;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Avalonia-часть монитора запущенных баз: доставка результата в UI-поток.</summary>
public partial class MainViewModel
{
    partial void DispatchOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    /// <summary>Активация окна 1С на Linux не поддержана (нужны wmctrl/xdotool) — no-op.</summary>
    partial void ActivateRunningInfobaseCore(Infobase infobase)
    {
        // OneCWindowActivator.Activate возвращает false; тихо пропускаем.
        _ = infobase;
    }
}
#endif
