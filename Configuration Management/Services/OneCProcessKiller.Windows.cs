#if WINDOWS
using System;
using System.Diagnostics;

namespace Configuration_Management.Services;

/// <summary>
/// Завершение процесса 1С на Windows: Process.Kill вместе с деревом потомков.
/// PID-токен времени старта на Windows не нужен — Process.GetProcessById сам
/// укажет, что процесс уже завершён (ArgumentException).
/// </summary>
public sealed class OneCProcessKiller : IOneCProcessKiller
{
    public bool Kill(int pid, string? startTimeToken)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited)
                return true;

            p.Kill(entireProcessTree: true);
            p.WaitForExit(3000);
            return true;
        }
        catch (ArgumentException)
        {
            // Процесс с таким PID уже отсутствует — цель достигнута.
            return true;
        }
        catch
        {
            // Нет прав на завершение / доступ к процессу.
            return false;
        }
    }
}
#endif