using System;
using System.Linq;
using Configuration_Management.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Configuration_Management.Services;

/// <summary>
/// Выполнение команд CLI (функция 10). Команды получают сервисы из <see cref="AppServices"/>
/// и возвращают единый код возврата (см. <see cref="CliExitCodes"/>). Чистые части логики
/// выносятся в тестируемые методы по мере добавления команд на последующих этапах.
/// </summary>
public static class CliCommands
{
    /// <summary>Диспетчер команд: выполняет распознанную команду и возвращает код выхода.</summary>
    public static int Execute(CliParseResult parsed)
    {
        var command = CommandName(parsed.Kind);

        return parsed.Kind switch
        {
            CliCommandKind.List => ExecuteList(command, parsed.Options),
            CliCommandKind.Run => ExecuteRun(command, parsed.Target!, parsed.Options),
            _ => Unsupported(command, parsed.Options)
        };
    }

    // --------------------------------------------------------------- список (--list)

    /// <summary>
    /// Вывод списка баз. Plain-режим сохраняет исторический формат <c>Name\tkind\tId</c>
    /// (совместимость со скриптами); при --json — детали в едином контракте.
    /// Приватные базы заблокированного профиля скрываются (0.3.9.85).
    /// </summary>
    internal static int ExecuteList(string command, CliOptions options)
    {
        var repository = AppServices.GetRequiredService<IInfobaseRepository>();
        var logger = AppServices.GetRequiredService<IAppLogger>();
        var profileService = AppServices.GetRequiredService<IProfileService>();
        bool IsPrivateVisible(Infobase ib) => !ib.IsPrivate || profileService.CanShowPrivateBases;

        var visible = repository.Load()
            .Where(IsPrivateVisible)
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Json)
        {
            var items = visible.Select(ib => new
            {
                name = ib.Name,
                kind = KindOf(ib),
                id = ib.Id,
                group = ib.Group,
                connection = ib.Connection?.ToConnectionString() ?? string.Empty,
                tags = ib.Tags ?? new System.Collections.Generic.List<string>(),
                isFavorite = ib.IsFavorite,
                isPinned = ib.IsPinned,
                lastLaunch = ib.LastLaunchDate,
                lastBackup = ib.LastBackupUtc
            });

            CliOutput.WriteResult(CliResult.Success(command, new { bases = items }), json: true);
        }
        else
        {
            foreach (var ib in visible)
                Console.WriteLine($"{ib.Name}\t{KindOf(ib)}\t{ib.Id}");
        }

        logger.Info("[cli] Выведен список баз (--list)");
        return CliExitCodes.Success;
    }

    // ---------------------------------------------------------------- запуск (--run)

    /// <summary>
    /// Запуск базы (--run "Имя"/--base): поиск по Id, затем по имени без учёта регистра;
    /// pre/post-команды пользовательских скриптов (функция 8); история запуска.
    /// Коды возврата мигрированы на единый контракт (не найдено — 2, ошибка запуска — 1,
    /// приватная база — 1 с error.code=private_base).
    /// </summary>
    internal static int ExecuteRun(string command, string target, CliOptions options)
    {
        var repository = AppServices.GetRequiredService<IInfobaseRepository>();
        var logger = AppServices.GetRequiredService<IAppLogger>();
        var profileService = AppServices.GetRequiredService<IProfileService>();
        bool IsPrivateVisible(Infobase ib) => !ib.IsPrivate || profileService.CanShowPrivateBases;

        var bases = repository.Load();
        var infobase = bases.FirstOrDefault(b =>
            string.Equals(b.Id, target, StringComparison.OrdinalIgnoreCase))
            ?? bases.FirstOrDefault(b =>
                string.Equals(b.Name.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase));

        if (infobase is null)
        {
            var message = $"База не найдена: '{target}'";
            CliOutput.WriteResult(CliResult.Failure(command, "base_not_found", message), options.Json);
            logger.Warn($"[cli] База не найдена: '{target}'");
            return CliExitCodes.NotFound;
        }

        // Приватная база заблокированного профиля: запуск из CLI запрещён (0.3.9.85).
        // Разблокировать её можно только в работающем приложении паролем профиля.
        if (!IsPrivateVisible(infobase))
        {
            var message =
                $"База «{infobase.Name}» приватная: откройте приложение и разблокируйте " +
                "приватные базы паролем профиля (меню «Утилиты» → «Открыть приватные базы»).";
            CliOutput.WriteResult(CliResult.Failure(command, "private_base", message), options.Json);
            logger.Warn($"[cli] Отказ запуска приватной базы «{infobase.Name}» (профиль не разблокирован)");
            return CliExitCodes.Error;
        }

        var mode = options.Designer ? OneCLaunchMode.Configurator : OneCLaunchMode.Enterprise;
        var launcher = AppServices.GetRequiredService<IOneCLauncher>();

        // Пользовательские скрипты при запуске базы (0.3.9.98, функция №8):
        // pre-команда выполняется с ожиданием (до 30 секунд). При ошибке или
        // таймауте запуск НЕ блокируется — пишем предупреждение и продолжаем.
        if (!string.IsNullOrWhiteSpace(infobase.PreLaunchCommand))
        {
            var preOk = ExternalCommandRunner.RunAsync(
                infobase.PreLaunchCommand,
                ExternalCommandRunner.DefaultPreCommandTimeoutMs).GetAwaiter().GetResult();
            if (preOk)
            {
                logger.Info($"[cli] Pre-команда «{infobase.Name}» выполнена: {infobase.PreLaunchCommand}");
            }
            else
            {
                CliOutput.WriteDiagnostic(
                    $"Внимание: не удалось выполнить команду перед запуском: {infobase.PreLaunchCommand}");
                logger.Warn($"[cli] Pre-команда «{infobase.Name}» завершилась с ошибкой: {infobase.PreLaunchCommand}");
            }
        }

        var ok = launcher.Launch(infobase, mode);

        if (ok)
        {
            // Post-команда — fire-and-forget, без ожидания завершения.
            if (!string.IsNullOrWhiteSpace(infobase.PostLaunchCommand))
            {
                ExternalCommandRunner.RunDetached(infobase.PostLaunchCommand);
                logger.Info($"[cli] Запущена post-команда «{infobase.Name}»: {infobase.PostLaunchCommand}");
            }

            // История запуска: если приложение уже запущено, его сохранение
            // перезапишет файл последним — запись истории может потеряться,
            // это допустимо (полные JSON-файлы, «побеждает последний»).
            infobase.LastLaunchDate = DateTime.Now;
            infobase.AddLaunchHistory(options.Designer ? "Configurator" : "Enterprise", BuildCliLaunchDetails(infobase));
            try { repository.Save(bases); }
            catch { /* параллельная запись — не ошибка CLI-запуска */ }

            if (options.Json)
            {
                CliOutput.WriteResult(CliResult.Success(command, new
                {
                    name = infobase.Name,
                    mode = options.Designer ? "configurator" : "enterprise"
                }), json: true);
            }

            logger.Info($"[cli] Запущена «{infobase.Name}» ({mode})");
            return CliExitCodes.Success;
        }

        var failMessage = $"Не удалось запустить «{infobase.Name}»";
        CliOutput.WriteResult(CliResult.Failure(command, "launch_failed", failMessage), options.Json);
        logger.Warn($"[cli] Не удалось запустить «{infobase.Name}» ({mode})");
        return CliExitCodes.Error;
    }

    // ------------------------------------------------------------ не реализовано

    /// <summary>Команды будущих этапов цикла (add/backup/status/export/help) — до реализации
    /// парсером не распознаются, поэтому сюда не попадают; ветка — страховка диспетчера.</summary>
    private static int Unsupported(string command, CliOptions options)
    {
        CliOutput.WriteResult(
            CliResult.Failure(command, "invalid_args", $"Команда не поддерживается: {command}"),
            options.Json);
        return CliExitCodes.Error;
    }

    /// <summary>Тип подключения базы для вывода: server/web/file.</summary>
    internal static string KindOf(Infobase ib) => ib.Connection?.Type switch
    {
        ConnectionType.ClientServer => "server",
        ConnectionType.WebServer => "web",
        _ => "file"
    };

    /// <summary>Имя команды для контракта вывода (нижний регистр).</summary>
    internal static string CommandName(CliCommandKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>
    /// Детали истории запуска из CLI с маркером пользовательских команд (0.3.9.98):
    /// например «cli; pre: ras connect …; post: start …».
    /// </summary>
    private static string BuildCliLaunchDetails(Infobase infobase)
    {
        var parts = new System.Collections.Generic.List<string> { "cli" };
        if (!string.IsNullOrWhiteSpace(infobase.PreLaunchCommand))
            parts.Add("pre: " + infobase.PreLaunchCommand);
        if (!string.IsNullOrWhiteSpace(infobase.PostLaunchCommand))
            parts.Add("post: " + infobase.PostLaunchCommand);
        return string.Join("; ", parts);
    }
}