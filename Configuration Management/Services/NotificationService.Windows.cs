#if WINDOWS
using System;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Configuration_Management.Services;

/// <summary>
/// Системные уведомления (Windows/WPF): balloon-tip значка в системном трее.
/// Используется уже существующий <see cref="Forms.NotifyIcon"/> главного окна
/// (<see cref="MainWindow.TrayIconInstance"/>) — это самый надёжный путь без AUMID/WinRT:
/// для Toast-уведомлений Windows 10/11 потребовался бы AppUserModelID, здесь он не нужен.
/// Если значок трея недоступен (приложение запущено без трея) — тихий no-op.
/// Показ выполняется через Dispatcher главного окна: вызовы приходят из фоновых потоков
/// (таймер планировщика, задача автообновления), а WinForms-контрол требует UI-потока.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private static readonly TimeSpan BalloonTimeout = TimeSpan.FromSeconds(5);

    private readonly IInfobaseRepository _repository;

    public NotificationService(IInfobaseRepository repository)
    {
        _repository = repository;
    }

    public void Show(string title, string message)
    {
        try
        {
            // Настройка «Системные уведомления» (AppSettings.ShowSystemNotifications):
            // при false все уведомления подавляются.
            if (!_repository.LoadSettings().ShowSystemNotifications)
                return;

            var tray = MainWindow.TrayIconInstance;
            if (tray is null)
                return;

            var app = Application.Current;
            if (app is null)
                return;

            if (app.Dispatcher.CheckAccess())
            {
                ShowBalloon(tray, title, message);
            }
            else
            {
                // Не блокируем вызывающий поток: откладываем показ на UI-поток.
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { ShowBalloon(tray, title, message); }
                    catch { /* уведомление не должно ронять приложение */ }
                }));
            }
        }
        catch
        {
            // Трей недоступен / нет UI-потока — тихий no-op.
        }
    }

    private static void ShowBalloon(Forms.NotifyIcon tray, string title, string message)
    {
        if (!tray.Visible)
            return;
        tray.ShowBalloonTip((int)BalloonTimeout.TotalMilliseconds, title, message, Forms.ToolTipIcon.Info);
    }
}
#endif