using System;
using System.Text;
using System.Text.Json;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Вывод результата CLI: stdout — результат команды (при <c>--json</c> — единый контракт),
/// stderr — человеческая диагностика. Машиночитаемый JSON не зависит от локализации
/// (структура и error.code фиксированы, функция 10).
/// </summary>
public static class CliOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Единый контракт вывода: имена свойств в lowerCamelCase (ok/command/data/error).
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
        // а не \uXXXX-последовательностями (как InfobaseJsonTransfer.Serialize).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Выставляет UTF-8 для вывода; при перенаправлении может быть недоступно — не критично.</summary>
    public static void EnsureUtf8()
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { /* вывод может быть перенаправлен — не критично */ }
    }

    /// <summary>
    /// Печатает результат команды: при json=true — единый JSON-контракт в stdout;
    /// иначе диагностику ошибки в stderr (plain-текст команд печатается самими командами).
    /// </summary>
    public static void WriteResult(CliResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(SerializeJson(result));
            return;
        }

        if (result.Error is not null)
            Console.Error.WriteLine("[cli] " + result.Error.Message);
    }

    /// <summary>Диагностическое сообщение команды в stderr (не является контрактом для скриптов).</summary>
    public static void WriteDiagnostic(string message) =>
        Console.Error.WriteLine("[cli] " + message);

    /// <summary>Сериализация результата в единый JSON-контракт (internal — для юнит-тестов).</summary>
    internal static string SerializeJson(CliResult result) =>
        JsonSerializer.Serialize(result, JsonOptions);
}