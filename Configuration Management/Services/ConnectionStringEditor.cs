using System.Globalization;
using System.Text.RegularExpressions;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистый редактор строки подключения (0.3.9.187, функция 6 «Массовая замена в строке
/// подключения баз»): единая точка разбора/сборки через <see cref="ConnectionSettings"/>
/// и применение правила замены к одному полю. Не имеет платформенных зависимостей,
/// используется обеими платформами (Windows/WPF и Linux/Avalonia).
/// </summary>
public static class ConnectionStringEditor
{
    /// <summary>
    /// Разбирает строку подключения 1С любого типа (File/WS/Srvr+Ref) в структурные
    /// настройки. Тонкая обёртка над <see cref="ConnectionSettings.ParseConnectionString"/> —
    /// единая точка нормализации; редактор не дублирует логику разбора.
    /// </summary>
    /// <param name="connectionString">Строка подключения 1С (может быть пустой или null).</param>
    public static ConnectionSettings Parse(string? connectionString)
        => ConnectionSettings.ParseConnectionString(connectionString);

    /// <summary>
    /// Собирает каноническую строку подключения из настроек. Обёртка над
    /// <see cref="ConnectionSettings.ToConnectionString"/>: порт 1541 опускается,
    /// нестандартный порт сохраняется, IPv6 — в квадратных скобках, кавычки в значениях
    /// удваиваются.
    /// </summary>
    /// <param name="settings">Настройки подключения.</param>
    public static string Build(ConnectionSettings settings)
        => settings.ToConnectionString();

    /// <summary>
    /// Применяет правило замены к одному полю настроек, не мутируя входные
    /// <paramref name="settings"/>: <paramref name="updated"/> — копия с изменённым полем.
    /// Возвращает true, если значение поля реально изменилось; через
    /// <paramref name="beforeValue"/>/<paramref name="afterValue"/> отдаёт старое и новое
    /// значение поля (для предпросмотра «было → станет»).
    /// Возвращает false без изменений, если: Find пустой, база без подключения, поле не
    /// соответствует типу базы (например FilePath у серверной базы), совпадение не найдено
    /// или результат равен исходному значению.
    /// </summary>
    /// <param name="settings">Исходные настройки подключения (не мутируются).</param>
    /// <param name="rule">Правило замены.</param>
    /// <param name="updated">Копия настроек с применённой заменой (валидна только при true).</param>
    /// <param name="beforeValue">Текущее значение поля (или вся строка для <see cref="ConnectionField.Any"/>).</param>
    /// <param name="afterValue">Новое значение поля после замены.</param>
    /// <returns>true — замена применена; false — правило не сработало.</returns>
    /// <exception cref="ArgumentException">Режим <see cref="ConnectionMatchMode.Regex"/> и паттерн невалиден (параметр <paramref name="rule"/>).</exception>
    public static bool TryApply(
        ConnectionSettings settings,
        ConnectionStringReplaceRule rule,
        out ConnectionSettings updated,
        out string beforeValue,
        out string afterValue)
    {
        updated = new ConnectionSettings();
        beforeValue = string.Empty;
        afterValue = string.Empty;

        if (settings is null || rule is null || string.IsNullOrEmpty(rule.Find))
            return false;

        // База без подключения (все поля подключения пусты) — замена не применяется.
        if (IsConnectionless(settings))
            return false;

        return rule.Field switch
        {
            ConnectionField.Any => TryApplyAny(settings, rule, out updated, out beforeValue, out afterValue),
            ConnectionField.Port => TryApplyPort(settings, rule, out updated, out beforeValue, out afterValue),
            ConnectionField.Server or ConnectionField.Ref or ConnectionField.FilePath or ConnectionField.WebUrl
                => TryApplyText(settings, rule, out updated, out beforeValue, out afterValue),
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };
    }

    /// <summary>
    /// Применяет правило к произвольному тексту строки подключения как целому
    /// (предназначено для <see cref="ConnectionField.Any"/>): подстрока/префикс/regex по
    /// всему тексту без разбора на поля. Сегментная замена значения сохраняет кавычки,
    /// экранирование и прочие параметры (Usr/Pwd/SchJobDn/disstt и неизвестные) как есть.
    /// Возвращает false, если Find пустой, текст пуст или совпадения не найдены.
    /// </summary>
    /// <param name="rawConnectionString">Сырой текст строки подключения.</param>
    /// <param name="rule">Правило замены (используются Find/Replace/Mode/IgnoreCase).</param>
    /// <param name="updated">Текст после замены (валиден только при true).</param>
    /// <returns>true — текст изменён; false — правило не сработало.</returns>
    /// <exception cref="ArgumentException">Режим <see cref="ConnectionMatchMode.Regex"/> и паттерн невалиден (параметр <paramref name="rule"/>).</exception>
    public static bool TryApplyRaw(
        string rawConnectionString,
        ConnectionStringReplaceRule rule,
        out string updated)
    {
        updated = string.Empty;

        if (rule is null || string.IsNullOrEmpty(rule.Find))
            return false;

        var raw = rawConnectionString ?? string.Empty;
        if (raw.Length == 0)
            return false;

        var replaced = ApplyToText(raw, rule);
        if (string.Equals(replaced, raw, StringComparison.Ordinal))
            return false;

        updated = replaced;
        return true;
    }

    // ---------- Применение по полям ----------

    /// <summary>
    /// Текстовые поля (Server/Ref/FilePath/WebUrl): поиск по значению поля, замена всех
    /// вхождений, результат пишется в копию настроек.
    /// </summary>
    private static bool TryApplyText(
        ConnectionSettings settings,
        ConnectionStringReplaceRule rule,
        out ConnectionSettings updated,
        out string beforeValue,
        out string afterValue)
    {
        updated = new ConnectionSettings();
        beforeValue = string.Empty;
        afterValue = string.Empty;

        // Поле не соответствует типу базы (например FilePath у серверной базы).
        if (!IsFieldApplicable(settings.Type, rule.Field))
            return false;

        var value = GetFieldValue(settings, rule.Field);
        if (string.IsNullOrEmpty(value))
            return false;

        var replaced = ApplyToText(value, rule);
        if (string.Equals(replaced, value, StringComparison.Ordinal))
            return false;

        beforeValue = value;
        afterValue = replaced;
        updated = Clone(settings);
        SetFieldValue(updated, rule.Field, replaced);
        return true;
    }

    /// <summary>
    /// Поле Port: работаем с числовым значением <see cref="ConnectionSettings.Port"/>.
    /// Find трактуется как строка десятичного порта («1541»); после сопоставления и замены
    /// результат разбирается обратно в число. Нестандартный порт при сборке строки
    /// добавляется как «host:port», стандартный 1541 опускается (<see cref="ConnectionSettings.GetServerWithPort"/>).
    /// </summary>
    private static bool TryApplyPort(
        ConnectionSettings settings,
        ConnectionStringReplaceRule rule,
        out ConnectionSettings updated,
        out string beforeValue,
        out string afterValue)
    {
        updated = new ConnectionSettings();
        beforeValue = string.Empty;
        afterValue = string.Empty;

        // Порт существует только у клиент-серверного подключения.
        if (settings.Type != ConnectionType.ClientServer)
            return false;

        var value = settings.Port.ToString(CultureInfo.InvariantCulture);
        var replaced = ApplyToText(value, rule);
        if (string.Equals(replaced, value, StringComparison.Ordinal))
            return false;

        if (!int.TryParse(replaced, NumberStyles.None, CultureInfo.InvariantCulture, out var newPort)
            || newPort < 1 || newPort > 65535
            || newPort == settings.Port)
            return false;

        beforeValue = value;
        afterValue = newPort.ToString(CultureInfo.InvariantCulture);
        updated = Clone(settings);
        updated.Port = newPort;
        return true;
    }

    /// <summary>
    /// Поле Any: сырая каноническая строка (<see cref="ConnectionSettings.ToConnectionString"/>)
    /// как текст; при совпадении результат разбирается обратно через <see cref="Parse"/>
    /// в новые настройки.
    /// </summary>
    private static bool TryApplyAny(
        ConnectionSettings settings,
        ConnectionStringReplaceRule rule,
        out ConnectionSettings updated,
        out string beforeValue,
        out string afterValue)
    {
        updated = new ConnectionSettings();
        beforeValue = string.Empty;
        afterValue = string.Empty;

        var raw = settings.ToConnectionString();
        if (string.IsNullOrEmpty(raw))
            return false;

        var replaced = ApplyToText(raw, rule);
        if (string.Equals(replaced, raw, StringComparison.Ordinal))
            return false;

        beforeValue = raw;
        updated = Parse(replaced);
        afterValue = updated.ToConnectionString();
        return true;
    }

    // ---------- Сопоставление и замена текста ----------

    /// <summary>
    /// Применяет правило к тексту значения: строит паттерн по режиму сопоставления и
    /// заменяет все вхождения. Для Exact/Prefix/Substring искомый текст экранируется
    /// (<see cref="Regex.Escape"/>) — буквальное сопоставление (точка в «srv1.test» не
    /// съедает лишние символы); для Regex — паттерн используется как есть. Замена всегда
    /// литеральная (без интерпретации $-ссылок).
    /// </summary>
    private static string ApplyToText(string value, ConnectionStringReplaceRule rule)
    {
        var options = rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        try
        {
            var regex = new Regex(BuildPattern(rule), options);
            return regex.Replace(value, _ => rule.Replace ?? string.Empty);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Некорректное регулярное выражение: {ex.Message}", nameof(rule));
        }
    }

    /// <summary>Строит паттерн поиска по режиму сопоставления.</summary>
    private static string BuildPattern(ConnectionStringReplaceRule rule) => rule.Mode switch
    {
        ConnectionMatchMode.Exact => $"^{Regex.Escape(rule.Find)}$",
        ConnectionMatchMode.Prefix => $"^{Regex.Escape(rule.Find)}",
        ConnectionMatchMode.Substring => Regex.Escape(rule.Find),
        ConnectionMatchMode.Regex => rule.Find,
        _ => throw new ArgumentOutOfRangeException(nameof(rule))
    };

    // ---------- Вспомогательные ----------

    /// <summary>Значение поля как текст (для Server/Ref/FilePath/WebUrl).</summary>
    private static string GetFieldValue(ConnectionSettings settings, ConnectionField field) => field switch
    {
        ConnectionField.Server => settings.Server ?? string.Empty,
        ConnectionField.Ref => settings.DatabaseName ?? string.Empty,
        ConnectionField.FilePath => settings.FilePath ?? string.Empty,
        ConnectionField.WebUrl => settings.WebUrl ?? string.Empty,
        _ => string.Empty
    };

    /// <summary>Пишет результат замены в целевое поле копии настроек.</summary>
    private static void SetFieldValue(ConnectionSettings settings, ConnectionField field, string value)
    {
        switch (field)
        {
            case ConnectionField.Server:
                settings.Server = value;
                break;
            case ConnectionField.Ref:
                settings.DatabaseName = value;
                break;
            case ConnectionField.FilePath:
                settings.FilePath = value;
                break;
            case ConnectionField.WebUrl:
                settings.WebUrl = value;
                break;
        }
    }

    /// <summary>
    /// Поле применимо к типу подключения: Srvr/Ref/порт — только клиент-серверные,
    /// File — файловые, WS — веб-публикация; Any применим всегда.
    /// </summary>
    private static bool IsFieldApplicable(ConnectionType type, ConnectionField field) => field switch
    {
        ConnectionField.Server or ConnectionField.Port or ConnectionField.Ref
            => type == ConnectionType.ClientServer,
        ConnectionField.FilePath => type == ConnectionType.File,
        ConnectionField.WebUrl => type == ConnectionType.WebServer,
        _ => true
    };

    /// <summary>
    /// База «без подключения»: ни одно из полей подключения не заполнено
    /// (дефолтные настройки, пустая строка подключения).
    /// </summary>
    private static bool IsConnectionless(ConnectionSettings settings)
        => string.IsNullOrWhiteSpace(settings.Server)
            && string.IsNullOrWhiteSpace(settings.DatabaseName)
            && string.IsNullOrWhiteSpace(settings.FilePath)
            && string.IsNullOrWhiteSpace(settings.WebUrl);

    /// <summary>Независимая копия настроек (все поля, кроме производного UseOsAuthentication).</summary>
    private static ConnectionSettings Clone(ConnectionSettings source) => new()
    {
        Type = source.Type,
        Server = source.Server,
        DatabaseName = source.DatabaseName,
        FilePath = source.FilePath,
        BlockScheduledJobs = source.BlockScheduledJobs,
        ForbidSpeechRecognition = source.ForbidSpeechRecognition,
        User = source.User,
        Password = source.Password,
        AuthenticationMode = source.AuthenticationMode,
        Port = source.Port,
        WebUrl = source.WebUrl
    };
}