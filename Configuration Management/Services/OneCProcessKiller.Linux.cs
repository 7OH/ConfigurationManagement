#if LINUX
using System;

namespace Configuration_Management.Services;

/// <summary>
/// Завершение процесса 1С на Linux: kill через <see cref="LinuxProc"/> с обязательной
/// сверкой времени старта (/proc/<pid>/stat) — ядро переиспользует номера
/// процессов, и без сверки можно завершить чужой процесс, занявший тот же PID.
/// </summary>
public sealed class OneCProcessKiller : IOneCProcessKiller
{
    public bool Kill(int pid, string? startTimeToken) => LinuxProc.KillOne(pid, startTimeToken);
}
#endif