using System;
using System.IO;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика сопоставления командной строки процесса 1С с информационной базой
/// (индикатор «база сейчас запущена»). Не зависит от платформы и UI — покрыта
/// юнит-тестами. Ключи подключения: файловая — <c>/F &lt;каталог&gt;</c>,
/// клиент-серверная — <c>/S &lt;сервер&gt;\&lt;база&gt;</c>; после ключа путь может
/// идти через пробел или без него в кавычках (<c>/F"C:\Bases\ib"</c>).
/// Веб-базы не сопоставляются (ключ /WS встречается и у прочих сценариев).
/// </summary>
public static class RunningInfobaseMatcher
{
    /// <summary>Сопоставляет базу с командной строкой процесса 1С.</summary>
    public static bool MatchesCommandLine(Infobase infobase, string? commandLine)
    {
        if (infobase?.Connection is null || string.IsNullOrWhiteSpace(commandLine))
            return false;

        return infobase.Connection.Type switch
        {
            ConnectionType.File => MatchesValue(commandLine, "F", infobase.Connection.FilePath ?? ""),
            ConnectionType.ClientServer => MatchesValue(commandLine, "S",
                $"{infobase.Connection.Server}\\{infobase.Connection.DatabaseName}"),
            _ => false
        };
    }

    /// <summary>
    /// Ищет в командной строке ключ «/буква» (или «-буква») и сравнивает следующее
    /// за ним значение с ожидаемым. Значение в cmdline: остаток строки до следующего
    /// ключа «/», без кавычек. Сравнение путей — без учёта регистра и хвостовых
    /// разделителей; пустое ожидаемое значение не сопоставляется никогда.
    /// </summary>
    internal static bool MatchesValue(string commandLine, string key, string expected)
    {
        var value = (expected ?? "").Trim();
        if (value.Length == 0)
            return false;

        var line = commandLine ?? "";
        var search = "/" + key;

        var index = 0;
        while ((index = line.IndexOf(search, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            // Ключ должен стоять отдельным токеном: начало строки или пробел перед ним,
            // чтобы не поймать «/S» внутри «/SDanything» (сравнение ниже всё равно
            // отсечёт лишнее, но пробел-условие убирает ложные ключи вида «/Out»).
            var tokenStart = index == 0 || line[index - 1] == ' ' || line[index - 1] == '\t';
            index += search.Length;

            if (!tokenStart)
                continue;

            // После ключа допускается кавычка и/или пробел: /F"path", /F "path", /Fpath.
            var rest = line[index..].TrimStart().TrimStart('"');

            // Значение заканчивается перед следующим ключом «/...» или «-...».
            var end = rest.Length;
            for (var i = 1; i < rest.Length; i++)
            {
                if ((rest[i] == '/' || rest[i] == '-') && (rest[i - 1] == ' ' || rest[i - 1] == '"'))
                {
                    end = i;
                    break;
                }
            }

            var actual = rest[..end].Trim().TrimEnd('"').Trim();

            if (PathsEqual(actual, value))
                return true;
        }

        return false;
    }

    /// <summary>Сравнение путей/строк подключения без регистра и хвостовых разделителей.</summary>
    internal static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        var na = NormalizePath(a);
        var nb = NormalizePath(b);
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Нормализация пути: полная форма, без хвостовых разделителей; при ошибке — trim.</summary>
    internal static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
