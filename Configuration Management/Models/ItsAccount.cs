namespace Configuration_Management.Models;

/// <summary>
/// Учётная запись доступа к ресурсам ИТС 1С (issue #333): логин/пароль сайта 1С
/// (downloads.1c.ru / portal.1c.ru / login.1c.ru) для проверки обновлений конфигураций
/// и скачивания дистрибутивов. Хранится в отдельном читаемом JSON-файле
/// <c>its_accounts.json</c> (см. Services.ItsAccountsStore).
/// </summary>
public class ItsAccount
{
    /// <summary>Идентификатор учётной записи (GUID, по образцу ScriptScenario/BackupScenario).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Наименование учётной записи для представления в списках.</summary>
    public string Name { get; set; } = "";

    /// <summary>Логин учётной записи сайта 1С.</summary>
    public string Login { get; set; } = "";

    /// <summary>Пароль учётной записи сайта 1С (хранится открыто в файле справочника,
    /// маскируется при журналировании — см. <see cref="Services.SensitiveDataMasker"/>).</summary>
    public string Password { get; set; } = "";

    /// <summary>Признак «Основная»: используется по умолчанию, когда для запроса не выбрана
    /// конкретная учётная запись. Меняется ТОЛЬКО через кнопку «Задать основным» окна
    /// справочника; при загрузке файла нормализуется хранилищем (ровно одна основная).</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Отображаемое имя (для ComboBox и списков).</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Login : Name;
}