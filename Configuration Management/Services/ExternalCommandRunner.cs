using System.Diagnostics;

namespace Configuration_Management.Services;

/// <summary>
/// Выполнение пользовательских команд при запуске базы (функция №8, 0.3.9.98):
/// «Выполнить перед запуском» и «Выполнить после запуска».
/// <para>
/// Команда — произвольная строка (скрипт/исполняемый файл с аргументами),
/// выполняется через системный shell: на Windows — <c>cmd /c <команда></c>,
/// на Linux — <c>sh -c <команда></c>. Экранирование кавычек сознательно
/// не выполняется: команда пользовательская и передаётся в shell как есть.
/// </para>
/// <para>
/// <see cref="RunAsync"/> ждёт завершения процесса (с таймаутом) и возвращает
/// успех по коду возврата 0; <see cref="RunDetached"/> запускает процесс
/// без ожидания (fire-and-forget) — используется для post-команды.
/// </para>
/// </summary>
public static class ExternalCommandRunner
{
    /// <summary>
    /// Таймаут выполнения pre-команды по умолчанию (30 секунд): при превышении
    /// команда считается неуспешной, но запуск базы НЕ блокируется — вызывающий
    /// код предупреждает пользователя и продолжает.
    /// </summary>
    public const int DefaultPreCommandTimeoutMs = 30_000;

    /// <summary>
    /// Собирает команду запуска для системного shell текущей платформы:
    /// Windows — <c>cmd.exe /c <команда></c>, Linux — <c>/bin/sh -c <команда></c>.
    /// Пустая/пробельная команда не приводит к ошибке — shell запускается с пустым телом.
    /// </summary>
    public static (string FileName, string Arguments) BuildShellCommand(string? command)
    {
        var cmd = (command ?? string.Empty).Trim();
        // Пустая команда — без хвостового пробела («/c»/«-c»), чтобы аргументы
        // оставались каноничными и тестируемыми.
#if WINDOWS
        return ("cmd.exe", cmd.Length == 0 ? "/c" : "/c " + cmd);
#else
        return ("/bin/sh", cmd.Length == 0 ? "-c" : "-c " + cmd);
#endif
    }

    /// <summary>
    /// Выполняет команду через shell с ожиданием завершения и таймаутом.
    /// Возвращает <c>true</c>, если процесс завершился с кодом 0. При превышении
    /// таймаута, ошибке запуска или исключении — <c>false</c> (процесс при
    /// таймауте принудительно завершается).
    /// </summary>
    public static async Task<bool> RunAsync(
        string? command,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        var (fileName, arguments) = BuildShellCommand(command);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Math.Max(1, timeoutMs));

        Process? process = null;
        try
        {
            process = new Process { StartInfo = CreateStartInfo(fileName, arguments) };
            if (!process.Start())
                return false;

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            // Таймаут: пробуем завершить процесс и считаем команду неуспешной.
            TryKill(process);
            return false;
        }
        catch (Exception)
        {
            // Ошибка запуска/ожидания — команда не выполнена, но это не должно
            // ронять вызывающий код (запуск базы продолжается).
            return false;
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Запускает команду через shell без ожидания завершения (fire-and-forget).
    /// Используется для post-команды после успешного старта 1С: результат
    /// команды не отслеживается.
    /// </summary>
    public static void RunDetached(string? command)
    {
        var (fileName, arguments) = BuildShellCommand(command);
        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(fileName, arguments) };
            if (!process.Start())
            {
                // Нечего сообщить: fire-and-forget по построению.
            }
        }
        catch (Exception)
        {
            // Игнорируем: post-команда не должна мешать уже запущенной базе.
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, string arguments)
    {
        // Без перенаправления вывода: поток чтения при большом объёме мог бы
        // заблокировать процесс (дедлок буфера), а консольное окно не создаём.
        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    /// <summary>Завершает процесс по таймауту (лучшее усилие, без ошибок).</summary>
    private static void TryKill(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Игнорируем: процесс мог завершиться сам.
        }
    }
}