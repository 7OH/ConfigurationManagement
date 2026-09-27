#if WINDOWS
using System;
using System.Windows;

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
}
#endif
