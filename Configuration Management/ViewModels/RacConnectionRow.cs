using System.Globalization;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка вкладки «Соединения» окна «Серверы 1С» (0.3.9.124): форматирование
/// <see cref="RacConnectionInfo"/> для отображения — время установления/последнего
/// обращения локально, длительность «HH:mm:ss», признак блокировки с цветом.
/// Чистый .NET — без платформенных зависимостей.
/// </summary>
public sealed class RacConnectionRow
{
    private readonly RacConnectionInfo _info;

    public RacConnectionRow(RacConnectionInfo info)
    {
        _info = info ?? throw new System.ArgumentNullException(nameof(info));
    }

    /// <summary>Идентификатор соединения.</summary>
    public System.Guid Id => _info.Id;

    /// <summary>Идентификатор сеанса соединения.</summary>
    public System.Guid SessionId => _info.SessionId;

    /// <summary>Признак блокировки соединения.</summary>
    public bool Blocked => _info.Blocked;

    /// <summary>«Да»/«Нет» для колонки «Заблокировано».</summary>
    public string BlockedText => _info.Blocked
        ? LocalizationManager.T("Common.Yes")
        : LocalizationManager.T("Common.No");

    /// <summary>Тип коннектора (например «1CV8»).</summary>
    public string Connector => _info.Connector;

    /// <summary>Идентификатор рабочего процесса соединения.</summary>
    public System.Guid ProcessId => _info.ProcessId;

    /// <summary>Имя компьютера клиента.</summary>
    public string Host => _info.Host;

    /// <summary>Порт клиента.</summary>
    public int Port => _info.Port;

    /// <summary>Время установления соединения (локальное), «—» если не задано.</summary>
    public string EstablishedAtText => _info.EstablishedAt == default
        ? "—"
        : _info.EstablishedAt.ToString("dd.MM.yyyy HH:mm:ss");

    /// <summary>Время последнего обращения (локальное), «—» если не задано.</summary>
    public string LastConnectionTimeText => _info.LastConnectionTime == default
        ? "—"
        : _info.LastConnectionTime.ToString("dd.MM.yyyy HH:mm:ss");

    /// <summary>Длительность соединения (HH:mm:ss).</summary>
    public string DurationText =>
        TimeSpan.FromMilliseconds(_info.Duration).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>Описание соединения (descr из нового формата вывода rac, issue #324).</summary>
    public string Descr => _info.Descr;

    /// <summary>Цвет признака блокировки: жёлтый — заблокировано, нейтральный — нет.</summary>
    public string BlockedColorHex => _info.Blocked ? "#D97706" : "#64748B";
}