using System.Threading.Tasks;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Результат операции установки/снятия блокировки сеансов ИБ.
/// </summary>
public class SessionLockResult
{
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public static SessionLockResult Ok() => new() { Success = true };

    public static SessionLockResult Fail(string message) => new() { ErrorMessage = message };
}

/// <summary>
/// Сервис установки/снятия блокировки сеансов информационной базы (функция №20
/// StartManager). Не требует открытия «1С:Предприятия»: блокировка выполняется
/// пакетным запуском конфигуратора (<see cref="OneCLauncher.RunDesignerBatch"/>
/// с операциями <c>LockIB</c>/<c>UnlockIB</c>) без интерактивного окна.
/// </summary>
public interface ISessionLockService
{
    /// <summary>Устанавливает блокировку сеансов ИБ на указанный период.</summary>
    Task<SessionLockResult> LockAsync(Infobase infobase, SessionLockOptions options);

    /// <summary>Снимает ранее установленную блокировку сеансов ИБ.</summary>
    Task<SessionLockResult> UnlockAsync(Infobase infobase);
}