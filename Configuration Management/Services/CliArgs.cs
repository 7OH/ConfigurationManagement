using System;
using System.Linq;

namespace Configuration_Management.Services;

/// <summary>Команда CLI, распознанная в аргументах командной строки (функция 10).</summary>
public enum CliCommandKind
{
    /// <summary>Аргументы не содержат команд CLI — обычный запуск приложения.</summary>
    None,

    /// <summary>Запустить базу (--run / --base).</summary>
    Run,

    /// <summary>Вывести список баз (--list).</summary>
    List,

    /// <summary>Добавить базу (--add, этап 0.3.9.219).</summary>
    Add,

    /// <summary>Резервная копия одной базы (--backup, этап 0.3.9.220).</summary>
    Backup,

    /// <summary>Резервная копия всех видимых баз (--backup-all, этап 0.3.9.220).</summary>
    BackupAll,

    /// <summary>Статус базы (--status, этап 0.3.9.221).</summary>
    Status,

    /// <summary>Экспорт списка баз в файл (--export, этап 0.3.9.222).</summary>
    Export,

    /// <summary>Справка по командам (--help, этап 0.3.9.218).</summary>
    Help
}

/// <summary>Общие параметры любой команды CLI.</summary>
public sealed class CliOptions
{
    /// <summary>Машиночитаемый вывод (--json): результат печатается единым JSON-контрактом.</summary>
    public bool Json { get; set; }

    /// <summary>Профиль сессии (--profile <id>).</summary>
    public string? Profile { get; set; }

    /// <summary>Модификатор режима запуска (--designer) — только для --run.</summary>
    public bool Designer { get; set; }
}

/// <summary>Результат разбора аргументов командной строки.</summary>
public sealed class CliParseResult
{
    /// <summary>Распознанная команда.</summary>
    public CliCommandKind Kind { get; set; } = CliCommandKind.None;

    /// <summary>Целевой аргумент команды (имя/id базы, файл экспорта и т.п.).</summary>
    public string? Target { get; set; }

    /// <summary>Общие параметры.</summary>
    public CliOptions Options { get; set; } = new();

    /// <summary>Текст ошибки валидации комбинации ключей (null — разбор успешен).</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Парсер аргументов командной строки CLI (функция 10). Чистая функция — покрыта юнит-тестами.
/// Поддерживает формы <c>--key value</c> и <c>--key=value</c>, значения в кавычках,
/// глобальные флаги <c>--json</c>/<c>--profile</c> и модификатор <c>--designer</c>.
/// Возвращает null, если аргументы не содержат команд CLI (обычный запуск приложения).
/// </summary>
public static class CliArgs
{
    /// <summary>
    /// Разбирает аргументы командной строки. Возвращает null, если команд CLI нет.
    /// При невалидной комбинации ключей возвращает результат с заполненным <see cref="CliParseResult.Error"/>
    /// (кроме исторического поведения: <c>--run</c> без цели и <c>--designer</c> без <c>--run</c>
    /// трактуются как отсутствие команд — null, совместимость со старым CLI).
    /// </summary>
    public static CliParseResult? Parse(string[]? args)
    {
        if (args is null || args.Length == 0)
            return null;

        var action = CliCommandKind.None;
        var target = string.Empty;
        var options = new CliOptions();

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
                action = CliCommandKind.List;
                continue;
            }

            if (string.Equals(key, "--json", StringComparison.OrdinalIgnoreCase))
            {
                options.Json = true;
                continue;
            }

            if (string.Equals(key, "--profile", StringComparison.OrdinalIgnoreCase))
            {
                options.Profile = value ?? ReadNextValue(args, ref i);
                continue;
            }

            var isRun = string.Equals(key, "--run", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "--base", StringComparison.OrdinalIgnoreCase);
            if (!isRun)
                continue;

            // --list имеет приоритет: смешанный запуск трактуем как вывод списка.
            if (action != CliCommandKind.List)
                action = CliCommandKind.Run;

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
        // режима запуска, а не команда проводника (она обрабатывается ранее, ExplorerCommandLine).
        if (args.Any(a => a.TrimStart('-').Equals("designer", StringComparison.OrdinalIgnoreCase)
                          && !a.Contains('=')))
            options.Designer = true;

        if (action == CliCommandKind.None)
            return null;

        if (action == CliCommandKind.Run && string.IsNullOrWhiteSpace(target))
            return null;

        return new CliParseResult { Kind = action, Target = target, Options = options };
    }

    /// <summary>Читает значение ключа из следующего аргумента (форма «--key значение»).</summary>
    private static string? ReadNextValue(string[] args, ref int i)
    {
        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
        {
            i++;
            return args[i].Trim().Trim('"');
        }

        return null;
    }
}