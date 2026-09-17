using System.IO;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чтение параметров запуска по умолчанию из файла <c>1CLaunch.cfg</c> (Этап 10
/// дорожной карты StartManager). Платформенно-нейтральный сервис: используется и WPF,
/// и Avalonia-сборками.
///
/// Формат файла — простой «ключ=значение» (по одному на строку), поддерживаются
/// комментарии (<c>#</c>/<c>;</c>) и значения в двойных кавычках. Распознаются ключи:
/// <c>configpath</c> (asp/usp/sp), <c>configdir</c>, <c>appmode</c>, <c>selectmodeoff</c>.
///
/// Приоритет параметров (от меньшего к большему):
/// <list type="number">
/// <item>значения из <c>1CLaunch.cfg</c>;</item>
/// <item>аргументы командной строки приложения (переменная <c>--<ключ>=значение</c>
/// либо <c>-<ключ> <значение></c>) — они перекрывают одноимённые значения файла.</item>
/// </list>
///
/// Результат также конвертируется в список аргументов запуска 1С
/// (<see cref="OneCLaunchArgument"/>) для подстановки лаунчером в качестве значений по
/// умолчанию, если те же ключи не заданы параметрами самой базы / командной строкой.
/// </summary>
public static class OneCLaunchConfigReader
{
    /// <summary>Имя файла конфигурации.</summary>
    public const string FileName = "1CLaunch.cfg";

    /// <summary>Распознаваемые ключи конфигурации.</summary>
    public static readonly IReadOnlyList<string> KnownKeys =
        new[] { "configpath", "configdir", "appmode", "selectmodeoff" };

    /// <summary>
    /// Находит файл <c>1CLaunch.cfg</c>. Поиск ведётся в порядке приоритета:
    /// портативный каталог данных → каталог исполняемого файла → системный каталог данных.
    /// Возвращает null, если файл не найден.
    /// </summary>
    public static string? FindConfigFile()
    {
        var candidates = new List<string>();
        AddIfExists(candidates, Path.Combine(PortablePaths.PortableAppDataDirectory, FileName));
        AddIfExists(candidates, Path.Combine(PortablePaths.ApplicationDirectory, FileName));
        AddIfExists(candidates, Path.Combine(PlatformPaths.AppDataDirectory, FileName));
        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// Читает <c>1CLaunch.cfg</c> и возвращает параметры по умолчанию с учётом приоритета
    /// командной строки над файлом. Если файл не найден — параметры из командной строки
    /// (если есть) или пустые значения.
    /// </summary>
    public static LaunchConfigDefaults ReadDefaults()
    {
        var fromFile = ParseFile(FindConfigFile());
        var merged = ApplyCommandLine(fromFile);
        return merged;
    }

    /// <summary>
    /// Возвращает параметры по умолчанию из <c>1CLaunch.cfg</c> (и командной строки)
    /// в виде списка аргументов запуска 1С. Ключи без значения (пусто) пропускаются,
    /// а <c>selectmodeoff=0</c> не формирует аргумент-флаг.
    /// </summary>
    public static IReadOnlyList<OneCLaunchArgument> ToLaunchArguments(LaunchConfigDefaults defaults)
    {
        var result = new List<OneCLaunchArgument>();
        if (defaults is null)
            return result;

        if (!string.IsNullOrWhiteSpace(defaults.ConfigPath))
            result.Add(new OneCLaunchArgument("/ConfigurationPath", defaults.ConfigPath.Trim()));
        if (!string.IsNullOrWhiteSpace(defaults.ConfigDir))
            result.Add(new OneCLaunchArgument("/ConfigurationDir", defaults.ConfigDir.Trim()));
        if (!string.IsNullOrWhiteSpace(defaults.AppMode))
            result.Add(new OneCLaunchArgument("/AppMode", defaults.AppMode.Trim()));
        if (defaults.SelectModeOff)
            result.Add(new OneCLaunchArgument("/SelectModeOff"));

        return result;
    }

    // ---------------------------------------------------------------- internals

    private static void AddIfExists(List<string> list, string path)
    {
        try
        {
            if (File.Exists(path))
                list.Add(path);
        }
        catch
        {
            // Игнорируем недоступные пути.
        }
    }

    /// <summary>Разбирает INI-подобный файл конфигурации в словарь ключ→значение (без учета регистра).</summary>
    private static Dictionary<string, string> ParseFile(string? path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return result;

        try
        {
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim().Trim('"');
                if (string.IsNullOrEmpty(key))
                    continue;

                result[key] = value;
            }
        }
        catch
        {
            // Файл мог быть занят/испорчен — возвращаем то, что удалось разобрать.
        }

        return result;
    }

    /// <summary>Накладывает аргументы командной строки приложения поверх значений файла.</summary>
    private static LaunchConfigDefaults ApplyCommandLine(Dictionary<string, string> fromFile)
    {
        var values = new Dictionary<string, string>(fromFile, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ParseCommandLine())
            values[key] = value;

        var configPath = Get(values, "configpath");
        var configDir = Get(values, "configdir");
        var appMode = Get(values, "appmode");
        var selectModeOff = ParseBool(Get(values, "selectmodeoff"));

        return new LaunchConfigDefaults
        {
            ConfigPath = configPath,
            ConfigDir = configDir,
            AppMode = appMode,
            SelectModeOff = selectModeOff
        };
    }

    /// <summary>Возвращает значение по ключу (регистронезависимо) или пустую строку.</summary>
    private static string Get(IReadOnlyDictionary<string, string> dict, string key)
    {
        return dict.TryGetValue(key, out var v) ? (v ?? string.Empty).Trim() : string.Empty;
    }

    /// <summary>Разбирает булево значение (1/true/yes/on — true).</summary>
    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Trim().Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Разбирает командную строку приложения на пары ключ→значение вида
    /// <c>--configpath=asp</c>, <c>-configdir <путь></c>, <c>/appmode:1</c>.
    /// Служебные аргументы без «=»/разделителя игнорируются.
    /// </summary>
    private static Dictionary<string, string> ParseCommandLine()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        if (args.Count == 0)
            return result;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var trimmed = arg.TrimStart('-', '/');
            var colon = trimmed.IndexOf(':');
            var equals = trimmed.IndexOf('=');
            var sep = colon >= 0 && (equals < 0 || colon < equals) ? colon : equals;
            if (sep > 0)
            {
                var key = trimmed[..sep].Trim();
                var value = trimmed[(sep + 1)..].Trim().Trim('"');
                if (KnownKeys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                    result[key] = value;
                continue;
            }

            // Форма «-key value».
            if (KnownKeys.Any(k => string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Count)
            {
                result[trimmed] = args[i + 1].Trim().Trim('"');
                i++;
            }
        }

        return result;
    }
}