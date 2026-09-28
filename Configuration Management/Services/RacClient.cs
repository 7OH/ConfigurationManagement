using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Клиент утилиты rac (Remote Administration Client) — сервисный слой встроенного монитора
/// серверов 1С (цикл 0.3.9.123–0.3.9.126). Чистый сервис — без UI-зависимостей.
/// Команды выполняются прямым запуском rac (без shell, через ArgumentList) с захватом
/// stdout/stderr (UTF-8), параллельным чтением и таймаутом. Пароль администратора кластера
/// передаётся аргументом <c>--password</c>, но НЕ пишется в журнал (маскируется
/// <see cref="SensitiveDataMasker.MaskRacPassword"/>).
/// </summary>
public sealed class RacClient : IRacClient
{
    /// <summary>Таймаут выполнения одной rac-команды (30 секунд).</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private readonly IAppLogger _logger;

    public RacClient(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacCluster>> GetClustersAsync(
        RacConnectionParams parameters, CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, "cluster", "list")
            .ConfigureAwait(false);
        return RacOutputParser.ToClusters(output);
    }

    /// <inheritdoc />
    public async Task<RacClusterInfo?> GetClusterInfoAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, "cluster", "info",
                $"--cluster={clusterId}")
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(output) ? null : RacOutputParser.ToClusterInfo(output);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, "process", "list",
                $"--cluster={clusterId}")
            .ConfigureAwait(false);
        return RacOutputParser.ToProcesses(output);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, "session", "list",
                $"--cluster={clusterId}")
            .ConfigureAwait(false);
        return RacOutputParser.ToSessions(output);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, "connection", "list",
                $"--cluster={clusterId}")
            .ConfigureAwait(false);
        return RacOutputParser.ToConnections(output);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
        RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(parameters, cancellationToken, "lock", "list",
                $"--cluster={clusterId}")
            .ConfigureAwait(false);
        return RacOutputParser.ToLocks(output);
    }

    /// <inheritdoc />
    public Task<bool> TerminateSessionAsync(
        RacConnectionParams parameters, Guid clusterId, Guid sessionId,
        CancellationToken cancellationToken = default) =>
        RunActionAsync(parameters, cancellationToken, "session", "terminate",
            $"--cluster={clusterId}", $"--session={sessionId}");

    /// <inheritdoc />
    public Task<bool> DisconnectConnectionAsync(
        RacConnectionParams parameters, Guid clusterId, Guid connectionId,
        CancellationToken cancellationToken = default) =>
        RunActionAsync(parameters, cancellationToken, "connection", "disconnect",
            $"--cluster={clusterId}", $"--connection={connectionId}");

    /// <inheritdoc />
    public string LastActionError { get; private set; } = string.Empty;

    /// <summary>
    /// Собирает аргументы командной строки rac:
    /// <c>[--host=addr --port=N --user=U --password=P] <команда> [--cluster=uuid ...]</c>.
    /// Параметры подключения опускаются, если пусты (пароль/логин) или некорректны (порт ≤ 0).
    /// Internal — для юнит-тестов сборки аргументов (без запуска процесса).
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(
        RacConnectionParams parameters, params string[] commandAndArgs)
    {
        var args = new List<string>(commandAndArgs.Length + 5);
        if (!string.IsNullOrWhiteSpace(parameters.Address))
            args.Add($"--host={parameters.Address}");
        if (parameters.Port > 0)
            args.Add($"--port={parameters.Port}");
        if (!string.IsNullOrEmpty(parameters.User))
            args.Add($"--user={parameters.User}");
        if (!string.IsNullOrEmpty(parameters.Password))
            args.Add($"--password={parameters.Password}");
        args.AddRange(commandAndArgs);
        return args;
    }

    /// <summary>
    /// Выполняет rac-команду действия (завершение сеанса / разрыв соединения).
    /// Возвращает true при ExitCode 0; при неудаче — false и текст ошибки
    /// (сообщение rac + stderr) в <see cref="LastActionError"/>. Исключения наружу
    /// не пробрасываются: ViewModel показывает пользователю LastActionError.
    /// </summary>
    private async Task<bool> RunActionAsync(
        RacConnectionParams parameters, CancellationToken cancellationToken,
        params string[] commandAndArgs)
    {
        try
        {
            await RunAsync(parameters, cancellationToken, commandAndArgs).ConfigureAwait(false);
            LastActionError = string.Empty;
            return true;
        }
        catch (OperationCanceledException)
        {
            LastActionError = "Операция отменена.";
            return false;
        }
        catch (Exception ex)
        {
            LastActionError = string.IsNullOrWhiteSpace(ex.Message)
                ? "Неизвестная ошибка rac."
                : ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Выполняет rac-команду: поиск исполняемого файла, прямой запуск без shell
    /// (UseShellExecute=false, CreateNoWindow=true, UTF-8), параллельное чтение
    /// stdout/stderr, таймаут 30 с. Возвращает stdout при ExitCode 0; при ошибке —
    /// <see cref="RacClientException"/> с текстом stderr.
    /// </summary>
    private async Task<string> RunAsync(
        RacConnectionParams parameters, CancellationToken cancellationToken,
        params string[] commandAndArgs)
    {
        var args = BuildArguments(parameters, commandAndArgs);

        var rac = OneCPlatformLocator.FindRacExecutable();
        if (rac is null)
        {
            const string message =
                "Не найден исполняемый файл rac в каталоге установленной платформы 1С.";
            _logger.Warn($"RAC: {message}");
            throw new RacClientException(message);
        }

        // Пароль маскируется: в журнал rac-команда попадает без --password=<значение>.
        _logger.Info($"RAC: {SensitiveDataMasker.MaskRacPassword(string.Join(" ", args))}");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = rac,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            // ArgumentList — каждый аргумент отдельным токеном, без shell: корректная
            // передача значений с пробелами и спецсимволами (пароль, имена баз).
            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            if (!process.Start())
                throw new RacClientException($"Не удалось запустить rac: {rac}.");

            // Читаем stdout/stderr параллельно с ожиданием выхода: иначе большой вывод
            // может переполнить буфер канала и заблокировать завершение процесса.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CommandTimeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillProcess(process);
                if (cancellationToken.IsCancellationRequested)
                    throw;

                const string message = "Превышен таймаут ожидания ответа rac (30 с).";
                _logger.Warn($"RAC: {message}");
                throw new RacClientException(message);
            }

            var stdout = (await stdoutTask.ConfigureAwait(false)) ?? string.Empty;
            var stderr = (await stderrTask.ConfigureAwait(false)) ?? string.Empty;

            if (process.ExitCode != 0)
            {
                // Ненулевой код выхода rac: ошибка подключения, отсутствие прав либо
                // неподдерживаемая команда (например lock list на старых платформах).
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                var message = $"rac завершился с кодом {process.ExitCode}.";
                if (!string.IsNullOrWhiteSpace(detail))
                    message += " " + detail.Trim();

                _logger.Warn($"RAC: {message}");
                throw new RacClientException(message);
            }

            return stdout;
        }
        catch (RacClientException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Не удалось выполнить команду rac: {ex.Message}";
            _logger.Error($"RAC: {message}", ex);
            throw new RacClientException(message, ex);
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Процесс мог завершиться сам — игнорируем.
        }
    }
}