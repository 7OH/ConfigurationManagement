using System;

namespace Configuration_Management.Models;

/// <summary>
/// Версия хранилища конфигурации (цикл 0.3.9.127–0.3.9.130, «Обозреватель хранилища
/// конфигурации»): номер, дата фиксации, автор, комментарий и признак актуальной версии.
/// Чистая запись без зависимостей от UI и платформы; наполняется парсером отчёта по
/// истории (<c>/ConfigurationRepositoryReport</c>) либо служебной записью «актуальная
/// версия» (Number = -1, см. <c>Services.RepositoryStorageService.GetHistoryAsync</c>).
/// </summary>
public sealed record RepositoryVersion(
    /// <summary>Номер версии хранилища (>= 1); -1 — служебная запись «актуальная версия».</summary>
    int Number,
    /// <summary>Дата и время фиксации версии в хранилище.</summary>
    DateTime Date,
    /// <summary>Имя пользователя хранилища, зафиксировавшего версию.</summary>
    string Author,
    /// <summary>Комментарий к версии (может быть пустым).</summary>
    string Comment,
    /// <summary>Признак актуальной (последней) версии хранилища.</summary>
    bool IsCurrent);

/// <summary>
/// Объект метаданных выгрузки конфигурации для «Обозревателя хранилища конфигурации»:
/// тип-каталог выгрузки (<c>Configuration/<Тип>/…</c>), имя объекта, владелец для
/// вложенных объектов (объект верхнего уровня, которому принадлежит вложенный) и флаг
/// верхнего уровня. Чистая запись; наполняется <c>Services.ConfigurationDiffEngine.BuildObjectList</c>.
/// </summary>
public sealed record RepositoryObjectInfo(
    /// <summary>Тип-каталог выгрузки (например <c>Document</c>, <c>Catalog</c>; для вложенных — подкаталог объекта, например <c>Forms</c>).</summary>
    string TypeDir,
    /// <summary>Имя объекта (для файлового объекта верхнего уровня — имя файла <c><Имя>.xml</c>, для каталога — имя каталога; для вложенного — имя файла без расширения).</summary>
    string Name,
    /// <summary>Владелец: для вложенных объектов — имя объекта верхнего уровня; для верхнего уровня — пустая строка.</summary>
    string Owner,
    /// <summary>Признак объекта верхнего уровня (непосредственно в <c>Configuration/<Тип>/</c>).</summary>
    bool IsTopLevel);