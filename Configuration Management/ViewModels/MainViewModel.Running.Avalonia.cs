#if LINUX
using System;
using Avalonia.Threading;

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
}
#endif
