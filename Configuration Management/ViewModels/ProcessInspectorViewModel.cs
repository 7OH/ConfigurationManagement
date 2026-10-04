using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel окна «Инспектор процессов 1С» (0.3.9.93): таблица всех запущенных
/// процессов платформы 1С (база/режим/пользователь/время старта/PID/строка
/// подключения/значок сопоставления), обновление по кнопке и по таймеру,
/// завершение выбранного процесса с подтверждением и переход к базе в главном
/// окне по двойному клику (FindInList-механика). Чистый .NET без платформенных
/// зависимостей — обе платформы (WPF и Avalonia); окна только привязываются.
/// </summary>
public sealed class ProcessInspectorViewModel : ViewModelBase, IDisposable
{
    /// <summary>Период автообновления списка процессов, миллисекунды (~5 секунд).</summary>
    public const int AutoRefreshIntervalMs = 5000;

    private readonly IRunningInfobasesService _service;
    private readonly IOneCProcessKiller _killer;
    private readonly IDialogService _dialogs;
    private readonly Action<Infobase> _openBase;
    private readonly Action<Action>? _dispatchToUi;
    private readonly IReadOnlyList<Infobase> _infobases;
    private Timer? _timer;
    private int _refreshBusy;
    private ProcessRowViewModel? _selectedRow;

    /// <summary>
    /// Ключ строки, восстановленной последним обновлением (issue #342). Хранится для
    /// двухфазного восстановления: если пользователь с момента восстановления ничего
    /// не выбрал, повторная установка того же ключа страхует от «роняющего» выделение
    /// поведения виртуализации контрола (WPF DataGrid / Avalonia ListBox) на последующей
    /// разметке. Как только пользователь выбирает другую строку — ключ перезаписывается
    /// и повторная фаза становится no-op.
    /// </summary>
    private RowSelectionKey? _lastRestoredKey;

    /// <param name="service">Источник процессов 1С (WMI на Windows, /proc на Linux).</param>
    /// <param name="killer">Завершение процесса по PID.</param>
    /// <param name="dialogs">Диалоги (подтверждение завершения, сообщения об ошибках).</param>
    /// <param name="infobases">Все базы списка для сопоставления командных строк.</param>
    /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
    /// <param name="dispatchToUi">
    /// Доставка результата фонового опроса в UI-поток (передаёт окно); null —
    /// результаты применяются прямо из рабочего потока (тесты).
    /// </param>
    public ProcessInspectorViewModel(
        IRunningInfobasesService service,
        IOneCProcessKiller killer,
        IDialogService dialogs,
        IEnumerable<Infobase> infobases,
        Action<Infobase> openBase,
        Action<Action>? dispatchToUi = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _killer = killer ?? throw new ArgumentNullException(nameof(killer));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _openBase = openBase ?? throw new ArgumentNullException(nameof(openBase));
        _dispatchToUi = dispatchToUi;
        _infobases = infobases?.Where(ib => ib is not null).ToList()
            ?? new List<Infobase>();

        Refresh();
        _timer = new Timer(_ => Refresh(), null, AutoRefreshIntervalMs, AutoRefreshIntervalMs);
    }

    /// <summary>Строки таблицы (пересоздаются при каждом обновлении).</summary>
    public ObservableCollection<ProcessRowViewModel> Processes { get; } = new();

    /// <summary>
    /// Составной ключ строки для восстановления выделения (issue #342): PID + полная
    /// командная строка. PID может быть переиспользован ОС после перезапуска процесса —
    /// одна только проверка по PID перескочила бы выделение на другой процесс; командная
    /// строка (строка подключения) остаётся стабильной для одного и того же запуска.
    /// </summary>
    private readonly record struct RowSelectionKey(int Pid, string CommandLine);

    /// <summary>Ключ строки для восстановления выделения (issue #342).</summary>
    private static RowSelectionKey KeyOf(ProcessRowViewModel row) =>
        new(row.Pid, row.FullCommandLine ?? string.Empty);

    /// <summary>Выбранная строка (кнопка «Завершить процесс»).</summary>
    public ProcessRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    /// <summary>Итог: «Всего процессов: N».</summary>
    public string SummaryText { get; private set; } = string.Empty;

    /// <summary>Есть ли хотя бы один процесс.</summary>
    public bool HasProcesses => Processes.Count > 0;

    /// <summary>«Обновить» — внеплановый опрос (первый опрос выполняется в конструкторе).</summary>
    public void Refresh()
    {
        // Один опрос за раз: длительный WMI/обход /proc не должен копить очередь задач.
        if (Interlocked.Exchange(ref _refreshBusy, 1) == 1)
            return;

        Task.Run(() =>
        {
            try
            {
                var processes = _service.GetRunningDetails();
                var rows = BuildRows(processes);
                if (_dispatchToUi is null)
                    ApplyRows(rows);
                else
                    _dispatchToUi(() => ApplyRows(rows));
            }
            catch
            {
                // Опрос не должен ронять окно: при ошибке список остаётся прежним.
            }
            finally
            {
                Interlocked.Exchange(ref _refreshBusy, 0);
            }
        });
    }

    /// <summary>«Завершить процесс»: подтверждение → kill → перезапуск списка.</summary>
    public void KillSelected()
    {
        var row = SelectedRow;
        if (row is null)
        {
            // Подсказка вместо молчаливого возврата: без выделения завершать нечего,
            // а молчание выглядело как поломка кнопки (issue #342).
            _dialogs.ShowWarning(
                LocalizationManager.T("ProcessInspector.SelectProcessHint"),
                LocalizationManager.T("ProcessInspector.KillProcess"));
            return;
        }

        if (!_dialogs.Confirm(
                string.Format(LocalizationManager.T("ProcessInspector.KillConfirm"), row.Pid),
                LocalizationManager.T("ProcessInspector.KillProcess")))
            return;

        var killed = _killer.Kill(row.Pid, string.IsNullOrEmpty(row.StartTimeToken) ? null : row.StartTimeToken);
        if (!killed)
        {
            // Причина отказа берётся из киллера (Windows: текст исключения — «Отказано
            // в доступе» и т.п.); если причина недоступна — стандартное объяснение
            // (issue #342: кнопка «Завершить процесс» не работала без пояснения).
            var reason = _killer.LastError;
            var message = string.IsNullOrWhiteSpace(reason)
                ? string.Format(LocalizationManager.T("ProcessInspector.KillFailedFormat"), row.Pid)
                : string.Format(LocalizationManager.T("ProcessInspector.KillFailedDetailFormat"), row.Pid, reason);
            _dialogs.ShowWarning(message, LocalizationManager.T("ProcessInspector.KillProcess"));
        }

        Refresh();
    }

    /// <summary>«Открыть базу» — переход к строке в главном окне (только для известной базы).</summary>
    public void OpenBase(ProcessRowViewModel row)
    {
        if (row?.Infobase is not null)
            _openBase(row.Infobase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private List<ProcessRowViewModel> BuildRows(IReadOnlyList<RunningOneCProcessDetails> processes)
    {
        var rows = new List<ProcessRowViewModel>(processes.Count);
        foreach (var p in processes)
        {
            Infobase? matched = null;
            if (!string.IsNullOrWhiteSpace(p.CommandLine))
            {
                foreach (var ib in _infobases)
                {
                    if (RunningInfobaseMatcher.MatchesCommandLine(ib, p.CommandLine))
                    {
                        matched = ib;
                        break;
                    }
                }
            }

            rows.Add(new ProcessRowViewModel(p, matched));
        }

        return rows;
    }

    private void ApplyRows(List<ProcessRowViewModel> rows)
    {
        // Строки пересоздаются при каждом опросе, поэтому выделение сохраняется
        // по составному ключу (PID + командная строка), а не по ссылке на старую
        // строку (issue #342). Командная строка в ключе защищает от переиспользования
        // PID ОС: если процесс перезапустился с другим аргументом, тот же PID — это
        // уже другой процесс, выделение на него не переносится.
        var selectedKey = SelectedRow is { } selected ? KeyOf(selected) : (RowSelectionKey?)null;

        Processes.Clear();
        foreach (var row in rows)
            Processes.Add(row);

        if (selectedKey is { } key)
        {
            // Точное совпадение по составному ключу; при пустой командной строке у
            // старой строки (идентичность неизвестна) — fallback на единственный PID.
            var match = rows.FirstOrDefault(r => KeyOf(r) == key)
                ?? (key.CommandLine.Length == 0 ? rows.FirstOrDefault(r => r.Pid == key.Pid) : null);
            SelectedRow = match;
        }
        else
        {
            SelectedRow = null;
        }

        _lastRestoredKey = SelectedRow is { } restored ? KeyOf(restored) : (RowSelectionKey?)null;

        // Двухфазное восстановление (issue #342): часть контролов (виртуализация,
        // пересоздание контейнеров) может сбросить только что восстановленное выделение
        // на последующем проходе разметки. Повторная фаза через диспетчер UI переустанавливает
        // выбор, но только если пользователь за это время не выбрал другую строку.
        if (_lastRestoredKey is not null && _dispatchToUi is not null)
            _dispatchToUi(ReassertSelection);

        SummaryText = Processes.Count == 0
            ? LocalizationManager.T("ProcessInspector.Empty")
            : string.Format(LocalizationManager.T("ProcessInspector.SummaryFormat"), Processes.Count);
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasProcesses));
    }

    /// <summary>
    /// Вторая фаза восстановления выделения (issue #342): повторно уведомляет о выбранной
    /// строке, если она всё ещё в списке и пользователь не изменил выбор с момента
    /// восстановления. Выполняется асинхронно в UI-потоке ПОСЛЕ первой фазы, когда
    /// контрол успел обработать пересоздание коллекции и разметку.
    /// </summary>
    private void ReassertSelection()
    {
        var current = SelectedRow;
        if (current is null || _lastRestoredKey is not { } key)
            return;
        // Пользователь уже выбрал другую строку (или выбор снят) — не вмешиваемся.
        if (KeyOf(current) != key)
            return;
        // Строка должна всё ещё присутствовать в коллекции (не удалена следующим опросом).
        if (!Processes.Contains(current))
            return;
        // Повторное уведомление заставляет привязку SelectedItem перевыставить выбор.
        OnPropertyChanged(nameof(SelectedRow));
    }
}