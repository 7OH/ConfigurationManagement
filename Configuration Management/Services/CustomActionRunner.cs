using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>Результат выполнения пользовательского действия для одной базы.</summary>
/// <param name="Infobase">База, для которой выполнялось действие.</param>
/// <param name="Success">true — команда завершилась успешно (код 0).</param>
/// <param name="TimedOut">true — выполнение прервано по таймауту действия.</param>
/// <param name="Error">Текст ошибки (пустая команда, исключение исполнителя и т.п.); null — успех.</param>
public sealed record CustomActionRunResult(Infobase Infobase, bool Success, bool TimedOut, string? Error);

/// <summary>
/// Оркестратор выполнения пользовательских действий контекстного меню (0.3.9.195, функция 7):
/// сборка командной строки действия через <see cref="ScriptParameterResolver"/>, выполнение
/// команды для одной базы через <see cref="ExternalCommandRunner.RunAsync"/> с таймаутом
/// (<see cref="CustomAction.TimeoutMs"/>) и параллельный запуск пакета
/// (<see cref="RunBatchAsync"/>, Task.WhenAll). Чистый .NET без платформенных зависимостей;
/// исполнитель переопределяется делегатом (в тестах — fake).
/// <para>
/// Ошибка или таймаут одной базы НЕ блокируют остальные: исключения внутри задач гасятся,
/// результаты возвращаются по всем целям в порядке входного списка. Пароль базы в команде
/// для журналов маскируется <see cref="MaskSecrets"/>.
/// </para>
/// </summary>
public sealed class CustomActionRunner
{
    private readonly Func<string, int, CancellationToken, Task<bool>> _executeAsync;

    /// <summary>
    /// Создаёт исполнитель. По умолчанию команда выполняется через
    /// <see cref="ExternalCommandRunner.RunAsync"/> (ожидание с таймаутом); в тестах
    /// передаётся fake-делегат.
    /// </summary>
    public CustomActionRunner(Func<string, int, CancellationToken, Task<bool>>? executeAsync = null)
    {
        // Method group ExternalCommandRunner.RunAsync имеет 4 параметра (2 опциональных) —
        // для делегата из трёх параметров нужна явная лямбда.
        _executeAsync = executeAsync
            ?? ((command, timeoutMs, ct) => ExternalCommandRunner.RunAsync(command, timeoutMs, cancellationToken: ct));
    }

    /// <summary>
    /// Собирает тело команды действия для базы: подстановка токенов команды
    /// (<see cref="CustomAction.Command"/>) с учётом флага <see cref="CustomAction.EscapeValues"/>
    /// и выбранного интерпретатора (<see cref="ScriptParameterResolver.BuildActionCommandLine"/>).
    /// Реальный запуск выполняет это тело через shell без повторной обёртки.
    /// </summary>
    public string BuildCommandLine(CustomAction action, Infobase infobase, DateTime? now = null)
        => ScriptParameterResolver.BuildActionCommandLine(action, infobase, now);

    /// <summary>
    /// Полная командная строка действия с обёрткой выбранного интерпретатора
    /// (<see cref="ScriptParameterResolver.BuildActionShellCommandLine"/>) — для истории
    /// запусков базы и журнала (после <see cref="MaskSecrets"/>).
    /// </summary>
    public string BuildLogCommandLine(CustomAction action, Infobase infobase, DateTime? now = null)
        => ScriptParameterResolver.BuildActionShellCommandLine(action, infobase, now);

    /// <summary>
    /// Маскирует секреты в командной строке для лога/истории: точное значение пароля
    /// заменяется на «***» (в том числе в экранированном виде <c>"pass"</c>/<c>'pass'</c> —
    /// простая подстановка подстроки). Пустой пароль — строка без изменений.
    /// </summary>
    public static string MaskSecrets(string commandLine, string? password)
        => SensitiveDataMasker.MaskValue(commandLine, password);

    /// <summary>
    /// Выполняет действие для одной базы: команда = <see cref="BuildCommandLine"/>;
    /// пустая/пробельная команда → результат <c>(false, false, "EmptyCommand")</c> без
    /// вызова исполнителя. Иначе вызывает исполнитель с таймаутом
    /// <see cref="CustomAction.TimeoutMs"/>; таймаут определяется трекером времени вокруг
    /// вызова (лучшее усилие — реальный таймаут возвращает false спустя ≥ TimeoutMs),
    /// исключения делегата ловятся внутри и превращаются в результат с сообщением.
    /// </summary>
    public async Task<CustomActionRunResult> RunOneAsync(
        CustomAction action, Infobase infobase, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(infobase);

        var command = BuildCommandLine(action, infobase);
        if (string.IsNullOrWhiteSpace(command))
            return new CustomActionRunResult(infobase, false, false, "EmptyCommand");

        var sw = Stopwatch.StartNew();
        try
        {
            var ok = await _executeAsync(command, action.TimeoutMs, ct).ConfigureAwait(false);
            sw.Stop();

            // Таймаут: исполнитель вернул false, а время вызова не меньше порога
            // (минус 50 мс — допуск на точность измерения). Мгновенный false fake-исполнителя
            // (код ошибки) таймаутом не считается.
            var timedOut = !ok && sw.ElapsedMilliseconds >= Math.Max(0, action.TimeoutMs - 50);
            return new CustomActionRunResult(infobase, ok, timedOut, ok ? null : (timedOut ? "Timeout" : "Failed"));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CustomActionRunResult(infobase, false, false, ex.Message);
        }
    }

    /// <summary>
    /// Выполняет действие для набора баз ПАРАЛЛЕЛЬНО (<see cref="Task.WhenAll"/>). Возвращает
    /// результаты по всем целям; исключения внутри каждой задачи гасятся (успех остальных
    /// баз не зависит). Порядок результатов соответствует порядку целей.
    /// </summary>
    public async Task<IReadOnlyList<CustomActionRunResult>> RunBatchAsync(
        CustomAction action, IReadOnlyList<Infobase> targets, CancellationToken ct)
    {
        if (action is null || targets is null || targets.Count == 0)
            return Array.Empty<CustomActionRunResult>();

        var valid = targets.Where(t => t is not null).ToList();
        if (valid.Count == 0)
            return Array.Empty<CustomActionRunResult>();

        var tasks = valid.Select(t => RunOneAsync(action, t, ct)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}