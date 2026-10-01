namespace Configuration_Management.Models;

/// <summary>
/// Машиночитаемая ошибка команды CLI (единый контракт вывода, функция 10).
/// </summary>
public sealed class CliError
{
    /// <summary>
    /// Стабильный машинный код ошибки — не локализуется и является частью контракта
    /// для скриптов (например <c>base_not_found</c>, <c>private_base</c>, <c>invalid_args</c>).
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Человекочитаемое сообщение об ошибке (локаль сессии).</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Результат команды CLI: <c>ok</c>/<c>error</c> + данные. При <c>--json</c> сериализуется
/// в stdout целиком (структура фиксирована и не зависит от локализации).
/// </summary>
public sealed class CliResult
{
    /// <summary>Успешно ли выполнена команда.</summary>
    public bool Ok { get; set; }

    /// <summary>Имя команды (list/add/backup/status/export/run).</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Данные команды (объект/массив) или null при ошибке.</summary>
    public object? Data { get; set; }

    /// <summary>Ошибка (null при успехе).</summary>
    public CliError? Error { get; set; }

    /// <summary>Результат успеха: <see cref="Ok"/> = true.</summary>
    public static CliResult Success(string command, object? data = null) =>
        new() { Ok = true, Command = command, Data = data };

    /// <summary>Результат ошибки: <see cref="Ok"/> = false с машиночитаемым кодом.</summary>
    public static CliResult Failure(string command, string code, string message) =>
        new() { Ok = false, Command = command, Error = new CliError { Code = code, Message = message } };
}