namespace Configuration_Management.Services;

/// <summary>
/// Интеграция с проводником Windows (функция №12 дорожной карты StartManager):
/// регистрация ассоциации <c>.1CD</c> и команд контекстного меню
/// «Зарегистрировать в списке баз» / «Запустить 1С:Предприятие» /
/// «Запустить Конфигуратор».
/// <para>
/// Ассоциация и команды пишутся в раздел реестра <c>HKCU\Software\Classes\</c>,
/// поэтому работают для текущего пользователя без прав администратора.
/// </para>
/// <para>
/// Сервис Windows-специфичен. На Linux/Avalonia используется парная заглушка
/// (<see cref="ExplorerIntegrationService.Avalonia"/>), где <see cref="IsAvailable"/>
/// всегда <c>false</c>, а методы — no-op. Пункт настройки на Linux скрыт/заблокирован.
/// </para>
/// </summary>
public interface IExplorerIntegrationService
{
    /// <summary>
    /// Доступна ли интеграция с проводником на текущей платформе.
    /// Windows/WPF — <c>true</c>; Linux/Avalonia — <c>false</c>.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>true, если ассоциация <c>.1CD</c> и команды контекстного меню уже зарегистрированы.</summary>
    bool IsRegistered();

    /// <summary>Регистрирует ассоциацию <c>.1CD</c> и команды контекстного меню в <c>HKCU\Software\Classes</c>.</summary>
    void Register();

    /// <summary>Удаляет ассоциацию <c>.1CD</c> и команды контекстного меню из реестра.</summary>
    void Unregister();
}