#if LINUX
namespace Configuration_Management.Services;

/// <summary>
/// Заглушка интеграции с проводником для Linux/Avalonia (функция №12 дорожной карты).
/// Интеграция с проводником — Windows-специфичная возможность (реестр HKCU, Shell),
/// на Linux недоступна: <see cref="IsAvailable"/> всегда <c>false</c>, а методы —
/// no-op. Соответствующий пункт настройки на Linux скрыт/заблокирован.
/// </summary>
public sealed class ExplorerIntegrationService : IExplorerIntegrationService
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public bool IsRegistered() => false;

    /// <inheritdoc />
    public void Register()
    {
        // На Linux интеграция с проводником недоступна — ничего не делаем.
    }

    /// <inheritdoc />
    public void Unregister()
    {
        // На Linux интеграция с проводником недоступна — ничего не делаем.
    }
}
#endif