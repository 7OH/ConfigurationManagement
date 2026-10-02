using System.Collections.Generic;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Предопределённый (встроенный) набор типовых конфигураций 1С. Используется окном
/// «Актуальные релизы» и окном настройки связи ИБ ↔ конфигурация как стартовый список.
/// Пользователь может добавлять собственные конфигурации (хранятся в
/// <c>AppSettings.CustomConfigTypes</c>) — они объединяются со встроенными при показе.
/// </summary>
public static class BuiltInConfigTypes
{
    /// <summary>Единственный экземпляр предопределённого набора (неизменяемый).</summary>
    public static IReadOnlyList<OneCConfigType> All { get; } = Build();

    private static List<OneCConfigType> Build()
    {
        var list = new List<OneCConfigType>
        {
            // Бухгалтерия предприятия — наиболее распространённая типовая конфигурация.
            new OneCConfigType
            {
                Code = "BP",
                Name = "Бухгалтерия предприятия",
                // Имя конфигурации в метаданных 1С (issue #321): «БухгалтерияПредприятия».
                ConfigName = "БухгалтерияПредприятия",
                UrlCode = "Бухгалтерия предприятия",
                Nick = "AccountingCorp30",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "3.0", Red = "3.0" },
                    new OneCConfigEdition { Name = "2.0", Red = "2.0" },
                },
            },
            // Зарплата и управление персоналом.
            new OneCConfigType
            {
                Code = "ZUP",
                Name = "Зарплата и управление персоналом",
                // Имя конфигурации в метаданных 1С: «ЗарплатаИУправлениеПерсоналом» (issue #321).
                ConfigName = "ЗарплатаИУправлениеПерсоналом",
                UrlCode = "Зарплата и управление персоналом",
                // Точный ник для ЗУП 3.1 на releases.1c.ru не подтверждён — остаётся пустым,
                // чтобы не выдавать ложный результат проверки (будет заполнен после уточнения).
                Nick = string.Empty,
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "3.1", Red = "3.1" },
                },
            },
            // Управление торговлей.
            new OneCConfigType
            {
                Code = "UT",
                Name = "Управление торговлей",
                // Имя конфигурации в метаданных 1С: «УправлениеТорговлей» (issue #321).
                ConfigName = "УправлениеТорговлей",
                UrlCode = "Управление торговлей",
                // Точный ник для УТ 11 на releases.1c.ru не подтверждён — остаётся пустым.
                Nick = string.Empty,
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "11", Red = "11" },
                    new OneCConfigEdition { Name = "10.3", Red = "10.3" },
                },
            },
            // Комплексная автоматизация.
            new OneCConfigType
            {
                Code = "KA",
                Name = "Комплексная автоматизация",
                // Имя конфигурации в метаданных 1С: «КомплекснаяАвтоматизация» (issue #321).
                ConfigName = "КомплекснаяАвтоматизация",
                UrlCode = "Комплексная автоматизация",
                // Точный ник для КА 2.5 на releases.1c.ru не подтверждён — остаётся пустым.
                Nick = string.Empty,
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "2.5", Red = "2.5" },
                    new OneCConfigEdition { Name = "2.0", Red = "2.0" },
                    new OneCConfigEdition { Name = "1.1", Red = "1.1" },
                    new OneCConfigEdition { Name = "1.0", Red = "1.0" },
                },
            },
            // ERP Управление холдингом.
            new OneCConfigType
            {
                Code = "ERP",
                Name = "ERP Управление холдингом",
                // Имя конфигурации в метаданных 1С: «УправлениеПредприятием» (issue #321).
                ConfigName = "УправлениеПредприятием",
                UrlCode = "ERP Управление холдингом",
                // Точный ник для ERP 2.5 неизвестен — остаётся пустым (будет скорректировано).
                Nick = string.Empty,
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "2.5", Red = "2.5" },
                },
            },
            // Розница.
            new OneCConfigType
            {
                Code = "Retail",
                Name = "Розница",
                // Имя конфигурации в метаданных 1С: «Retail» (issue #321, по списку 7OH).
                ConfigName = "Retail",
                UrlCode = "Розница",
                // Точный ник для Розницы на releases.1c.ru не подтверждён — остаётся пустым.
                Nick = string.Empty,
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "3.0", Red = "3.0" },
                    new OneCConfigEdition { Name = "2.3", Red = "2.3" },
                },
            },
            // Бухгалтерия государственного учреждения.
            new OneCConfigType
            {
                Code = "BGU",
                Name = "Бухгалтерия государственного учреждения",
                // Имя конфигурации в метаданных 1С: «БухгалтерияГосударственногоУчреждения» (issue #321).
                ConfigName = "БухгалтерияГосударственногоУчреждения",
                UrlCode = "Бухгалтерия государственного учреждения",
                // Точный ник для БГУ неизвестен — остаётся пустым (будет скорректировано).
                Nick = string.Empty,
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "2.0", Red = "2.0" },
                },
            },
        };

        // Сегменты URL принято кодировать латиницей/цифрами; имя с пробелами и кириллицей
        // дополнительно экранируется при формировании адреса (Uri.EscapeDataString).
        return list;
    }
}