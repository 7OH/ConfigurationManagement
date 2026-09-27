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
}
#endif
