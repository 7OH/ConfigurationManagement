#if LINUX
using System;
using System.Collections.Generic;
using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Список запущенных процессов платформы 1С (Linux): обход /proc/[0-9]*/cmdline —
/// аргументы процесса разделены нулевыми байтами. Клиентские процессы: 1cv8,
/// 1cv8c (тонкий клиент). Ошибки доступа к отдельным процессам пропускаются,
/// любые проблемы каталога дают пустой список.
/// </summary>
public sealed class RunningInfobasesService : IRunningInfobasesService
{
    public System.Collections.Generic.IReadOnlyList<RunningOneCProcess> GetRunning()
    {
        var result = new List<RunningOneCProcess>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                var name = Path.GetFileName(dir);
                if (name.Length == 0 || name[0] < '0' || name[0] > '9')
                    continue;

                string content;
                try { content = File.ReadAllText(Path.Combine(dir, "cmdline")); }
                catch { continue; /* процесс завершился или нет прав */ }

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var args = content.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length == 0)
                    continue;

                var exe = Path.GetFileName(args[0]);
                if (!exe.StartsWith("1cv8", StringComparison.Ordinal))
                    continue;

                result.Add(new RunningOneCProcess(exe, string.Join(" ", args)));
            }
        }
        catch
        {
            // /proc недоступен (нестандартное окружение): тихо возвращаем пусто.
        }

        return result;
    }
}
#endif
