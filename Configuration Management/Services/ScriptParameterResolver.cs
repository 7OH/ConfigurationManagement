using System.Text.RegularExpressions;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая подстановка параметров сценариев запуска скриптов (issue #308).
/// <para>
/// Поддерживаются токены вида <c>%ключ%</c>: имя базы (<c>%name%</c>), свойства
/// подключения через точку (<c>%connection.server%</c>, <c>%connection.database%</c>,
/// <c>%connection.filePath%</c> и т.д.) и текущая дата — <c>%date%</c> (по умолчанию
/// <c>yyyy-MM-dd</c>) либо <c>%date:Формат%</c> (формат .NET, например
/// <c>%date:yyyyMMdd_HHmm%</c>). Неизвестный ключ по умолчанию остаётся в строке
/// как есть; при <c>leaveUnknown=false</c> заменяется пустой строкой.
/// </para>
/// <para>
/// Класс не зависит от UI-платформы и покрыт юнит-тестами (ScriptParameterResolverTests).
/// </para>
/// </summary>
public static class ScriptParameterResolver
{
    private static readonly Regex TokenPattern = new("%([^%]+)%", RegexOptions.Compiled);

    /// <summary>Формат даты по умолчанию для токена <c>%date%</c>.</summary>
    public const string DefaultDateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Собирает словарь значений подстановок для информационной базы: плоские ключи
    /// (<c>name</c>) и свойства подключения с префиксом <c>connection.</c>.
    /// </summary>
    public static Dictionary<string, string> BuildValueMap(Infobase? infobase)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (infobase is null)
            return map;

        map["name"] = infobase.Name ?? "";

        var conn = infobase.Connection;
        if (conn is null)
            return map;

        map["connection.type"] = conn.Type.ToString();
        map["connection.server"] = conn.Server ?? "";
        map["connection.serverPort"] = conn.GetServerWithPort();
        map["connection.database"] = conn.DatabaseName ?? "";
        map["connection.filePath"] = conn.FilePath ?? "";
        map["connection.webUrl"] = conn.WebUrl ?? "";
        map["connection.port"] = conn.Port.ToString();
        map["connection.connectionString"] = conn.ToConnectionString();
        map["connection.user"] = conn.User ?? "";
        return map;
    }

    /// <summary>
    /// Выполняет подстановку токенов в шаблоне. Момент времени <paramref name="now"/>
    /// передаётся явно для тестируемости; <c>null</c> — текущее время.
    /// </summary>
    /// <param name="template">Шаблон с токенами <c>%…%</c>.</param>
    /// <param name="values">Значения подстановок (см. <see cref="BuildValueMap"/>).</param>
    /// <param name="now">Момент времени для <c>%date%</c>/<c>%date:…%</c>.</param>
    /// <param name="leaveUnknown">Оставлять неизвестный токен как есть (<c>true</c>)
    /// или заменять пустой строкой (<c>false</c>).</param>
    public static string Resolve(
        string? template,
        IReadOnlyDictionary<string, string>? values,
        DateTime? now = null,
        bool leaveUnknown = true)
    {
        if (string.IsNullOrEmpty(template))
            return template ?? "";

        var current = now ?? DateTime.Now;
        return TokenPattern.Replace(template, match =>
        {
            var token = match.Groups[1].Value.Trim();
            if (token.Equals("date", StringComparison.OrdinalIgnoreCase))
                return SafeFormat(current, DefaultDateFormat);
            if (token.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
                return SafeFormat(current, token.Substring(5));

            if (values is not null && values.TryGetValue(token, out var value))
                return value ?? "";

            return leaveUnknown ? match.Value : "";
        });
    }

    /// <summary>
    /// Собирает полную командную строку сценария: путь к файлу (в кавычках, если
    /// содержит пробелы) + резолвнутые параметры. Строка передаётся системному shell
    /// через <see cref="ExternalCommandRunner.BuildShellCommand"/>.
    /// </summary>
    public static string BuildCommandLine(
        ScriptScenario scenario,
        IReadOnlyDictionary<string, string>? values,
        DateTime? now = null)
    {
        if (scenario is null)
            return "";

        var path = (scenario.FilePath ?? "").Trim();
        var parts = new List<string>();
        if (path.Length > 0)
            parts.Add(QuoteIfNeeded(path));

        foreach (var parameter in scenario.Parameters ?? new List<string>())
        {
            var resolved = Resolve(parameter, values, now).Trim();
            if (resolved.Length > 0)
                parts.Add(resolved);
        }

        return string.Join(" ", parts);
    }

    /// <summary>Пытается преобразовать строку в число параметров; некорректное — 0.</summary>
    private static string SafeFormat(DateTime now, string format)
    {
        try
        {
            return now.ToString(format);
        }
        catch (FormatException)
        {
            // Некорректный формат пользователя не должен ронять подстановку.
            return now.ToString(DefaultDateFormat);
        }
    }

    /// <summary>Заключает значение в кавычки, если оно содержит пробелы и ещё не в кавычках.</summary>
    private static string QuoteIfNeeded(string value)
        => value.Contains(' ') && !value.StartsWith("\"") ? "\"" + value + "\"" : value;
}