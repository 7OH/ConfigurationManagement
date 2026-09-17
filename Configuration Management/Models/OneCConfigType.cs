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

    /// <summary>Сегмент web-адреса <c><Конфигурация></c> по правилу 1С. Если пуст —
    /// при формировании URL используется <see cref="Name"/>.</summary>
    public string UrlCode { get; set; } = string.Empty;

    /// <summary>Признак предопределённой конфигурации из встроенного набора.</summary>
    public bool IsBuiltIn { get; set; }

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