using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Чистое форматирование/усечение записей диагностики <c>trace.json</c>
/// (issue #340, девятая попытка). Без файлового I/O и платформенных зависимостей —
/// вынесено из <see cref="MenuCloseTrace"/>, чтобы покрыть юнит-тестами: построение
/// одной JSONL-строки (валидный JSON, ключи латиницей), круговое усечение при
/// превышении лимита и маскирование чувствительных полей (пароли/токены).
/// </summary>
internal static class MenuCloseTraceFormat
{
    /// <summary>
    /// Максимальный размер файла трассировки до кругового усечения (~1 МБ,
    /// план 0.3.9.311, B-7: лимит увеличен с 512 КБ плана 0.3.9.306, 2.3.1 —
    /// трассировка стала плотнее за счёт безусловных записей кликов и активаций).
    /// При превышении остаётся хвост последних записей и дописывается
    /// строка-маркер <c>{"event":"truncated","ts":...}</c>.
    /// </summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>
    /// Основное имя файла трассировки (соглашение с пользователем, issue #340, 0.3.9.308):
    /// <c>trace.json</c> РЯДОМ с настройками приложения (тот же каталог, что settings.json).
    /// Содержимое — JSON Lines (одна JSON-запись на строку); расширение .json по просьбе
    /// пользователя (семантика «файл-флаг», которого нет в механизме env-переменной).
    /// </summary>
    public const string PrimaryFileName = "trace.json";

    /// <summary>
    /// Прежнее имя файла трассировки версии 0.3.9.306 (<c>menuclose_trace.json</c>).
    /// Если такой файл уже существует рядом с настройками — журнал продолжает
    /// дописываться в него (непрерывность диагностики), иначе используется
    /// <see cref="PrimaryFileName"/>.
    /// </summary>
    public const string LegacyFileName = "menuclose_trace.json";

    /// <summary>
    /// Выбор имени файла трассировки (issue #340, 0.3.9.308): при наличии legacy-файла
    /// <c>menuclose_trace.json</c> от 0.3.9.306 журнал дописывается в него (непрерывность
    /// диагностики), иначе — основной <c>trace.json</c>. Чистая функция выбора пути
    /// для юнит-тестов (сам путь строит <see cref="MenuCloseTrace"/> через
    /// <see cref="PlatformPaths.AppDataDirectory"/>).
    /// </summary>
    public static string ResolveFileName(bool legacyExists)
        => legacyExists ? LegacyFileName : PrimaryFileName;

    /// <summary>
    /// Маркеры имён полей, значения которых НЕ выводятся в диагностику
    /// (см. <see cref="MaskSensitive"/>). Короткие слова ("user", "login", "pwd")
    /// сравниваются ТОЛЬКО целиком, чтобы не маскировать диагностические ключи
    /// вроде "userReselected".
    /// </summary>
    private static readonly string[] SensitiveExactKeys =
    {
        "user", "login", "pwd"
    };

    private static readonly string[] SensitiveContainedMarkers =
    {
        "password", "passwd", "username", "token", "secret",
        "authorization", "apikey", "api_key"
    };

    /// <summary>
    /// Собирает одну JSONL-запись вида
    /// <c>{"ts":"2026-10-04T20:30:00.000+03:00","thread":12,"event":"log","data":{...}}</c>.
    /// Без символов перевода строки — каждая строка файла является валидным JSON.
    /// </summary>
    /// <param name="eventName">Имя события (латиницей).</param>
    /// <param name="ts">Метка времени записи.</param>
    /// <param name="threadId">Идентификатор потока (<see cref="Environment.CurrentManagedThreadId"/>).</param>
    /// <param name="data">Произвольные данные записи (значения уже очищены от секретов
    /// через <see cref="MaskSensitive"/> по мере необходимости).</param>
    public static string BuildLine(
        string eventName,
        DateTimeOffset ts,
        int threadId,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        var sb = new StringBuilder(192);
        sb.Append("{\"ts\":").Append(JsonEscape(ts.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture)));
        sb.Append(",\"thread\":").Append(threadId.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"event\":").Append(JsonEscape(eventName));
        if (data is not null && data.Count > 0)
        {
            sb.Append(",\"data\":{");
            var first = true;
            foreach (var pair in data)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append(JsonEscape(pair.Key)).Append(':').Append(JsonValue(pair.Value));
            }
            sb.Append('}');
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Startup-запись при первом обращении к трассировке (план 0.3.9.306, 2.3.1):
    /// версия приложения (InformationalVersion), платформа (WPF/Avalonia), ОС и
    /// каталог данных приложения — чтобы по логу всегда было ясно, какая сборка.
    /// </summary>
    public static string BuildStartupLine(
        string appVersion,
        string platform,
        string os,
        string appDataDirectory,
        DateTimeOffset ts,
        int threadId)
    {
        return BuildLine("startup", ts, threadId, new Dictionary<string, object?>
        {
            ["version"] = appVersion,
            ["platform"] = platform,
            ["os"] = os,
            ["appDataDir"] = appDataDirectory
        });
    }

    /// <summary>
    /// Круговое усечение содержимого файла трассировки (JSONL): если объём превышает
    /// <paramref name="maxBytes"/>, остаётся ХВОСТ последних записей (в рамках лимита),
    /// а первой строкой дописывается маркер <c>{"event":"truncated","ts":...}</c>.
    /// Возвращает готовое к записи содержимое (маркер + хвост). Если в лимит не
    /// помещается ни одна строка — только маркер. Если содержимое уже в пределах
    /// лимита — возвращается БЕЗ изменений (без маркера). Чистая функция для юнит-тестов.
    /// </summary>
    public static string TruncateToLimit(string content, long maxBytes, DateTimeOffset ts)
    {
        if (string.IsNullOrEmpty(content) || maxBytes <= 0)
            return string.Empty;
        // Уже в пределах лимита — маркер не нужен.
        if (System.Text.Encoding.UTF8.GetByteCount(content) <= maxBytes)
            return content;

        var markerLine = BuildLine("truncated", ts, 0);
        var budget = maxBytes - markerLine.Length - 1; // маркер + перевод строки
        if (budget <= 0)
            return markerLine + "\n";

        var lines = SplitLines(content);
        var tail = new List<string>();
        long used = 0;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            var add = line.Length + 1; // строка + перевод строки
            if (used + add > budget)
                break;
            tail.Add(line);
            used += add;
        }
        tail.Reverse();

        var sb = new StringBuilder((int)(markerLine.Length + used + 1));
        sb.Append(markerLine).Append('\n');
        foreach (var line in tail)
            sb.Append(line).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Маскирует значения полей с чувствительными именами (пароль, логин, токен,
    /// secret и т.п.) перед записью в диагностику: такие поля заменяются на
    /// <c>"***"</c>. Имена полей сохраняются (видно, что поле было), значения — нет.
    /// Диагностические ключи вроде "message", "selectedItemId", "userReselected"
    /// не затрагиваются.
    /// </summary>
    public static Dictionary<string, object?> MaskSensitive(IEnumerable<KeyValuePair<string, object?>> source)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (source is null)
            return result;
        foreach (var pair in source)
        {
            result[pair.Key] = IsSensitiveKey(pair.Key) ? "***" : pair.Value;
        }
        return result;
    }

    /// <summary>
    /// Экранирует строку для помещения в JSON-строку (без перевода строк и
    /// управляющих символов внутри; кириллица сохраняется как есть).
    /// </summary>
    public static string JsonEscape(string? value)
    {
        if (value is null)
            return "null";
        var sb = new StringBuilder(value.Length + 8);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        var lower = key.ToLowerInvariant();
        foreach (var marker in SensitiveExactKeys)
        {
            if (string.Equals(lower, marker, StringComparison.Ordinal))
                return true;
        }
        foreach (var marker in SensitiveContainedMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string JsonValue(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        byte i => i.ToString(CultureInfo.InvariantCulture),
        sbyte i => i.ToString(CultureInfo.InvariantCulture),
        short i => i.ToString(CultureInfo.InvariantCulture),
        ushort i => i.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        uint i => i.ToString(CultureInfo.InvariantCulture),
        long i => i.ToString(CultureInfo.InvariantCulture),
        ulong i => i.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        string s => JsonEscape(s),
        _ => JsonEscape(value.ToString())
    };

    /// <summary>Разбивает JSONL-содержимое на строки без завершающей пустой (после финального '\n').</summary>
    private static List<string> SplitLines(string content)
    {
        var result = new List<string>();
        int start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '\n')
                continue;
            if (i > start)
                result.Add(content.Substring(start, i - start).TrimEnd('\r'));
            else
                result.Add(string.Empty);
            start = i + 1;
        }
        if (start < content.Length)
            result.Add(content.Substring(start).TrimEnd('\r'));
        return result;
    }
}