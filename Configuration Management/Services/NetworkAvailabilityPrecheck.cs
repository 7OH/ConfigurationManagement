namespace Configuration_Management.Services;

/// <summary>
/// Быстрая TCP-проверка порта кластера перед COM-подключением в общей проверке
/// доступности баз (функция 12, этап 0.3.9.233). Работает только в сторону
/// «недоступно» (quick-fail): закрытый порт/таймаут — база точно недоступна,
/// COM вызывать незачем; открытый порт или любая неопределённость (пустой хост,
/// исключение зонда) — проверку продолжает COM, чтобы не давать ложных результатов.
/// Чистый статический класс без UI-зависимостей — обе платформы; зонд инжектируется
/// делегатом для юнит-тестов.
/// </summary>
public static class NetworkAvailabilityPrecheck
{
    /// <summary>Таймаут одного TCP-зонда, мс (заметно меньше ComDetectTimeoutMs ≥ 1000).</summary>
    public const int DefaultTimeoutMs = 1500;

    /// <summary>Решение по результату precheck.</summary>
    public enum Verdict
    {
        /// <summary>Проверка отключена настройкой — переходить к COM как раньше.</summary>
        Skip,

        /// <summary>Порт открыт или неопределённость — финальное решение за COM.</summary>
        Continue,

        /// <summary>Порт закрыт/таймаут — база недоступна, COM не вызывать.</summary>
        FailFast
    }

    /// <summary>
    /// Выполняет быструю TCP-проверку и выносит вердикт.
    /// </summary>
    /// <param name="enabled">Флаг настройки AvailabilityTcpPrecheckEnabled.</param>
    /// <param name="host">Хост из настроек базы (может содержать «host:port»).</param>
    /// <param name="port">Порт кластера из настроек базы (по умолчанию 1541).</param>
    /// <param name="tcpProbe">TCP-зонд; реальная реализация —
    /// <see cref="NetworkDiagnosticsService.TcpPortCheckAsync"/>.</param>
    public static async Task<Verdict> EvaluateAsync(
        bool enabled,
        string? host,
        int port,
        Func<string, int, int, CancellationToken, Task<TcpProbeResult>> tcpProbe,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (!enabled)
            return Verdict.Skip;

        var address = NetworkDiagnosticsService.ParseAddress(host, port);
        if (address is null)
            return Verdict.Continue; // некорректный адрес — пусть COM решит

        try
        {
            var probe = await tcpProbe(address.Host, address.Port, timeoutMs, cancellationToken)
                .ConfigureAwait(false);
            return probe.Reachable ? Verdict.Continue : Verdict.FailFast;
        }
        catch
        {
            // Неопределённость (резолв, сетевой сбой зонда) — не блокируем COM-проверку.
            return Verdict.Continue;
        }
    }

    /// <summary>
    /// Синхронная обёртка для использования из <c>IsBaseAvailable</c> (Parallel.ForEach):
    /// true — база точно недоступна (закрытый порт/таймаут), COM можно не вызывать;
    /// false — проверку продолжает COM (Skip/Continue).
    /// </summary>
    public static bool IsUnreachableFast(
        bool enabled,
        string? host,
        int port,
        Func<string, int, int, CancellationToken, Task<TcpProbeResult>> tcpProbe,
        int timeoutMs = DefaultTimeoutMs)
        => EvaluateAsync(enabled, host, port, tcpProbe, timeoutMs).GetAwaiter().GetResult()
           == Verdict.FailFast;
}