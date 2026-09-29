using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая подстановка параметров сценариев запуска скриптов (issue #308).
/// <para>
/// Поддерживаются токены вида <c>%ключ%</c>: имя базы (<c>%name%</c>), свойства
/// подключения через точку (<c>%connection.server%</c>, <c>%connection.database%</c>,
/// <c>%connection.filePath%</c>, <c>%connection.password%</c> и т.д.), пароль —
/// также плоский ключ <c>%password%</c>, и текущая дата — <c>%date%</c> (по умолчанию
/// <c>yyyy-MM-dd</c>) либо <c>%date:Формат%</c> (формат .NET, например
/// <c>%date:yyyyMMdd_HHmm%</c>). Неизвестный ключ по умолчанию остаётся в строке
/// как есть; при <c>leaveUnknown=false</c> заменяется пустой строкой.
/// </para>
/// <para>
/// Карта значений собирается из явных ключей плюс динамического прохода рефлексией
/// по публичным свойствам <see cref="Infobase"/> и <see cref="ConnectionSettings"/>
/// (ключи <c>connection.<имя></c> и плоские <c><имя></c> в нижнем регистре):
/// будущие свойства моделей подхватываются автоматически. Явные ключи перезаписывают
/// динамические, поведение неизвестного ключа (leaveUnknown=true) не меняется.
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

    // Кэшированный список публичных свойств для рефлексии (не читать GetProperties
    // на каждый вызов; значения скалярных типов — строки и значимые типы).
    private static readonly IReadOnlyList<PropertyInfo> InfobaseSubstitutionProperties =
        typeof(Infobase).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(IsScalarProperty)
            .ToArray();

    private static readonly IReadOnlyList<PropertyInfo> ConnectionSubstitutionProperties =
        typeof(ConnectionSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(IsScalarProperty)
            .ToArray();

    /// <summary>Отбирает свойства со скалярными значениями (строки и значимые типы),
    /// исключая ссылочные объекты (Connection, Repository, списки и т.д.).</summary>
    private static bool IsScalarProperty(PropertyInfo property)
    {
        var type = property.PropertyType;
        return type == typeof(string)
            || type.IsEnum
            || type.IsValueType;
    }

    /// <summary>
    /// Собирает словарь значений подстановок для информационной базы: плоские ключи
    /// (<c>name</c>, <c>password</c>) и свойства подключения с префиксом <c>connection.</c>.
    /// Публичные свойства <see cref="Infobase"/> и <see cref="ConnectionSettings"/>
    /// попадают в карту динамически (в нижнем регистре), явные ключи — поверх.
    /// </summary>
    public static Dictionary<string, string> BuildValueMap(Infobase? infobase)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (infobase is null)
            return map;

        // Динамика по Infobase: плоские ключи <имя> для всех публичных скалярных свойств.
        foreach (var prop in InfobaseSubstitutionProperties)
        {
            var value = ReadPropertyValue(infobase, prop);
            if (value is not null)
                map[prop.Name.ToLowerInvariant()] = value;
        }

        var conn = infobase.Connection;
        if (conn is null)
            return map;

        // Динамика по ConnectionSettings: плоские ключи <имя> и connection.<имя>.
        foreach (var prop in ConnectionSubstitutionProperties)
        {
            var value = ReadPropertyValue(conn, prop);
            if (value is null)
                continue;
            var key = prop.Name.ToLowerInvariant();
            map[key] = value;
            map["connection." + key] = value;
        }

        // Явные ключи: каноничные имена токенов, перезаписывают динамические.
        map["name"] = infobase.Name ?? "";
        map["connection.type"] = conn.Type.ToString();
        map["connection.server"] = conn.Server ?? "";
        map["connection.serverPort"] = conn.GetServerWithPort();
        map["connection.database"] = conn.DatabaseName ?? "";
        map["connection.filePath"] = conn.FilePath ?? "";
        map["connection.webUrl"] = conn.WebUrl ?? "";
        map["connection.port"] = conn.Port.ToString();
        map["connection.connectionString"] = conn.ToConnectionString();
        map["connection.user"] = conn.User ?? "";
        map["connection.password"] = conn.Password ?? "";
        map["password"] = conn.Password ?? "";
        map["connection.blockScheduledJobs"] = conn.BlockScheduledJobs.ToString();
        map["connection.forbidSpeechRecognition"] = conn.ForbidSpeechRecognition.ToString();
        map["connection.authenticationMode"] = conn.AuthenticationMode.ToString();
        map["connection.useOsAuthentication"] = conn.UseOsAuthentication.ToString();
        return map;
    }

    /// <summary>
    /// Читает значение свойства как строку (инвариантная культура); при ошибке
    /// доступа пропускает ключ — не роняет построение карты.
    /// </summary>
    private static string? ReadPropertyValue(object target, PropertyInfo property)
    {
        try
        {
            var value = property.GetValue(target);
            if (value is null)
                return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            // Значение свойства недоступно — ключ просто не попадёт в карту.
            return null;
        }
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
    /// Собирает полную командную строку сценария: рабочая папка запуска (если задана,
    /// префикс <c>cd "…" &&</c>), путь к файлу (в кавычках, если содержит пробелы)
    /// и резолвнутые параметры. Строка передаётся системному shell через
    /// <see cref="ExternalCommandRunner.BuildShellCommand"/>.
    /// <para>
    /// Реальный запуск использует <see cref="System.Diagnostics.ProcessStartInfo.WorkingDirectory"/>
    /// (без <c>cd</c>); префикс <c>cd</c> нужен только для наглядного превью в окнах
    /// редактирования и выбора (issue #308, п.7). При пустой рабочей папке строка
    /// не меняется — старые сценарии мигрируют без изменения поведения.
    /// </para>
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

        // Папка запуска (issue #308, п.7): видна в превью как «cd "…" && …».
        var workingDirectory = (scenario.WorkingDirectory ?? "").Trim();
        if (workingDirectory.Length > 0)
        {
            parts.Add("cd " + QuoteIfNeeded(workingDirectory));
            parts.Add("&&");
        }

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