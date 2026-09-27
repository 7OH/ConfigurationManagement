using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Монитор запущенных баз (частичный класс <see cref="MainViewModel"/>): раз в 10 секунд
/// запрашивает список процессов платформы 1С и сопоставляет их командные строки с базами
/// списка (<see cref="RunningInfobaseMatcher"/>). Результат — флаг <c>Infobase.IsRunning</c>,
/// который рисует зелёную точку у имени базы. Платформенная часть — только доставка
/// результата в UI-поток (<c>DispatchOnUi</c>: реализация в Windows/Avalonia-партиалах).
/// </summary>
public partial class MainViewModel
{
    private Timer? _runningBasesTimer;
    private int _runningRefreshBusy;

    /// <summary>Период опроса процессов 1С (миллисекунды).</summary>
    private const int RunningBasesIntervalMs = 10_000;

    /// <summary>
    /// Запускает монитор запущенных баз (повторный вызов игнорируется).
    /// Вызывается из окна после построения списка — первый опрос сразу, далее по таймеру.
    /// </summary>
    public void StartRunningBasesMonitor()
    {
        if (_runningBasesTimer is not null)
            return;

        RefreshRunningFlags();
        _runningBasesTimer = new Timer(_ => RefreshRunningFlags(), null,
            RunningBasesIntervalMs, RunningBasesIntervalMs);
    }

    /// <summary>Внеплановый опрос (после запуска базы — точка появится, не дожидаясь таймера).</summary>
    public void RefreshRunningFlags()
    {
        // Один опрос за раз: длительный WMI не должен копить очередь задач.
        if (Interlocked.Exchange(ref _runningRefreshBusy, 1) == 1)
            return;

        Task.Run(() =>
        {
            try
            {
                var processes = AppServices.TryGetService<IRunningInfobasesService>()
                    ?.GetRunning() ?? Array.Empty<RunningOneCProcess>();
                DispatchOnUi(() => ApplyRunningProcesses(processes));
            }
            catch
            {
                // Монитор не должен влиять на работу приложения.
            }
            finally
            {
                Interlocked.Exchange(ref _runningRefreshBusy, 0);
            }
        });
    }

    /// <summary>Применяет результаты опроса к базам списка (только в UI-потоке).</summary>
    private void ApplyRunningProcesses(System.Collections.Generic.IReadOnlyList<RunningOneCProcess> processes)
    {
        try
        {
            var commandLines = processes
                .Where(p => !string.IsNullOrWhiteSpace(p.CommandLine))
                .Select(p => p.CommandLine)
                .ToList();

            foreach (var ib in Infobases)
            {
                var running = commandLines.Any(cl => RunningInfobaseMatcher.MatchesCommandLine(ib, cl));
                if (ib.IsRunning != running)
                    ib.IsRunning = running;
            }
        }
        catch
        {
            // Применение результатов не должно ломать интерфейс.
        }
    }

    /// <summary>Выполняет действие в UI-потоке (платформенная реализация).</summary>
    partial void DispatchOnUi(Action action);
}
