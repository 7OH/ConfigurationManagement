using System.Collections.Generic;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Хранилище пользовательских действий контекстного меню (функция 7): единый читаемый
/// JSON-файл <c>custom_actions.json</c> в каталоге данных профиля (рядом с
/// <c>settings.json</c>), по образцу <see cref="ICustomConfigTypesStore"/>. Запись —
/// атомарная (временный файл + замена), JSON с отступами и читаемой кириллицей.
/// </summary>
public interface ICustomActionsStore
{
    /// <summary>Путь к файлу действий (каталог данных профиля / явный override для тестов).</summary>
    string FilePath { get; }

    /// <summary>Загружает все действия (битый файл — пустой список, сортировка по имени).</summary>
    IReadOnlyList<CustomAction> LoadAll();

    /// <summary>Сохраняет действие (создаёт или перезаписывает запись по Id; атомарная запись).</summary>
    void Save(CustomAction action);

    /// <summary>Удаляет действие по Id.</summary>
    void Delete(string id);

    /// <summary>Возвращает действие по Id или null.</summary>
    CustomAction? Get(string id);
}