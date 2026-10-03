using System.Collections.Generic;
using System.Linq;

namespace Configuration_Management.Models;

/// <summary>
/// Типовая конфигурация 1С: внутренний код, отображаемое имя, сегмент web-адреса
/// обновлений (<c>UrlCode</c>) и список редакций/каталогов релизов. Может быть
/// предопределённой (<see cref="IsBuiltIn"/>, набор из <c>Services/BuiltInConfigTypes</c>)
/// либо пользовательской (создаётся/редактируется в окне настроек).
/// </summary>
public class OneCConfigType
{
    /// <summary>Внутренний стабильный код конфигурации (например «BP», «ZUP»). Используется
    /// для связи ИБ ↔ конфигурация в поле <c>Infobase.UpdateConfigCode</c>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Отображаемое имя конфигурации (например «Бухгалтерия предприятия»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Имя конфигурации в метаданных 1С (например «БухгалтерияПредприятия», «Retail»),
    /// как оно задано в самой конфигурации. НЕ участвует в построении адреса обновлений
    /// (в отличие от <see cref="UrlCode"/>/<see cref="Nick"/>) — служит для сопоставления
    /// данных о конфигурации, её версии и строки таблицы (issue #321).
    /// Пусто — сопоставление по имени не выполняется.
    /// </summary>
    public string ConfigName { get; set; } = string.Empty;

    /// <summary>Устаревшее поле (deprecated, issue #321): сегмент web-адреса
    /// <c><Конфигурация></c> по прежнему правилу 1С. Адрес обновлений строится ТОЛЬКО
    /// из <see cref="Nick"/> (<c>releases.1c.ru/project/<nick></c>), поле из UI удалено
    /// и не заполняется для новых записей. Оставлено для обратной совместимости JSON-файлов.</summary>
    public string UrlCode { get; set; } = string.Empty;

    /// <summary>Ник конфигурации на ресурсе обновлений 1С
    /// (<c>releases.1c.ru/version_files?nick=…</c>), например «AccountingCorp30» для
    /// Бухгалтерии предприятия 3.0. Если пуст — используется устаревший механизм
    /// формирования адреса (сегментный путь <c>downloads.1c.ru/ipp/…/Configs/…</c>).</summary>
    public string Nick { get; set; } = string.Empty;

    /// <summary>Признак предопределённой конфигурации из встроенного набора.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Признак пользовательской копии предопределённой конфигурации (issue #321): запись
    /// создаётся при правке встроенной строки в окне «Типовые конфигурации» и в общем списке
    /// (<see cref="ICustomConfigTypesStore.LoadAll"/>) заменяет предопределённую с тем же
    /// <see cref="Code"/>. Статический экземпляр <see cref="BuiltInConfigTypes"/> при этом не
    /// мутируется. Кнопка «Восстановить типовые» удаляет такие записи, возвращая
    /// предопределённый набор к исходному виду (пользовательские конфигурации не трогаются).
    /// </summary>
    public bool OverridesBuiltIn { get; set; }

    /// <summary>
    /// Идентификатор учётной записи ИТС из справочника <c>its_accounts.json</c> (issue #333),
    /// используемой для этой конфигурации при запросах к сайту 1С. Пусто — используется
    /// «Основная» запись справочника (либо выбранная в настройках). Обратная совместимость:
    /// отсутствие поля в старых JSON-файлах десериализуется в пустую строку.
    /// </summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Признак «отслеживать» в окне «Актуальные релизы» (по умолчанию true).</summary>
    public bool IsTracked { get; set; } = true;

    /// <summary>Список редакций/каталогов релизов конфигурации.</summary>
    public List<OneCConfigEdition> Editions { get; set; } = new();

    /// <summary>Сегмент URL конфигурации: UrlCode, либо (если пуст) имя.</summary>
    public string EffectiveUrlCode =>
        string.IsNullOrWhiteSpace(UrlCode) ? Name : UrlCode;

    /// <summary>Первая (по умолчанию) редакция конфигурации, или null, если редакций нет.</summary>
    public OneCConfigEdition? DefaultEdition => Editions.FirstOrDefault();

    /// <summary>Отображаемое имя конфигурации (для ComboBox и списков).</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Code : Name;
}