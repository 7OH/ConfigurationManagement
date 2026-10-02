#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Активация окна уже запущенной базы 1С (issue #339): при включённом отборе
/// «Только запущенные» двойной клик/Enter поднимают окно процесса 1С этой базы
/// вместо повторного запуска. PID процесса находится сопоставлением командной
/// строки (<see cref="RunningInfobaseMatcher"/>), затем через Win32-перечисление
/// окон выбирается видимое главное окно процесса и выводится на передний план.
/// </summary>
public static class OneCWindowActivator
{
    private const int SwRestore = 9;

    /// <summary>Активирует видимое окно процесса 1С, подключённого к базе. Возвращает true при успехе.</summary>
    public static bool Activate(Infobase infobase)
    {
        if (infobase is null)
            return false;

        var service = AppServices.TryGetService<IRunningInfobasesService>();
        var details = service?.GetRunningDetails() ?? Array.Empty<RunningOneCProcessDetails>();

        // PID процессов, чья командная строка соответствует базе (как в мониторе).
        var pids = details
            .Where(d => RunningInfobaseMatcher.MatchesCommandLine(infobase, d.CommandLine))
            .Select(d => d.Pid)
            .Distinct()
            .ToArray();
        if (pids.Length == 0)
            return false;

        IntPtr? target = null;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true; // продолжаем перечисление

            GetWindowThreadProcessId(hWnd, out var pid);
            if (Array.IndexOf(pids, (int)pid) < 0)
                return true;

            target = hWnd;
            return false; // нашли окно — останавливаем перечисление
        }, IntPtr.Zero);

        if (target is not { } handle)
            return false;

        ShowWindow(handle, SwRestore);
        return SetForegroundWindow(handle);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
#endif