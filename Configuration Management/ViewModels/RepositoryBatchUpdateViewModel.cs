using System.Collections.ObjectModel;
using System.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Статус строки базы в окне пакетного обновления из хранилищ.</summary>
public enum RepositoryBatchStatus
{
    /// <summary>Ожидание (база ещё не обработана).</summary>
    Pending,
    /// <summary>Идёт обновление конфигурации из хранилища.</summary>
    Running,
    /// <summary>Обновление выполнено успешно.</summary>
    Success,
    /// <summary>Обновление завершилось ошибкой.</summary>
    Failed,
    /// <summary>База пропущена (запущена, конфигуратор занят и т.п.).</summary>
    Skipped
}

/// <summary>
/// Строка окна пакетного обновления из хранилищ: имя базы, адрес хранилища,
/// флажок выбора, текущий статус и затраченное время. Чистый .NET без
/// платформенных зависимостей — подключается в Linux-сборку явно.
/// </summary>
public sealed class RepositoryBatchItem : ViewModelBase
{
    private bool _isChecked;
    private bool _isBusy;
    private RepositoryBatchStatus _status = RepositoryBatchStatus.Pending;
    private string _statusText = string.Empty;
    private string _durationText = string.Empty;

    public RepositoryBatchItem(Infobase infobase)
    {
        Infobase = infobase;
        // Базы, которые уже запущены, не выбираются по умолчанию (0.3.9.82).
        _isChecked = !infobase.IsRunning;
    }

    /// <summary>Информационная база.</summary>
    public Infobase Infobase { get; }

    /// <summary>Имя базы.</summary>
    public string Title => Infobase.Name;

    /// <summary>Пояснение: группа и адрес хранилища конфигурации.</summary>
    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Infobase.Group))
                parts.Add(Infobase.Group);
            var repo = Infobase.Repository?.AddressDisplay ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(repo))
                parts.Add(repo);
            if (Infobase.IsRunning)
                parts.Add(LocalizationManager.T("RepoUpdate.BaseRunning"));
            return string.Join("  •  ", parts);
        }
    }

    /// <summary>Отмечена ли база для обновления.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    /// <summary>Идёт ли по базе операция (блокирует флажок во время прогона).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsSelectable));
        }
    }

    /// <summary>Можно ли менять флажок (не идёт ли операция по базе).</summary>
    public bool IsSelectable => !_isBusy;

    /// <summary>Статус обработки базы.</summary>
    public RepositoryBatchStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Человекочитаемый текст статуса.</summary>
    public string StatusText
    {
        get
        {
            if (!string.IsNullOrEmpty(_statusText))
                return _statusText;
            return Status switch
            {
                RepositoryBatchStatus.Pending => LocalizationManager.T("RepoUpdate.StatusPending"),
                RepositoryBatchStatus.Running => LocalizationManager.T("RepoUpdate.StatusRunning"),
                RepositoryBatchStatus.Success => LocalizationManager.T("RepoUpdate.StatusSuccess"),
                RepositoryBatchStatus.Failed => LocalizationManager.T("RepoUpdate.StatusFailed"),
                RepositoryBatchStatus.Skipped => LocalizationManager.T("RepoUpdate.StatusSkipped"),
                _ => string.Empty
            };
        }
    }

    /// <summary>Произвольный текст статуса (например, причина пропуска или текст ошибки).</summary>
    public void SetStatusText(string text) => SetProperty(ref _statusText, text, nameof(StatusText));

    /// <summary>Затраченное время (для строки статуса).</summary>
    public string DurationText
    {
        get => _durationText;
        set => SetProperty(ref _durationText, value);
    }
}

/// <summary>
/// ViewModel окна «Обновление из хранилищ» (0.3.9.88): список баз с заполненным
/// хранилищем, последовательный прогон выбранных через конфигуратор в пакетном
/// режиме (<see cref="OneCLauncher.RunDesignerBatch"/> с операцией
/// <see cref="OneCLauncher.DesignerBatchOperation.RepositoryUpdate"/>) и построчный
/// лог. Чистый .NET, обе платформы (WPF и Avalonia). Окна только вызывают методы
/// и привязываются к коллекциям — вся логика здесь.
/// </summary>
public sealed class RepositoryBatchUpdateViewModel : ViewModelBase
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(60);

    private readonly SynchronizationContext? _syncContext;
    private bool _isRunning;
    private string _summary = string.Empty;

    public RepositoryBatchUpdateViewModel(IEnumerable<Infobase> infobases)
    {
        _syncContext = SynchronizationContext.Current;
        foreach (var ib in infobases)
        {
            if (ib?.Repository is { HasServer: true })
                Items.Add(new RepositoryBatchItem(ib));
        }
    }

    /// <summary>Строки списка (только базы с заполненным адресом хранилища).</summary>
    public ObservableCollection<RepositoryBatchItem> Items { get; } = new();

    /// <summary>Строки лога прогона (по одной строке на шаг).</summary>
    public ObservableCollection<string> LogLines { get; } = new();

    /// <summary>Идёт ли прогон в данный момент (блокирует кнопки окна).</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    /// <summary>Итоговая строка по завершении прогона.</summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>Есть ли хоть одна база с заполненным хранилищем.</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>Отметить/снять все строки (запущенные базы не отмечаются).</summary>
    public void SetAll(bool value)
    {
        foreach (var item in Items)
        {
            if (!item.Infobase.IsRunning)
                item.IsChecked = value;
        }
    }

    /// <summary>
    /// Последовательно выполняет обновление конфигурации из хранилища для всех
    /// отмеченных баз. Для каждой базы запускает конфигуратор в пакетном режиме
    /// и ждёт завершения; результат (успех/ошибка/время) пишется в лог построчно.
    /// Базы, запущенные в данный момент (флаг <see cref="Infobase.IsRunning"/>,
    /// монитор 0.3.9.82), пропускаются с пометкой. По завершении успешных баз
    /// добавляется запись истории запусков AddLaunchHistory("RepoUpdate").
    /// </summary>
    public async Task RunAsync()
    {
        if (IsRunning)
            return;

        IsRunning = true;
        Summary = string.Empty;
        LogLines.Clear();
        foreach (var item in Items)
        {
            item.IsBusy = false;
            item.DurationText = string.Empty;
            item.SetStatusText(string.Empty);
            if (item.Status != RepositoryBatchStatus.Running)
                item.Status = RepositoryBatchStatus.Pending;
        }

        var selected = Items.Where(i => i.IsChecked).ToList();
        if (selected.Count == 0)
        {
            AppendLog(LocalizationManager.T("RepoUpdate.NothingSelected"));
            IsRunning = false;
            return;
        }

        int ok = 0, failed = 0, skipped = 0;
        AppendLog(string.Format(LocalizationManager.T("RepoUpdate.StartedFormat"), selected.Count));

        foreach (var item in selected)
        {
            var ib = item.Infobase;
            item.IsBusy = true;
            item.DurationText = string.Empty;

            // Пропуск запущенных баз: конфигуратор/предприятие этой базы открыты
            // (индикатор «зелёная точка», монитор из 0.3.9.82) — обновлять нельзя.
            if (ib.IsRunning)
            {
                skipped++;
                item.Status = RepositoryBatchStatus.Skipped;
                item.SetStatusText(LocalizationManager.T("RepoUpdate.SkipRunning"));
                AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {LocalizationManager.T("RepoUpdate.SkipRunning")}");
                item.IsBusy = false;
                continue;
            }

            var startedAt = DateTime.Now;
            AppendLog($"[{startedAt:HH:mm:ss}] «{ib.Name}» → {ib.Repository?.AddressDisplay ?? string.Empty}");
            var started = OneCLauncher.RunDesignerBatch(ib, OneCLauncher.DesignerBatchOperation.RepositoryUpdate);
            if (!started)
            {
                failed++;
                item.Status = RepositoryBatchStatus.Failed;
                item.SetStatusText(LocalizationManager.T("RepoUpdate.StartFailed"));
                item.DurationText = FormatElapsed(DateTime.Now - startedAt);
                AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {LocalizationManager.T("RepoUpdate.StartFailed")} ({FormatElapsed(DateTime.Now - startedAt)})");
                item.IsBusy = false;
                continue;
            }

            var info = await WaitForCompletionAsync(ib);
            var elapsed = DateTime.Now - startedAt;

            if (info is { Success: true })
            {
                ok++;
                item.Status = RepositoryBatchStatus.Success;
                item.DurationText = FormatElapsed(elapsed);
                AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {LocalizationManager.T("RepoUpdate.OperationOk")} ({FormatElapsed(elapsed)})");
                ib.AddLaunchHistory("RepoUpdate",
                    string.Format(LocalizationManager.T("RepoUpdate.HistoryOkFormat"),
                        ib.Repository?.AddressDisplay ?? string.Empty, FormatElapsed(elapsed)));
            }
            else
            {
                failed++;
                item.Status = RepositoryBatchStatus.Failed;
                item.DurationText = FormatElapsed(elapsed);
                var errorText = info is null
                    ? string.Format(LocalizationManager.T("RepoUpdate.TimeoutFormat"), DefaultTimeout.TotalMinutes)
                    : string.IsNullOrWhiteSpace(info.ErrorMessage)
                        ? string.Format(LocalizationManager.T("RepoUpdate.ExitCodeFormat"), info.ExitCode)
                        : info.ErrorMessage.Trim();
                item.SetStatusText(errorText);
                AppendLog($"[{DateTime.Now:HH:mm:ss}] «{ib.Name}» — {LocalizationManager.T("RepoUpdate.OperationFailed")} ({FormatElapsed(elapsed)}): {errorText}");
                ib.AddLaunchHistory("RepoUpdate",
                    string.Format(LocalizationManager.T("RepoUpdate.HistoryFailFormat"),
                        ib.Repository?.AddressDisplay ?? string.Empty, FormatElapsed(elapsed)));
            }
            item.IsBusy = false;
        }

        Summary = string.Format(LocalizationManager.T("RepoUpdate.SummaryFormat"), ok, failed, skipped, selected.Count);
        AppendLog("— " + Summary + " —");
        IsRunning = false;
    }

    /// <summary>
    /// Ожидает завершения конкретной пакетной операции DESIGNER (по имени базы)
    /// через событие <see cref="OneCLauncher.DesignerBatchCompleted"/> с таймаутом.
    /// Операции выполняются последовательно, поэтому активна только одна.
    /// </summary>
    private async Task<OneCLauncher.DesignerBatchInfo?> WaitForCompletionAsync(Infobase infobase)
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        var tcs = new TaskCompletionSource<OneCLauncher.DesignerBatchInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<OneCLauncher.DesignerBatchInfo> handler = (_, info) =>
        {
            if (info.Operation == OneCLauncher.DesignerBatchOperation.RepositoryUpdate &&
                string.Equals(info.InfobaseName, infobase.Name, StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(info);
            }
        };
        OneCLauncher.DesignerBatchCompleted += handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
            return completed == tcs.Task ? tcs.Task.Result : null;
        }
        finally
        {
            OneCLauncher.DesignerBatchCompleted -= handler;
        }
    }

    /// <summary>Добавляет строку в лог потокобезопасно (через захваченный контекст синхронизации).</summary>
    private void AppendLog(string line)
    {
        if (_syncContext is null)
        {
            LogLines.Add(line);
            return;
        }
        _syncContext.Post(_ =>
        {
            LogLines.Add(line);
            OnPropertyChanged(nameof(LogLines));
        }, null);
    }

    /// <summary>Форматирует интервал времени «м.с» (например «0:12» → «12 сек»).</summary>
    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        if (elapsed.TotalMinutes >= 1)
            return $"{elapsed.Minutes}:{elapsed.Seconds:D2}";
        return $"{Math.Max(1, (int)elapsed.TotalSeconds)} сек";
    }
}