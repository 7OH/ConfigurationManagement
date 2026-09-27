using System;
using System.Linq;

namespace Configuration_Management.Services;

/// <summary>Действие, распознанное в аргументах командной строки.</summary>
public enum CommandLineAction
{
    /// <summary>Аргументы не содержат команд CLI — обычный запуск приложения.</summary>
    None,

    /// <summary>Запустить базу (--run).</summary>
    Run,

    /// <summary>Вывести список баз (--list).</summary>
    List
}

/// <summary>
/// Обработчик командной строки для ярлыков и скриптов: <c>--run "База" [--designer]</c>
/// и <c>--list</c>. Выполняется после инициализации активного профиля (репозиторий
/// уже указывает на его каталог данных) и до проверки одиночного экземпляра:
/// команда выполняется вторым процессом напрямую — запуск базы через
/// <see cref="IOneCLauncher"/> не требует интерфейса, поэтому IPC с уже
/// запущенным приложением не нужен. Окно приложения при этом не показывается.
/// <para>
/// Команды контекстного меню проводника (--register/--launch/--designer с путём)
/// обрабатываются ранее классом <see cref="ExplorerCommandLine"/>: он вызывается
/// первым и «съедает» свои аргументы. Здесь <c>--designer</c> — только флаг-модификатор
/// к <c>--run</c> (без значения).
/// </para>
/// </summary>
public static class CommandLineHandler
{
    /// <summary>
    /// Разбирает аргументы командной строки. Возвращает null, если аргументы не
    /// содержат команд CLI (обычный запуск). Чистая функция — покрыта юнит-тестами.
    /// </summary>
    public static (CommandLineAction Action, string Target, bool Designer)? Parse(string[]? args)
    {
        if (args is null || args.Length == 0)
            return null;

        var action = CommandLineAction.None;
        var target = string.Empty;
        var designer = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            var eq = arg.IndexOf('=');
            var key = (eq >= 0 ? arg[..eq] : arg).Trim();
            var value = eq >= 0 ? arg[(eq + 1)..].Trim().Trim('"') : null;

            if (string.Equals(key, "--list", StringComparison.OrdinalIgnoreCase))
            {
                action = CommandLineAction.List;
                continue;
            }

            var isRun = string.Equals(key, "--run", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "--base", StringComparison.OrdinalIgnoreCase);
            if (!isRun)
                continue;

            // --list имеет приоритет: смешанный запуск трактуем как вывод списка.
            if (action != CommandLineAction.List)
                action = CommandLineAction.Run;

            // Формы: --run "Имя" / --run="Имя" / --run="Имя" --designer.
            if (value is not null)
            {
                target = value;
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                target = args[i + 1].Trim().Trim('"');
                i++;
            }
        }

        // --designer имеет смысл только вместе с --run; без значения — это модификатор
        // режима запуска, а не команда проводника (она обрабатывается ранее).
        if (args.Any(a => a.TrimStart('-').Equals("designer", StringComparison.OrdinalIgnoreCase)
                          && !a.Contains('=')))
            designer = true;

        if (action == CommandLineAction.None)
            return null;

        if (action == CommandLineAction.Run && string.IsNullOrWhiteSpace(target))
            return null;

        return (action, target, designer);
    }

    /// <summary>
    /// Пытается обработать аргументы как команду CLI. Возвращает <c>true</c>, если
    /// аргументы содержали команду (независимо от успеха действия) — вызывающий код
    /// должен завершить приложение с <paramref name="exitCode"/>, не показывая окно.
    /// </summary>
    public static bool TryHandle(string[]? args, out int exitCode)
    {
        exitCode = 0;
        var parsed = Parse(args);
        if (parsed is null)
            return false;

        var (action, target, designer) = parsed.Value;

        try
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch { /* вывод может быть перенаправлен — не критично */ }

            var repository = AppServices.GetRequiredService<IInfobaseRepository>();
            var logger = AppServices.GetRequiredService<IAppLogger>();

            if (action == CommandLineAction.List)
            {
                foreach (var ib in repository.Load().OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var kind = ib.Connection?.Type switch
                    {
                        Models.ConnectionType.ClientServer => "server",
                        Models.ConnectionType.WebServer => "web",
                        _ => "file"
                    };
                    Console.WriteLine($"{ib.Name}\t{kind}\t{ib.Id}");
                }

                logger.Info("[cli] Выведен список баз (--list)");
                return true;
            }

            // Run: поиск по Id, затем по имени (первое совпадение без учёта регистра).
            var bases = repository.Load();
            var infobase = bases.FirstOrDefault(b =>
                string.Equals(b.Id, target, StringComparison.OrdinalIgnoreCase))
                ?? bases.FirstOrDefault(b =>
                    string.Equals(b.Name.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase));

            if (infobase is null)
            {
                Console.Error.WriteLine($"[cli] База не найдена: '{target}'");
                logger.Warn($"[cli] База не найдена: '{target}'");
                exitCode = 1;
                return true;
            }

            var mode = designer ? OneCLaunchMode.Configurator : OneCLaunchMode.Enterprise;
            var launcher = AppServices.GetRequiredService<IOneCLauncher>();
            var ok = launcher.Launch(infobase, mode);

            if (ok)
            {
                // История запуска: если приложение уже запущено, его сохранение
                // перезапишет файл последним — запись истории может потеряться,
                // это допустимо (полные JSON-файлы, «побеждает последний»).
                infobase.LastLaunchDate = DateTime.Now;
                infobase.AddLaunchHistory(designer ? "Configurator" : "Enterprise", "cli");
                try { repository.Save(bases); }
                catch { /* параллельная запись — не ошибка CLI-запуска */ }

                logger.Info($"[cli] Запущена «{infobase.Name}» ({mode})");
            }
            else
            {
                Console.Error.WriteLine($"[cli] Не удалось запустить «{infobase.Name}»");
                logger.Warn($"[cli] Не удалось запустить «{infobase.Name}» ({mode})");
                exitCode = 2;
            }

            return true;
        }
        catch (Exception ex)
        {
            try
            {
                Console.Error.WriteLine("[cli] Ошибка обработки команды: " + ex.Message);
                AppServices.GetRequiredService<IAppLogger>().Error("[cli] Ошибка обработки команды: " + ex);
            }
            catch { /* логирование не должно маскировать ошибку */ }
            exitCode = 3;
            return true;
        }
    }
}
