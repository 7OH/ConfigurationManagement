#if LINUX
using System;
using System.Diagnostics;

namespace Configuration_Management.Services;

/// <summary>
/// Системные уведомления (Linux/Avalonia): команда <c>notify-send</c> из libnotify —
/// стандартный способ показать уведомление в десктоп-окружениях (GNOME, KDE и др.).
/// Запускается отдельным процессом без окна (fire-and-forget), имя приложения задаётся
/// через <c>--app-name</c>. Если notify-send не установлен или уведомления запрещены —
/// тихий no-op: исключения процесса гасятся, приложение не падает.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(3);

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

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "notify-send",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    ArgumentList =
                    {
                        "--app-name=ConfigurationManagement",
                        title ?? string.Empty,
                        message ?? string.Empty
                    }
                }
            };

            if (!process.Start())
                return;

            // Дожидаемся завершения с ограничением по времени: убивать не нужно,
            // но и держать поток планировщика дольше пары секунд тоже не стоит.
            if (!process.WaitForExit((int)StartTimeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch { /* процесс мог завершиться сам */ }
            }
        }
        catch
        {
            // notify-send отсутствует / окружение без уведомлений — тихий no-op.
        }
    }
}
#endif