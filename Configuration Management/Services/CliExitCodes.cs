namespace Configuration_Management.Services;

/// <summary>
/// Единые коды возврата CLI (функция 10): 0 — успех, 1 — ошибка, 2 — не найдено,
/// 3 — частичный успех. Используются всеми командами (в т.ч. мигрировавшими --run/--list).
/// </summary>
public static class CliExitCodes
{
    /// <summary>Успех (в т.ч. идемпотентное «уже существует» для --add).</summary>
    public const int Success = 0;

    /// <summary>Ошибка: невалидные аргументы, ошибка запуска/бэкапа, io, приватная база, внутренняя.</summary>
    public const int Error = 1;

    /// <summary>Не найдено: база/сценарий/задание/профиль.</summary>
    public const int NotFound = 2;

    /// <summary>Частичный успех (--backup-all: часть баз успешна, часть нет).</summary>
    public const int PartialSuccess = 3;
}