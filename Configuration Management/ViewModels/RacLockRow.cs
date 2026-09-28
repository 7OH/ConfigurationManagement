using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка вкладки «Блокировки» окна «Серверы 1С» (0.3.9.124): форматирование
/// <see cref="RacLockInfo"/> для отображения — признаки ожидания/блокирования
/// с цветами, идентификаторы сеанса/соединения/транзакции. Чистый .NET —
/// без платформенных зависимостей.
/// </summary>
public sealed class RacLockRow
{
    private readonly RacLockInfo _info;

    public RacLockRow(RacLockInfo info)
    {
        _info = info ?? throw new System.ArgumentNullException(nameof(info));
    }

    /// <summary>Идентификатор блокировки.</summary>
    public System.Guid Id => _info.Id;

    /// <summary>Идентификатор сеанса, удерживающего блокировку.</summary>
    public System.Guid SessionId => _info.SessionId;

    /// <summary>Идентификатор информационной базы.</summary>
    public System.Guid InfobaseId => _info.InfobaseId;

    /// <summary>Идентификатор соединения, удерживающего блокировку.</summary>
    public System.Guid ConnectionId => _info.ConnectionId;

    /// <summary>Идентификатор транзакции.</summary>
    public System.Guid TransactionId => _info.TransactionId;

    /// <summary>Ожидает ли блокировку другой сеанс.</summary>
    public bool Waiting => _info.Waiting;

    /// <summary>Блокирует ли объект другой сеанс.</summary>
    public bool Blocking => _info.Blocking;

    /// <summary>«●» если блокировка ожидается, иначе пусто.</summary>
    public string WaitingSymbol => _info.Waiting ? "●" : string.Empty;

    /// <summary>«●» если блокировка активна, иначе пусто.</summary>
    public string BlockingSymbol => _info.Blocking ? "●" : string.Empty;

    /// <summary>Описание объекта блокировки.</summary>
    public string Object => _info.Object;

    /// <summary>
    /// Цвет состояния: красный — активная блокировка, жёлтый — ожидание,
    /// нейтральный — обычная блокировка.
    /// </summary>
    public string StateColorHex => _info.Blocking
        ? "#DC2626"
        : _info.Waiting ? "#D97706" : "#64748B";

    /// <summary>Подсказка по типам блокировок.</summary>
    public string KindText
    {
        get
        {
            if (_info.Blocking)
                return LocalizationManager.T("ServerMonitor.Lock.Kind.Blocking");
            if (_info.Waiting)
                return LocalizationManager.T("ServerMonitor.Lock.Kind.Waiting");
            return LocalizationManager.T("ServerMonitor.Lock.Kind.Held");
        }
    }
}