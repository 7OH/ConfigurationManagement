using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;
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
            foreach (var ib in Infobases)
            {
                // База считается запущенной, если хотя бы один процесс 1С подключён к ней.
                var matching = processes
                    .Where(p => RunningInfobaseMatcher.MatchesCommandLine(ib, p.CommandLine))
                    .ToList();
                var running = matching.Count > 0;

                // issue #310: если среди процессов базы есть «не отвечающий» —
                // точка становится оранжевой/красной вместо зелёной.
                var notResponding = running && matching.Any(p => !p.IsResponding);

                ib.IsRunning = running;
                ib.IsNotResponding = notResponding;
                ib.NotRespondingStreak = notResponding ? ib.NotRespondingStreak + 1 : 0;
            }
        }
        catch
        {
            // Применение результатов не должно ломать интерфейс.
        }
    }

    /// <summary>
    /// Активирует окно уже запущенной базы 1С (issue #339): при включённом отборе
    /// «Только запущенные» двойной клик и Enter вместо повторного запуска поднимают
    /// окно процесса 1С этой базы. Платформенная реализация — в партиалах.
    /// </summary>
    public void ActivateRunningInfobase(Infobase infobase)
    {
        if (infobase is null || !infobase.IsRunning)
            return;
        try
        {
            ActivateRunningInfobaseCore(infobase);
        }
        catch
        {
            // Активация окна не должна ломать интерфейс: при любой ошибке тихо пропускаем.
        }
    }

    /// <summary>Платформенная активация окна процесса 1С базы.</summary>
    partial void ActivateRunningInfobaseCore(Infobase infobase);

    /// <summary>Выполняет действие в UI-потоке (платформенная реализация).</summary>
    partial void DispatchOnUi(Action action);
}
