using System;
using System.IO;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Отладочный флаг логики обновлений платформы (issue #361): при переменной окружения
/// <c>CM_DEBUG_PLATFORM_UPDATE=1</c> подробные диагностические записи (найденные
/// установленные версии с путями, входы и решения <see cref="PlatformUpdateMatcher.Merge"/>,
/// распознанный каталог портала) пишутся в файл <c>platform-update-debug.log</c> рядом
/// с журналом приложения (<see cref="PlatformPaths.LogDirectory"/>). Статический доступ —
/// чтобы логировать из статических сервисов (<see cref="PlatformVersionService"/>,
/// <see cref="PlatformUpdateMatcher"/>), у которых нет инжектируемого логгера.
/// </summary>
public static class PlatformUpdateDebug
{
    private static readonly bool _enabled = IsEnabledByEnvironment();
    private static readonly object _sync = new();

    /// <summary>True — отладочный флаг включён (переменная окружения
    /// <c>CM_DEBUG_PLATFORM_UPDATE=1</c>).</summary>
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
                    Path.Combine(directory, "platform-update-debug.log"),
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
        var value = Environment.GetEnvironmentVariable("CM_DEBUG_PLATFORM_UPDATE");
        return value is "1" or "true" or "True" or "TRUE";
    }
}