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
    public string? LastError { get; private set; }

    public bool Kill(int pid, string? startTimeToken)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited)
                return true;

            p.Kill(entireProcessTree: true);
            p.WaitForExit(3000);
            LastError = null;
            return true;
        }
        catch (ArgumentException)
        {
            // Процесс с таким PID уже отсутствует — цель достигнута.
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            // Нет прав на завершение / доступ к процессу (например, процесс запущен от
            // имени другого пользователя или с повышенными правами). Причину сохраняем,
            // чтобы показать её пользователю (issue #342), а не глушить в «не удалось».
            LastError = ex.Message;
            return false;
        }
    }
}
#endif