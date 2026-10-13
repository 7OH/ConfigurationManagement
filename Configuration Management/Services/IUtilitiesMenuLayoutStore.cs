using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Хранилище компоновки меню «Утилиты» (issue #374): единый читаемый JSON-файл
/// <c>utilities_menu_layout.json</c> в каталоге данных профиля (рядом с
/// <c>settings.json</c>), по образцу <see cref="ICustomActionsStore"/>. Файл можно
/// править вручную и переносить между установками. Запись — атомарная (временный
/// файл + замена), JSON с отступами и читаемой кириллицей.
/// </summary>
public interface IUtilitiesMenuLayoutStore
{
    /// <summary>Путь к файлу компоновки (каталог данных профиля / явный override для тестов).</summary>
    string FilePath { get; }

    /// <summary>
    /// Загружает компоновку меню. Если файла нет — возвращает null (построитель меню
    /// использует стандартную компоновку). Битый/несовместимый файл не роняет загрузку:
    /// возвращается null, файл на диске не трогается (пользователь сможет его поправить).
    /// </summary>
    UtilitiesMenuLayout? Load();

    /// <summary>Сохраняет компоновку меню (атомарная запись).</summary>
    void Save(UtilitiesMenuLayout layout);
}