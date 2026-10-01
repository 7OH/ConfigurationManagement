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
    private IReadOnlyList<int> _currentPorts;
    private int _busy;

    private string _host;
    private string _statusText = string.Empty;
    private string _dnsText = string.Empty;
    private string _ipAddressesText = string.Empty;
    private string _pingText = string.Empty;

    /// <param name="target">Цель диагностики (хост + стартовые порты).</param>
    /// <param name="dispatchToUi">Доставка применения результатов в UI-поток; null — прямо (тесты).</param>
    public NetworkDiagnosticsViewModel(
        INetworkDiagnosticsService service,
        NetworkDiagnosticsTarget target,
        Action<Action>? dispatchToUi = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        _dispatchToUi = dispatchToUi;
        _host = target.Host;
        _currentPorts = target.Ports.ToList();

        RunCommand = new RelayCommand(_ => _ = RunAsync(), _ => !IsRunning);
        CheckPortsCommand = new RelayCommand(_ => _ = CheckPortsAsync(), _ => !IsRunning);
        RetryCommand = new RelayCommand(_ => _ = RetryAsync(), _ => !IsRunning);
    }

    /// <summary>Адрес диагностики (редактируемое поле окна).</summary>
    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
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

    /// <summary>Полная проверка: DNS + ICMP + TCP по текущему набору портов.</summary>
    public ICommand RunCommand { get; }

    /// <summary>Проверка стандартных портов 1С: 1540 (агент) / 1541 (кластер) / 1545 (RAS).</summary>
    public ICommand CheckPortsCommand { get; }

    /// <summary>Повтор последней проверки (с текущим значением поля адреса).</summary>
    public ICommand RetryCommand { get; }

    /// <inheritdoc cref="RunCommand"/>
    public Task RunAsync() => RunCoreAsync(_currentPorts);

    /// <inheritdoc cref="CheckPortsCommand"/>
    public Task CheckPortsAsync() =>
        RunCoreAsync(new[] { OneCPorts.Agent, OneCPorts.Cluster, OneCPorts.Ras });

    /// <inheritdoc cref="RetryCommand"/>
    public Task RetryAsync() => RunCoreAsync(_currentPorts);

    private async Task RunCoreAsync(IReadOnlyList<int> ports)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return; // повторный запуск во время прогона игнорируется

        try
        {
            _currentPorts = ports;
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
        if (RetryCommand is RelayCommand r3) r3.RaiseCanExecuteChanged();
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