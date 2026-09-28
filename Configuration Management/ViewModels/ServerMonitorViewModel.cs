using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel окна «Серверы 1С» (0.3.9.124, цикл 0.3.9.123–0.3.9.126): подключение
/// к серверу 1С через rac (адрес/порт/логин/пароль), список кластеров, вкладки
/// «Рабочие процессы / Сеансы / Соединения / Блокировки / Информация о кластере»,
/// ручное обновление, действия (завершение сеанса / разрыв соединения с
/// подтверждением) и автообновление по таймеру 5 с (этап 3, 0.3.9.125).
/// Чистый .NET без платформенных зависимостей — обе платформы (WPF и Avalonia);
/// окна только привязываются.
/// </summary>
public sealed class ServerMonitorViewModel : ViewModelBase, IDisposable
{
    /// <summary>Период автообновления данных кластера, миллисекунды (~5 секунд).</summary>
    public const int AutoRefreshIntervalMs = 5000;

    private readonly IRacClient _rac;
    private readonly IDialogService _dialogs;
    private readonly Action<Action>? _dispatchToUi;
    private Timer? _autoRefreshTimer;
    private int _busy;

    private string _serverAddress = "localhost";
    private int _serverPort = IRacClient.DefaultPort;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private Guid? _selectedClusterId;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasConnected;
    private RacClusterInfo? _clusterInfo;
    private string _clusterInfoText = string.Empty;
    private RacSessionRow? _selectedSession;
    private RacConnectionRow? _selectedConnection;

    private ICommand? _connectCommand;
    private ICommand? _refreshCommand;
    private ICommand? _terminateSessionCommand;
    private ICommand? _disconnectConnectionCommand;

    /// <param name="rac">Клиент rac (список кластеров, данные кластера).</param>
    /// <param name="dialogs">Диалоги (сообщения об ошибках; подтверждения действий — этап 3).</param>
    /// <param name="dispatchToUi">
    /// Доставка применения результатов в UI-поток (передаёт окно); null — результаты
    /// применяются прямо из рабочего потока (тесты).
    /// </param>
    public ServerMonitorViewModel(
        IRacClient rac,
        IDialogService dialogs,
        Action<Action>? dispatchToUi = null)
    {
        _rac = rac ?? throw new ArgumentNullException(nameof(rac));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatchToUi = dispatchToUi;
    }

    // ===================== Подключение =====================

    /// <summary>Адрес сервера 1С (host или IP).</summary>
    public string ServerAddress
    {
        get => _serverAddress;
        set => SetProperty(ref _serverAddress, value?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// Порт агента сервера 1С (ragent), по умолчанию 1540; для RAS — 1545.
    /// Некорректные значения (<see cref="IRacClient.DefaultPort"/> при ≤ 0).
    /// </summary>
    public int ServerPort
    {
        get => _serverPort;
        set => SetProperty(ref _serverPort, value > 0 ? value : IRacClient.DefaultPort);
    }

    /// <summary>Логин администратора кластера (пустая строка — без аутентификации).</summary>
    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value ?? string.Empty);
    }

    /// <summary>
    /// Пароль администратора кластера. Живёт ТОЛЬКО в памяти окна (в тестах сеттер
    /// открытый; в UI пароль передаётся вручную из PasswordBox при подключении),
    /// на диск не сохраняется (см. решения планирования, раздел 5 плана).
    /// </summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value ?? string.Empty);
    }

    // ===================== Кластеры =====================

    /// <summary>Кластеры сервера (модели из rac «cluster list»).</summary>
    public IReadOnlyList<RacCluster> Clusters { get; private set; } = Array.Empty<RacCluster>();

    /// <summary>Кластеры для выпадающего списка (форматированные строки).</summary>
    public IReadOnlyList<RacClusterRow> ClusterRows { get; private set; } = Array.Empty<RacClusterRow>();

    /// <summary>
    /// Выбранный кластер. При установке значения после подключения запускается
    /// загрузка данных кластера (процессы/сеансы/соединения/блокировки/инфо).
    /// </summary>
    public Guid? SelectedClusterId
    {
        get => _selectedClusterId;
        set
        {
            if (!SetProperty(ref _selectedClusterId, value))
                return;
            if (value is Guid id && HasConnected)
                _ = LoadClusterDataAsync(id);
        }
    }

    // ===================== Команды =====================

    /// <summary>«Подключиться»: список кластеров, при успехе — данные первого кластера.</summary>
    public ICommand ConnectCommand =>
        _connectCommand ??= new RelayCommand(async () => await ConnectAsync());

    /// <summary>«Обновить»: перечитать данные выбранного кластера (после подключения).</summary>
    public ICommand RefreshCommand =>
        _refreshCommand ??= new RelayCommand(Refresh);

    // ===================== Действия =====================

    /// <summary>«Завершить сеанс»: подтверждение → session terminate → обновление списков.</summary>
    public ICommand TerminateSessionCommand =>
        _terminateSessionCommand ??= new RelayCommand(async () => await TerminateSessionAsync());

    /// <summary>«Разорвать соединение»: подтверждение → connection disconnect → обновление списков.</summary>
    public ICommand DisconnectConnectionCommand =>
        _disconnectConnectionCommand ??= new RelayCommand(async () => await DisconnectConnectionAsync());

    // ===================== Вкладки =====================

    /// <summary>Рабочие процессы кластера (rphost/rmngr).</summary>
    public ObservableCollection<RacProcessRow> Processes { get; } = new();

    /// <summary>Сеансы пользователей кластера.</summary>
    public ObservableCollection<RacSessionRow> Sessions { get; } = new();

    /// <summary>Соединения клиентов кластера.</summary>
    public ObservableCollection<RacConnectionRow> Connections { get; } = new();

    /// <summary>Блокировки объектов данных кластера.</summary>
    public ObservableCollection<RacLockRow> Locks { get; } = new();

    /// <summary>Информация о кластере (команда «cluster info»); null, если не получена.</summary>
    public RacClusterInfo? ClusterInfo
    {
        get => _clusterInfo;
        private set => SetProperty(ref _clusterInfo, value);
    }

    /// <summary>Текст вкладки «Информация о кластере» («ключ: значение» построчно).</summary>
    public string ClusterInfoText
    {
        get => _clusterInfoText;
        private set => SetProperty(ref _clusterInfoText, value);
    }

    /// <summary>Выбранный сеанс на вкладке «Сеансы» (кнопка «Завершить сеанс»).</summary>
    public RacSessionRow? SelectedSession
    {
        get => _selectedSession;
        set => SetProperty(ref _selectedSession, value);
    }

    /// <summary>Выбранное соединение на вкладке «Соединения» (кнопка «Разорвать соединение»).</summary>
    public RacConnectionRow? SelectedConnection
    {
        get => _selectedConnection;
        set => SetProperty(ref _selectedConnection, value);
    }

    // ===================== Автообновление =====================

    /// <summary>Запущен ли таймер автообновления (после успешного подключения).</summary>
    public bool AutoRefreshActive => _autoRefreshTimer is not null;

    /// <summary>Подпись состояния автообновления для подсказки окна.</summary>
    public string AutoRefreshText => AutoRefreshActive
        ? LocalizationManager.T("ServerMonitor.AutoRefreshOn")
        : LocalizationManager.T("ServerMonitor.AutoRefreshOff");

    // ===================== Статус =====================

    /// <summary>Строка состояния («Подключение…», «Кластеров: N», ошибки).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Подробное сообщение последней ошибки (для статус-строки и диалога).</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Успешно ли установлено подключение (список кластеров получен).</summary>
    public bool HasConnected
    {
        get => _hasConnected;
        private set => SetProperty(ref _hasConnected, value);
    }

    /// <summary>Выполняется ли сейчас запрос (флаг занятости исключает наложение, см. ProcessInspectorViewModel).</summary>
    public bool IsBusy => _busy == 1;

    // ===================== Действия =====================

    /// <summary>
    /// «Подключиться»: получить список кластеров (rac «cluster list»); при успехе —
    /// выбрать первый кластер и загрузить его данные. Ошибки не роняют окно —
    /// пишутся в статус-строку/ErrorMessage.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (!TryEnterBusy())
            return;

        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ServerMonitor.Status.Connecting");

            var clusters = await _rac.GetClustersAsync(BuildParams()).ConfigureAwait(false);

            ApplyClusters(clusters);
            HasConnected = true;
            StatusText = clusters.Count == 0
                ? LocalizationManager.T("ServerMonitor.Status.NoClusters")
                : string.Format(LocalizationManager.T("ServerMonitor.Status.ConnectedFormat"), clusters.Count);

            // Автообновление запускается только после успешного подключения.
            StartAutoRefresh();

            // Первый кластер выбираем автоматически — он же запускает загрузку данных.
            if (SelectedClusterId is null && ClusterRows.Count > 0)
                SelectedClusterId = ClusterRows[0].Id;
        }
        catch (Exception ex)
        {
            HasConnected = false;
            ErrorMessage = BuildErrorMessage(ex);
            StatusText = LocalizationManager.T("ServerMonitor.Status.ConnectFailed");
            // Без подключения таймер автообновления не работает.
            StopAutoRefresh();
        }
        finally
        {
            ExitBusy();
        }
    }

    /// <summary>
    /// Загружает данные выбранного кластера: процессы, сеансы, соединения, блокировки
    /// и «cluster info» (параллельно). Результаты применяются через <see cref="_dispatchToUi"/>
    /// (null — напрямую, тесты).
    /// </summary>
    public async Task LoadClusterDataAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        if (!TryEnterBusy())
            return;

        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ServerMonitor.Status.Loading");

            var parameters = BuildParams();
            var processesTask = _rac.GetProcessesAsync(parameters, clusterId, cancellationToken);
            var sessionsTask = _rac.GetSessionsAsync(parameters, clusterId, cancellationToken);
            var connectionsTask = _rac.GetConnectionsAsync(parameters, clusterId, cancellationToken);
            var locksTask = _rac.GetLocksAsync(parameters, clusterId, cancellationToken);
            var infoTask = _rac.GetClusterInfoAsync(parameters, clusterId, cancellationToken);

            await Task.WhenAll(
                processesTask, sessionsTask, connectionsTask, locksTask, infoTask)
                .ConfigureAwait(false);

            var processes = await processesTask.ConfigureAwait(false);
            var sessions = await sessionsTask.ConfigureAwait(false);
            var connections = await connectionsTask.ConfigureAwait(false);
            var locks = await locksTask.ConfigureAwait(false);
            var info = await infoTask.ConfigureAwait(false);

            ApplyClusterData(processes, sessions, connections, locks, info);

            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Status.LoadedFormat"),
                processes.Count, sessions.Count, connections.Count, locks.Count);
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("ServerMonitor.Status.Cancelled");
        }
        catch (Exception ex)
        {
            ErrorMessage = BuildErrorMessage(ex);
            StatusText = LocalizationManager.T("ServerMonitor.Status.LoadFailed");
        }
        finally
        {
            ExitBusy();
        }
    }

    /// <summary>«Обновить»: перечитать данные выбранного кластера (без подключения — no-op).</summary>
    public void Refresh()
    {
        if (!HasConnected || SelectedClusterId is not Guid id)
            return;
        _ = LoadClusterDataAsync(id);
    }

    /// <summary>
    /// «Завершить сеанс»: подтверждение (предупреждение о потере несохранённых данных),
    /// команда rac «session terminate», обновление списков; ошибка — предупреждение +
    /// статус-строка. Образец — KillSelected из ProcessInspectorViewModel.
    /// </summary>
    public async Task TerminateSessionAsync()
    {
        var row = SelectedSession;
        if (row is null || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        if (!_dialogs.Confirm(
                string.Format(LocalizationManager.T("ServerMonitor.TerminateConfirmFormat"), row.User),
                LocalizationManager.T("ServerMonitor.TerminateTitle")))
            return;

        try
        {
            var ok = await _rac.TerminateSessionAsync(BuildParams(), clusterId, row.Id).ConfigureAwait(false);
            if (!ok)
            {
                var detail = BuildActionError(_rac.LastActionError);
                _dialogs.ShowWarning(
                    string.Format(LocalizationManager.T("ServerMonitor.TerminateFailedFormat"), row.User) + "\n" + detail,
                    LocalizationManager.T("ServerMonitor.TerminateTitle"));
                StatusText = detail;
                return;
            }

            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Status.TerminatedFormat"), row.User);
        }
        catch (Exception ex)
        {
            _dialogs.ShowWarning(
                string.Format(LocalizationManager.T("ServerMonitor.TerminateFailedFormat"), row.User) + "\n" + BuildErrorMessage(ex),
                LocalizationManager.T("ServerMonitor.TerminateTitle"));
            StatusText = BuildErrorMessage(ex);
        }
        finally
        {
            // После действия списки перечитываются (сеанс мог исчезнуть).
            Refresh();
        }
    }

    /// <summary>
    /// «Разорвать соединение»: подтверждение, команда rac «connection disconnect»,
    /// обновление списков; ошибка — предупреждение + статус-строка.
    /// </summary>
    public async Task DisconnectConnectionAsync()
    {
        var row = SelectedConnection;
        if (row is null || !HasConnected || SelectedClusterId is not Guid clusterId)
            return;

        if (!_dialogs.Confirm(
                string.Format(LocalizationManager.T("ServerMonitor.DisconnectConfirmFormat"), row.Host),
                LocalizationManager.T("ServerMonitor.DisconnectTitle")))
            return;

        try
        {
            var ok = await _rac.DisconnectConnectionAsync(BuildParams(), clusterId, row.Id).ConfigureAwait(false);
            if (!ok)
            {
                var detail = BuildActionError(_rac.LastActionError);
                _dialogs.ShowWarning(
                    string.Format(LocalizationManager.T("ServerMonitor.DisconnectFailedFormat"), row.Host) + "\n" + detail,
                    LocalizationManager.T("ServerMonitor.DisconnectTitle"));
                StatusText = detail;
                return;
            }

            StatusText = string.Format(
                LocalizationManager.T("ServerMonitor.Status.DisconnectedFormat"), row.Host);
        }
        catch (Exception ex)
        {
            _dialogs.ShowWarning(
                string.Format(LocalizationManager.T("ServerMonitor.DisconnectFailedFormat"), row.Host) + "\n" + BuildErrorMessage(ex),
                LocalizationManager.T("ServerMonitor.DisconnectTitle"));
            StatusText = BuildErrorMessage(ex);
        }
        finally
        {
            Refresh();
        }
    }

    /// <summary>
    /// Запускает таймер автообновления данных выбранного кластера (5 с). Создаётся при
    /// успешном подключении; повторный запуск — no-op. Тик идёт через <see cref="Refresh"/>
    /// с тем же флагом занятости, что и ручное «Обновить» (наложение исключено).
    /// </summary>
    private void StartAutoRefresh()
    {
        _autoRefreshTimer ??= new Timer(
            _ => Refresh(), null, AutoRefreshIntervalMs, AutoRefreshIntervalMs);
        OnPropertyChanged(nameof(AutoRefreshActive));
        OnPropertyChanged(nameof(AutoRefreshText));
    }

    /// <summary>Останавливает таймер автообновления (Dispose окна, ошибка подключения).</summary>
    private void StopAutoRefresh()
    {
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
        OnPropertyChanged(nameof(AutoRefreshActive));
        OnPropertyChanged(nameof(AutoRefreshText));
    }

    /// <inheritdoc />
    /// <summary>Останавливает таймер автообновления (вызывается окном при закрытии).</summary>
    public void Dispose() => StopAutoRefresh();

    // ===================== Внутреннее =====================

    private RacConnectionParams BuildParams() => new()
    {
        Address = string.IsNullOrWhiteSpace(ServerAddress) ? "localhost" : ServerAddress.Trim(),
        Port = ServerPort > 0 ? ServerPort : IRacClient.DefaultPort,
        User = UserName?.Trim() ?? string.Empty,
        Password = Password ?? string.Empty
    };

    private void ApplyClusters(IReadOnlyList<RacCluster> clusters)
    {
        void Apply()
        {
            Clusters = clusters;
            ClusterRows = clusters
                .Where(c => c is not null)
                .Select(c => new RacClusterRow(c))
                .ToList();
            OnPropertyChanged(nameof(Clusters));
            OnPropertyChanged(nameof(ClusterRows));
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    private void ApplyClusterData(
        IReadOnlyList<RacProcessInfo> processes,
        IReadOnlyList<RacSessionInfo> sessions,
        IReadOnlyList<RacConnectionInfo> connections,
        IReadOnlyList<RacLockInfo> locks,
        RacClusterInfo? info)
    {
        void Apply()
        {
            // Выбор сохраняем по идентификатору: после автообновления строка с тем же
            // Id остаётся выбранной, исчезнувшая (завершённый сеанс) — сбрасывается.
            var sessionId = SelectedSession?.Id;
            var connectionId = SelectedConnection?.Id;

            ReplaceRows(Processes, processes.Select(p => new RacProcessRow(p)));
            ReplaceRows(Sessions, sessions.Select(s => new RacSessionRow(s)));
            ReplaceRows(Connections, connections.Select(c => new RacConnectionRow(c)));
            ReplaceRows(Locks, locks.Select(l => new RacLockRow(l)));
            ClusterInfo = info;
            ClusterInfoText = FormatClusterInfo(info);

            SelectedSession = sessionId is Guid s ? Sessions.FirstOrDefault(x => x.Id == s) : null;
            SelectedConnection = connectionId is Guid c ? Connections.FirstOrDefault(x => x.Id == c) : null;
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    private static void ReplaceRows<T>(ObservableCollection<T> target, IEnumerable<T> rows)
    {
        target.Clear();
        foreach (var row in rows)
            target.Add(row);
    }

    private static string FormatClusterInfo(RacClusterInfo? info)
    {
        if (info is null)
            return LocalizationManager.T("ServerMonitor.Empty.Info");

        var sb = new StringBuilder();
        if (info.Properties.Count > 0)
        {
            foreach (var pair in info.Properties)
                sb.AppendLine($"{pair.Key}: {pair.Value}");
        }
        else
        {
            // Резерв: если словарь пуст (не распознан), выводим типизированные поля.
            sb.AppendLine($"name: {info.Name}");
            sb.AppendLine($"hostName: {info.HostName}");
            sb.AppendLine($"port: {info.Port}");
            sb.AppendLine($"expirationTimeout: {info.ExpirationTimeout}");
            sb.AppendLine($"lifetimeLimit: {info.LifetimeLimit}");
            sb.AppendLine($"maxMemorySize: {info.MaxMemorySize}");
            sb.AppendLine($"maxMemoryTimeLimit: {info.MaxMemoryTimeLimit}");
            sb.AppendLine($"securityLevel: {info.SecurityLevel}");
            sb.AppendLine($"sessionIdleTimeout: {info.SessionIdleTimeout}");
            sb.AppendLine($"sessionMaxMemorySize: {info.SessionMaxMemorySize}");
            sb.AppendLine($"sessionMaxTimeLimit: {info.SessionMaxTimeLimit}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildErrorMessage(Exception ex)
    {
        var message = ex.Message;
        return string.IsNullOrWhiteSpace(message)
            ? LocalizationManager.T("ServerMonitor.Errors.Unknown")
            : message;
    }

    /// <summary>Текст ошибки действия rac (из <see cref="IRacClient.LastActionError"/>).</summary>
    private static string BuildActionError(string? lastActionError) =>
        string.IsNullOrWhiteSpace(lastActionError)
            ? LocalizationManager.T("ServerMonitor.Errors.Unknown")
            : lastActionError;

    private bool TryEnterBusy()
    {
        // Один запрос за раз: длительная rac-команда не должна копить очередь (см. ProcessInspectorViewModel:80).
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return false;
        OnPropertyChanged(nameof(IsBusy));
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        OnPropertyChanged(nameof(IsBusy));
    }
}