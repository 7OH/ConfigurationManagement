namespace Configuration_Management.Models;

/// <summary>
/// Опциональные учётные данные сценария резервирования.
/// Когда <see cref="UseInfobaseAuth"/> истинно (по умолчанию), для подключения к ИБ при
/// выгрузке/восстановлении используются учётные данные базы (<see cref="Infobase.ConfiguratorAuth"/>).
/// Иначе применяются явные <see cref="User"/>/<see cref="Password"/>.
/// </summary>
public class BackupCredential
{
    /// <summary>Использовать авторизацию информационной базы (по умолчанию — да).</summary>
    public bool UseInfobaseAuth { get; set; } = true;

    /// <summary>Логин для подключения к ИБ, когда <see cref="UseInfobaseAuth"/> выключен.</summary>
    public string User { get; set; } = "";

    /// <summary>Пароль для подключения к ИБ, когда <see cref="UseInfobaseAuth"/> выключен.</summary>
    public string Password { get; set; } = "";
}