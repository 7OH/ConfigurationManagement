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
                UrlCode = "Бухгалтерия предприятия",
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
                UrlCode = "Зарплата и управление персоналом",
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
                UrlCode = "Управление торговлей",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "11", Red = "11" },
                },
            },
            // Комплексная автоматизация.
            new OneCConfigType
            {
                Code = "KA",
                Name = "Комплексная автоматизация",
                UrlCode = "Комплексная автоматизация",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "2.5", Red = "2.5" },
                },
            },
            // ERP Управление холдингом.
            new OneCConfigType
            {
                Code = "ERP",
                Name = "ERP Управление холдингом",
                UrlCode = "ERP Управление холдингом",
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
                UrlCode = "Розница",
                IsBuiltIn = true,
                Editions =
                {
                    new OneCConfigEdition { Name = "2.3", Red = "2.3" },
                },
            },
            // Бухгалтерия государственного учреждения.
            new OneCConfigType
            {
                Code = "BGU",
                Name = "Бухгалтерия государственного учреждения",
                UrlCode = "Бухгалтерия государственного учреждения",
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