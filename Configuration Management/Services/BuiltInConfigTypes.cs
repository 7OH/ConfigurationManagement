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
            // Ники конфигураций/редакций — по списку 7OH (issue #321, комментарий 5952509522):
            // база каталога Accounting; редакции 2.0/3.0 имеют собственные каталоги
            // Accounting20_82/Accounting30 (UrlOverride — releases.1c.ru/project/<ник>).
            new OneCConfigType
            {
                Code = "BP",
                Name = "Бухгалтерия предприятия",
                ConfigName = "БухгалтерияПредприятия",
                UrlCode = "Бухгалтерия предприятия",
                Nick = "Accounting",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "3.0", Red = "3",
                        UrlOverride = "https://releases.1c.ru/project/Accounting30",
                    },
                    new OneCConfigEdition
                    {
                        Name = "2.0", Red = "2",
                        UrlOverride = "https://releases.1c.ru/project/Accounting20_82",
                    },
                },
            },
            // Зарплата и управление персоналом.
            new OneCConfigType
            {
                Code = "ZUP",
                Name = "Зарплата и управление персоналом",
                ConfigName = "ЗарплатаИУправлениеПерсоналом",
                UrlCode = "Зарплата и управление персоналом",
                Nick = "HRM30",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "3.1", Red = "3.1" },
                },
            },
            // Управление торговлей: каталог Trade, редакции 10.3/11.х — Trade103/Trade110.
            new OneCConfigType
            {
                Code = "UT",
                Name = "Управление торговлей",
                ConfigName = "УправлениеТорговлей",
                UrlCode = "Управление торговлей",
                Nick = "Trade",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "11", Red = "11",
                        UrlOverride = "https://releases.1c.ru/project/Trade110",
                    },
                    new OneCConfigEdition
                    {
                        Name = "10.3", Red = "10.3",
                        UrlOverride = "https://releases.1c.ru/project/Trade103",
                    },
                },
            },
            // Комплексная автоматизация: каталог ARAutomation, редакции 1.0/1.1/2.0.
            new OneCConfigType
            {
                Code = "KA",
                Name = "Комплексная автоматизация",
                ConfigName = "КомплекснаяАвтоматизация",
                UrlCode = "Комплексная автоматизация",
                Nick = "ARAutomation",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "2.0", Red = "2.0",
                        UrlOverride = "https://releases.1c.ru/project/ARAutomation20",
                    },
                    new OneCConfigEdition
                    {
                        Name = "1.1", Red = "1.1",
                        UrlOverride = "https://releases.1c.ru/project/ARAutomation11",
                    },
                    new OneCConfigEdition
                    {
                        Name = "1.0", Red = "1.0",
                        UrlOverride = "https://releases.1c.ru/project/ARAutomation10",
                    },
                },
            },
            // 1С:ERP Управление предприятием.
            new OneCConfigType
            {
                Code = "ERP",
                Name = "ERP Управление холдингом",
                ConfigName = "УправлениеПредприятием",
                UrlCode = "ERP Управление холдингом",
                Nick = "EnterpriseERP20",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "2.5", Red = "2.5" },
                },
            },
            // Розница: каталог Retail, редакции 2.3/3.0 — Retail23/Retail30.
            new OneCConfigType
            {
                Code = "Retail",
                Name = "Розница",
                ConfigName = "Retail",
                UrlCode = "Розница",
                Nick = "Retail",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition
                    {
                        Name = "3.0", Red = "3.0",
                        UrlOverride = "https://releases.1c.ru/project/Retail30",
                    },
                    new OneCConfigEdition
                    {
                        Name = "2.3", Red = "2.3",
                        UrlOverride = "https://releases.1c.ru/project/Retail23",
                    },
                },
            },
            // Бухгалтерия государственного учреждения.
            new OneCConfigType
            {
                Code = "BGU",
                Name = "Бухгалтерия государственного учреждения",
                ConfigName = "БухгалтерияГосударственногоУчреждения",
                UrlCode = "Бухгалтерия государственного учреждения",
                Nick = "StateAccounting20",
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