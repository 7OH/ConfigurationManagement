using System;
using System.IO;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Отладочный журнал формы «Задания по расписанию» (issue #372): при переменной окружения
/// <c>CM_SCHEDULE_DEBUG=1</c> подробные диагностические записи (язык, значения локализации,
/// тип и содержимое выбранных пунктов ComboBox) пишутся в файл <c>schedule-debug.log</c>
/// в каталоге логов (<see cref="PlatformPaths.LogDirectory"/> — AppDataDirectory/logs).
/// Вызовы из окон и ViewModel обёрнуты в <c>#if DEBUG</c> — в релизной сборке диагностика
/// не компилируется. Статический доступ — чтобы логировать из статических методов
/// (<see cref="Configuration_Management.ViewModels.ScheduledTaskEditViewModel.BuildKindOptions"/>).
/// </summary>
public static class ScheduleDebug
{
    private static readonly bool _enabled = IsEnabledByEnvironment();
    private static readonly object _sync = new();

    /// <summary>True — отладочный флаг включён (переменная окружения <c>CM_SCHEDULE_DEBUG=1</c>).</summary>
    public static bool IsEnabled => _enabled;

    /// <summary>Пишет строку в файл отладочного журнала (только при включённом флаге).
    /// Сбой записи не роняет приложение — диагностика молча пропускается.</summary>
    public static void Log(string message)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            lock (_sync)
            {
                var directory = PlatformPaths.LogDirectory;
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "schedule-debug.log"),
                    line,
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Диагностика не должна ронять приложение.
        }
    }

    private static bool IsEnabledByEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("CM_SCHEDULE_DEBUG");
        return value is "1" or "true" or "True" or "TRUE";
    }
}
