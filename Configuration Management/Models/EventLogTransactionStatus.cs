namespace Configuration_Management.Models;

/// <summary>
/// Статус транзакции события журнала регистрации.
/// В последовательном формате (.lgf/.lgp) кодируется буквами N/U/R/C,
/// в SQLite (.lgd) — числами 0..3 (см. комментарии к значениям).
/// </summary>
public enum EventLogTransactionStatus
{
    /// <summary>Нет транзакции (N в последовательном формате).</summary>
    None = 0,

    /// <summary>Транзакция начата.</summary>
    Started = 1,

    /// <summary>Транзакция зафиксирована.</summary>
    Committed = 2,

    /// <summary>Транзакция отменена.</summary>
    RolledBack = 3,
}