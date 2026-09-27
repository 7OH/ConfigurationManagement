#if WINDOWS
using System;
using System.Collections.Generic;
using System.Management;

namespace Configuration_Management.Services;

/// <summary>
/// Список запущенных процессов платформы 1С (Windows): WMI-запрос Win32_Process
/// по именам «1cv8%» (1cv8.exe — толстый клиент/Конфигуратор, 1cv8c.exe — тонкий
/// клиент). Пакет System.Management уже используется проверкой блокировки запуска.
/// Любая ошибка (нет прав, WMI отключён политикой) тихо даёт пустой список —
/// индикатор «база запущена» в этом случае просто ничего не подсвечивает.
/// </summary>
public sealed class RunningInfobasesService : IRunningInfobasesService
{
    public System.Collections.Generic.IReadOnlyList<RunningOneCProcess> GetRunning()
    {
        var result = new List<RunningOneCProcess>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, CommandLine FROM Win32_Process WHERE Name LIKE '1cv8%'");
            using var objects = searcher.Get();
            foreach (var o in objects)
            {
                try
                {
                    var name = o["Name"]?.ToString() ?? "";
                    var commandLine = o["CommandLine"]?.ToString() ?? "";
                    if (name.Length > 0)
                        result.Add(new RunningOneCProcess(name, commandLine));
                }
                catch { /* строка процесса могла исчезнуть — пропускаем */ }
            }
        }
        catch
        {
            // WMI недоступен (права/политика): индикатор не должен ронять приложение.
        }

        return result;
    }

    /// <summary>
    /// Подробности процессов 1С: PID, время создания (CreationDate), командная строка
    /// и владелец (Win32_Process.GetOwner — требует прав; при отказе имя остаётся null).
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<RunningOneCProcessDetails> GetRunningDetails()
    {
        var result = new List<RunningOneCProcessDetails>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, CommandLine, ProcessId, CreationDate FROM Win32_Process WHERE Name LIKE '1cv8%'");
            using var objects = searcher.Get();
            foreach (var o in objects)
            {
                try
                {
                    var name = o["Name"]?.ToString() ?? "";
                    if (name.Length == 0)
                        continue;

                    var pid = 0;
                    if (o["ProcessId"] is not null && int.TryParse(o["ProcessId"].ToString(), out var parsedPid))
                        pid = parsedPid;

                    var commandLine = o["CommandLine"]?.ToString() ?? "";
                    var startTime = ParseCreationDate(o["CreationDate"]?.ToString());
                    var userName = o is ManagementObject mo ? TryGetOwner(mo) : null;
                    result.Add(new RunningOneCProcessDetails(
                        pid, name, commandLine, startTime, null, userName));
                }
                catch { /* процесс мог исчезнуть во время опроса — пропускаем */ }
            }
        }
        catch
        {
            // WMI недоступен (права/политика): инспектор показывает пустой список.
        }

        return result;
    }

    /// <summary>Время создания процесса из CIM_DATETIME (например «20260927123000.123456+180»).</summary>
    private static DateTime? ParseCreationDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try { return ManagementDateTimeConverter.ToDateTime(value); }
        catch { return null; }
    }

    /// <summary>
    /// Имя владельца процесса через Win32_Process.GetOwner (CIM-метод). Доступен
    /// администраторам и владельцу процесса; при отказе — null.
    /// </summary>
    private static string? TryGetOwner(ManagementObject process)
    {
        try
        {
            if (process.InvokeMethod("GetOwner", null) is not ManagementBaseObject owner)
                return null;
            var user = owner["User"]?.ToString();
            var domain = owner["Domain"]?.ToString();
            return string.IsNullOrWhiteSpace(user)
                ? null
                : string.IsNullOrWhiteSpace(domain)
                    ? user
                    : $"{domain}\\{user}";
        }
        catch
        {
            return null;
        }
    }
}
#endif
