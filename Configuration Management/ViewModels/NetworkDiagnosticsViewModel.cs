using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel окна «Диагностика подключения» (функция 12, этап 0.3.9.231): карточка
/// хоста (DNS-статус, IP-адреса, пинг/RTT), таблица портов с состояниями и задержками,
/// выводы-подсказки, команды «Проверить» / «Повторить» / «Проверить порты 1С».
/// Чистый .NET без платформенных зависимостей — обе платформы (WPF и Avalonia);
/// окна только привязываются. Результат публикуется через <see cref="_dispatchToUi"/>
/// (паттерн ServerMonitorViewModel); null — применение прямо из рабочего потока (тесты).
/// </summary>
public sealed class NetworkDiagnosticsViewModel : ViewModelBase
{
    private readonly INetworkDiagnosticsService _service;
    private readonly Action<Action>? _dispatchToUi;
    private readonly IServerPortsStore? _portsStore;
    private int _busy;

    private string _host;
    private string _selectedServer = string.Empty;
    private int _port;
    private string _statusText = string.Empty;
    private string _dnsText = string.Empty;
    private string _ipAddressesText = string.Empty;
    private string _pingText = string.Empty;
    private bool _suppressPortSync;

    /// <param name="target">Цель диагностики (хост + стартовые порты).</param>
    /// <param name="dispatchToUi">Доставка применения результатов в UI-поток; null — прямо (тесты).</param>
    /// <param name="portsStore">Хранилище портов в разрезе сервера (issue #335); null — не запоминать.</param>
    /// <param name="availableServers">Список известных серверов 1С для выпадающего списка.</param>
    public NetworkDiagnosticsViewModel(
        INetworkDiagnosticsService service,
        NetworkDiagnosticsTarget target,
        Action<Action>? dispatchToUi = null,
        IServerPortsStore? portsStore = null,
        IEnumerable<string>? availableServers = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        _dispatchToUi = dispatchToUi;
        _portsStore = portsStore;
        _host = target.Host?.Trim() ?? string.Empty;

        // Список известных серверов: внешние источники (клиент-серверные базы,
        // сохранённые порты) + адрес цели. Адрес цели включается гарантированно —
        // даже когда внешний список пуст, поле «Серверы» заполнено текущим
        // сервером (issue #335: «в поле Серверы — ничего нет — пусто»).
        var servers = new List<string>();
        if (availableServers is not null)
        {
            foreach (var s in availableServers
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Select(x => x!.Trim()))
                servers.Add(s);
        }
        if (!string.IsNullOrEmpty(_host) &&
            !servers.Contains(_host, StringComparer.OrdinalIgnoreCase))
            servers.Add(_host);

        foreach (var s in servers
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            AvailableServers.Add(s);

        // Стартовый порт: порт цели (базы/монитора), иначе сохранённый для этого
        // сервера порт кластера, иначе порт кластера по умолчанию (issue #335 —
        // раньше порт цели «помнился», но в поле не попадал).
        var savedForHost = GetSaved(_host);
        _port = target.Ports.Count > 0 && target.Ports[0] > 0
            ? target.Ports[0]
            : (savedForHost.Cluster > 0 ? savedForHost.Cluster : OneCPorts.Cluster);

        RunCommand = new RelayCommand(_ => _ = RunAsync(), _ => !IsRunning);
        CheckPortsCommand = new RelayCommand(_ => _ = CheckPortsAsync(), _ => !IsRunning);
        CheckRepositoryCommand = new RelayCommand(_ => _ = CheckRepositoryAsync(), _ => !IsRunning);
        RetryCommand = new RelayCommand(_ => _ = RetryAsync(), _ => !IsRunning);

        // Выбранный сервер из списка — без подстановки порта (стартовый порт уже
        // вычислен выше): программная установка не должна перетирать его.
        _suppressPortSync = true;
        SelectedServer = AvailableServers.Contains(_host, StringComparer.OrdinalIgnoreCase)
            ? _host
            : string.Empty;
        _suppressPortSync = false;
    }

    /// <summary>Адрес диагностики (редактируемое поле окна).</summary>
    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    /// <summary>Известные серверы 1С для выпадающего списка (имена без портов).</summary>
    public ObservableCollection<string> AvailableServers { get; } = new();

    /// <summary>
    /// Выбранный сервер из списка. При смене пользователем подставляет адрес и
    /// сохранённый для этого сервера порт кластера (issue #335).
    /// </summary>
    public string SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (!SetProperty(ref _selectedServer, value))
                return;
            if (_suppressPortSync || string.IsNullOrWhiteSpace(value))
                return;

            var server = value.Trim();
            Host = server;
            var saved = GetSaved(server);
            Port = saved.Cluster > 0 ? saved.Cluster : OneCPorts.Cluster;
        }
    }

    /// <summary>Активный порт проверки (порт кластера по умолчанию, если не задан).</summary>
    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    /// <summary>Строковое представление порта для текстового поля окна.</summary>
    public string PortText
    {
        get => _port > 0 ? _port.ToString() : string.Empty;
        set
        {
            if (!int.TryParse(value, out var parsed) || parsed is < 1 or > 65535)
                parsed = 0;
            Port = parsed;
        }
    }

    /// <summary>true — прогон выполняется (команды заблокированы).</summary>
    public bool IsRunning => _busy > 0;

    /// <summary>Статус-строка: «Проверка…», итог по времени или текст ошибки.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>DNS-статус карточки хоста.</summary>
    public string DnsText
    {
        get => _dnsText;
        private set => SetProperty(ref _dnsText, value);
    }

    /// <summary>Разрешённые IP-адреса карточки хоста (через запятую) или «—».</summary>
    public string IpAddressesText
    {
        get => _ipAddressesText;
        private set => SetProperty(ref _ipAddressesText, value);
    }

    /// <summary>Результат пинга: «12 мс» / «не отвечает» / «не проверено (нет прав)».</summary>
    public string PingText
    {
        get => _pingText;
        private set => SetProperty(ref _pingText, value);
    }

    /// <summary>Строки таблицы портов.</summary>
    public ObservableCollection<PortDiagnosticRow> Ports { get; } = new();

    /// <summary>Выводы/подсказки по результату (уже локализованные тексты).</summary>
    public ObservableCollection<string> Hints { get; } = new();

    /// <summary>Полная проверка: DNS + ICMP + TCP по активному порту поля.</summary>
    public ICommand RunCommand { get; }

    /// <summary>Проверка стандартных портов 1С: 1540 (агент) / 1541 (кластер) /
    /// 1542 (хранилище) / 1545 (RAS) — issue #335.</summary>
    public ICommand CheckPortsCommand { get; }

    /// <summary>Проверка порта сервера хранилища 1С (issue #335): сохранённый
    /// или порт по умолчанию 1542.</summary>
    public ICommand CheckRepositoryCommand { get; }

    /// <summary>Повтор последней проверки (с текущим значением полей адреса и порта).</summary>
    public ICommand RetryCommand { get; }

    /// <inheritdoc cref="RunCommand"/>
    public Task RunAsync() =>
        RunCoreAsync(new[] { EffectivePort }, remember: p => p with { Cluster = EffectivePort });

    /// <inheritdoc cref="CheckPortsCommand"/>
    public Task CheckPortsAsync() =>
        RunCoreAsync(new[]
        {
            OneCPorts.Agent, OneCPorts.Cluster, OneCPorts.Repository, OneCPorts.Ras
        });

    /// <inheritdoc cref="CheckRepositoryCommand"/>
    public Task CheckRepositoryAsync()
    {
        var saved = GetSaved(Host);
        var repositoryPort = saved.Repository > 0 ? saved.Repository : OneCPorts.Repository;
        return RunCoreAsync(
            new[] { repositoryPort },
            remember: p => p with { Repository = repositoryPort });
    }

    /// <inheritdoc cref="RetryCommand"/>
    public Task RetryAsync() =>
        RunCoreAsync(new[] { EffectivePort }, remember: p => p with { Cluster = EffectivePort });

    /// <summary>Порт для проверки: значение поля или порт кластера по умолчанию.</summary>
    private int EffectivePort => Port > 0 ? Port : OneCPorts.Cluster;

    /// <summary>Возвращает сохранённые порты сервера (пустая запись, если их нет).</summary>
    private ServerPortsSettings GetSaved(string? server)
    {
        if (_portsStore is null)
            return ServerPortsSettings.Empty;

        var host = (server ?? string.Empty).Trim();
        if (host.Length == 0)
            return ServerPortsSettings.Empty;

        try
        {
            return _portsStore.Load().TryGetValue(host, out var saved) && saved is not null
                ? saved
                : ServerPortsSettings.Empty;
        }
        catch
        {
            return ServerPortsSettings.Empty;
        }
    }

    /// <summary>Сохраняет порты для текущего сервера (обновляя заданные поля).</summary>
    private void RememberPorts(Func<ServerPortsSettings, ServerPortsSettings> transform)
    {
        if (_portsStore is null)
            return;

        var host = Host?.Trim() ?? string.Empty;
        if (host.Length == 0)
            return;

        try
        {
            _portsStore.Save(host, transform(GetSaved(host)));
        }
        catch
        {
            // Несохранение порта не должно ломать проверку.
        }
    }

    private async Task RunCoreAsync(
        IReadOnlyList<int> ports,
        Func<ServerPortsSettings, ServerPortsSettings>? remember = null)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return; // повторный запуск во время прогона игнорируется

        try
        {
            Publish(() =>
            {
                StatusText = LocalizationManager.T("Diagnostics.StatusRunning");
                OnIsRunningChanged();
            });

            var sw = Stopwatch.StartNew();
            NetworkDiagnosticsResult result;
            try
            {
                result = await _service.RunAsync(Host, ports).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Publish(() => StatusText = string.Format(
                    LocalizationManager.T("Diagnostics.ErrorFormat"), ex.Message));
                return;
            }
            finally
            {
                sw.Stop();
            }

            var elapsedSeconds = sw.Elapsed.TotalSeconds;
            Publish(() => Apply(result, elapsedSeconds));
        }
        finally
        {
            // Порт, введённый пользователем, запоминается в разрезе сервера независимо
            // от результата проверки (issue #335): при следующей смене сервера он
            // подставится в поле.
            if (remember is not null)
                RememberPorts(remember);

            Interlocked.Exchange(ref _busy, 0);
            Publish(OnIsRunningChanged);
        }
    }

    private void Apply(NetworkDiagnosticsResult result, double elapsedSeconds)
    {
        // Карточка хоста.
        DnsText = !result.HostValid || !result.DnsOk
            ? LocalizationManager.T("Diagnostics.DnsFailed")
            : IPAddress.TryParse(result.Host, out _)
                ? LocalizationManager.T("Diagnostics.DnsSkippedIp")
                : LocalizationManager.T("Diagnostics.DnsOk");

        IpAddressesText = result.ResolvedAddresses.Count > 0
            ? string.Join(", ", result.ResolvedAddresses)
            : LocalizationManager.T("Diagnostics.NoteEmpty");

        PingText = !result.PingPerformed
            ? LocalizationManager.T("Diagnostics.PingNotChecked")
            : result.PingOk
                ? string.Format(LocalizationManager.T("Diagnostics.PingOkFormat"), result.PingRttMs)
                : LocalizationManager.T("Diagnostics.PingNotAnswered");

        // Таблица портов.
        Ports.Clear();
        foreach (var probe in result.Ports)
            Ports.Add(new PortDiagnosticRow(probe));

        // Выводы.
        Hints.Clear();
        foreach (var hint in NetworkDiagnosticsHints.Build(result))
            Hints.Add(FormatHint(hint));

        // Итоговый статус.
        var hasProblems = !result.HostValid || !result.DnsOk ||
                          result.Ports.Any(p => p.State is DiagnosticPortState.Closed or DiagnosticPortState.Timeout);
        StatusText = hasProblems
            ? LocalizationManager.T("Diagnostics.StatusUnavailable")
            : string.Format(LocalizationManager.T("Diagnostics.StatusDoneFormat"), elapsedSeconds);
    }

    private void OnIsRunningChanged()
    {
        // Команды перечитывают CanExecute (WPF — автоматически через RequerySuggested;
        // явный вызов для Avalonia-зеркала RelayCommand).
        if (RunCommand is RelayCommand r1) r1.RaiseCanExecuteChanged();
        if (CheckPortsCommand is RelayCommand r2) r2.RaiseCanExecuteChanged();
        if (CheckRepositoryCommand is RelayCommand r3) r3.RaiseCanExecuteChanged();
        if (RetryCommand is RelayCommand r4) r4.RaiseCanExecuteChanged();
    }

    private void Publish(Action action)
    {
        if (_dispatchToUi is null)
            action();
        else
            _dispatchToUi(action);
    }

    /// <summary>
    /// Локализует подсказку: ключ словаря + аргументы; аргументы-ключи сервисов
    /// (Diagnostics.Port*) переводятся до форматирования.
    /// </summary>
    private static string FormatHint(NetworkDiagnosticHint hint)
    {
        if (hint.Args.Count == 0)
            return LocalizationManager.T(hint.Key);

        var args = hint.Args
            .Select(a => a is string s && s.StartsWith("Diagnostics.", StringComparison.Ordinal)
                ? LocalizationManager.T(s)
                : a)
            .ToArray();
        return string.Format(LocalizationManager.T(hint.Key), args);
    }
}