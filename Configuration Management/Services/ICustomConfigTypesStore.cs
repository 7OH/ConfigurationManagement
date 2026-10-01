using System.Collections.Generic;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Хранилище пользовательских типовых конфигураций 1С (issue #321): отдельный
/// читаемый JSON-файл <c>custom_config_types.json</c> рядом с настройками приложения.
/// Существующие пользовательские конфигурации из <see cref="AppSettings.CustomConfigTypes"/>
/// переносятся в файл автоматически при первом обращении (идемпотентно: файл создаётся
/// один раз, далее читается только он). Файл можно править вручную и передавать другим.
/// Общий список типовых конфигураций = <see cref="BuiltInConfigTypes"/> + пользовательские.
/// </summary>
public interface ICustomConfigTypesStore
{
    /// <summary>Полный путь к файлу пользовательских конфигураций.</summary>
    string FilePath { get; }

    /// <summary>Загружает пользовательские конфигурации из файла (с миграцией из настроек при первом запуске).</summary>
    IReadOnlyList<OneCConfigType> Load();

    /// <summary>Сохраняет пользовательские конфигурации в файл (читаемый UTF-8, атомарная запись).</summary>
    void Save(IReadOnlyCollection<OneCConfigType> types);

    /// <summary>
    /// Общий список типовых конфигураций: предопределённые + пользовательские из файла.
    /// Пользовательская копия предопределённой (<see cref="OneCConfigType.OverridesBuiltIn"/>)
    /// заменяет встроенную с тем же кодом; остальные пользовательские записи добавляются следом
    /// (несколько записей одной конфигурации — например ЗУП 3.0 и 3.1 — сосуществуют, issue #321).
    /// </summary>
    IReadOnlyList<OneCConfigType> LoadAll();

    /// <summary>
    /// «Восстановить типовые» (issue #321): удаляет пользовательские копии предопределённых
    /// конфигураций (<see cref="OneCConfigType.OverridesBuiltIn"/>), возвращая предопределённый
    /// набор к исходному виду <see cref="BuiltInConfigTypes.All"/>. Обычные пользовательские
    /// конфигурации не трогаются.
    /// </summary>
    void RestoreDefaults();
}