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
    /// Собирает <see cref="ProcessStartInfo"/> для команды системного shell
    /// (см. <see cref="BuildShellCommand"/>). Публичный — для юнит-тестов проверки
    /// флага <see cref="ProcessStartInfo.CreateNoWindow"/> (issue #308: видимое окно
    /// скрипта-сценария).
    /// </summary>
    /// <param name="command">Команда (передаётся shell как есть).</param>
    /// <param name="createNoWindow">Не создавать консольное окно (<c>true</c> по умолчанию).</param>
    public static ProcessStartInfo CreateProcessStartInfo(string? command, bool createNoWindow = true)
    {
        var (fileName, arguments) = BuildShellCommand(command);
        // Видимое окно (createNoWindow == false) на Windows требует UseShellExecute = true
        // (запуск через shell с новым окном): с UseShellExecute = false процесс GUI-приложения
        // без консоли окна не создаёт даже при CreateNoWindow = false (issue #308). Дефолт
        // createNoWindow = true (pre/post-команды баз, CLI) остаётся на UseShellExecute = false.
        // На Linux флаг UseShellExecute не поддерживается, а окно там зависит от окружения —
        // оставляем как есть.
        var useShellExecute = !createNoWindow && OperatingSystem.IsWindows();
        // Без перенаправления вывода: поток чтения при большом объёме мог бы
        // заблокировать процесс (дедлок буфера), а консольное окно — по запросу.
        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = useShellExecute,
            CreateNoWindow = createNoWindow
        };
    }

    /// <summary>
    /// Выполняет команду через shell с ожиданием завершения и таймаутом.
    /// Возвращает <c>true</c>, если процесс завершился с кодом 0. При превышении
    /// таймаута, ошибке запуска или исключении — <c>false</c> (процесс при
    /// таймауте принудительно завершается).
    /// </summary>
    /// <param name="command">Команда (передаётся shell как есть).</param>
    /// <param name="timeoutMs">Таймаут ожидания завершения (мс).</param>
    /// <param name="createNoWindow">Не создавать консольное окно (по умолчанию <c>true</c>).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async Task<bool> RunAsync(
        string? command,
        int timeoutMs,
        bool createNoWindow = true,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Math.Max(1, timeoutMs));

        Process? process = null;
        try
        {
            process = new Process { StartInfo = CreateProcessStartInfo(command, createNoWindow) };
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
    /// Используется для post-команды после успешного старта 1С (и для сценариев
    /// запуска скриптов, issue #308): результат команды не отслеживается.
    /// </summary>
    /// <param name="command">Команда (передаётся shell как есть).</param>
    /// <param name="createNoWindow">Не создавать консольное окно (по умолчанию <c>true</c>).</param>
    public static void RunDetached(string? command, bool createNoWindow = true)
    {
        try
        {
            using var process = new Process { StartInfo = CreateProcessStartInfo(command, createNoWindow) };
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