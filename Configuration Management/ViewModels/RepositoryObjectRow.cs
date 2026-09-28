using System;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка состава версии «Обозревателя хранилища конфигурации» (0.3.9.128, этап 2):
/// локализованный тип метаданных (через <see cref="MetadataTypeLocalizer"/>), имя
/// объекта, владелец для вложенных объектов и флаг верхнего уровня. Чистая модель
/// форматирования поверх <see cref="RepositoryObjectInfo"/> — без платформенных
/// зависимостей (обе платформы, WPF и Avalonia).
/// </summary>
public sealed class RepositoryObjectRow
{
    private readonly RepositoryObjectInfo _info;

    public RepositoryObjectRow(RepositoryObjectInfo info)
    {
        _info = info ?? throw new ArgumentNullException(nameof(info));
    }

    /// <summary>Локализованное имя типа метаданных (каталог выгрузки, напр. «Документ»).</summary>
    public string TypeName => MetadataTypeLocalizer.GetDisplayName(_info.TypeDir, LocalizationManager.T);

    /// <summary>Имя объекта (верхнего уровня — файла/каталога выгрузки; вложенного — файла без расширения).</summary>
    public string Name => _info.Name ?? string.Empty;

    /// <summary>Владелец: для вложенных объектов — объект верхнего уровня; для верхнего — пусто.</summary>
    public string Owner => _info.Owner ?? string.Empty;

    /// <summary>Признак объекта верхнего уровня (непосредственно в Configuration/<Тип>/).</summary>
    public bool IsTopLevel => _info.IsTopLevel;

    /// <summary>Отступ вложенных объектов: «↳ » перед именем, для верхнего уровня — пусто.</summary>
    public string NestedMark => IsTopLevel ? string.Empty : "↳ ";

    /// <summary>Колонка «Имя»: отступ вложенных + имя объекта.</summary>
    public string DisplayName => NestedMark + Name;
}