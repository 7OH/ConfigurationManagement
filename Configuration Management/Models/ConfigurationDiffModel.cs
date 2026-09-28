namespace Configuration_Management.Models;

/// <summary>
/// Модели отчёта о сравнении конфигураций (0.3.9.99, функция №9 «Config-Diff»).
/// Чистые записи без зависимостей от UI и платформы: сравнение двух выгрузок
/// <c>/DumpConfigToFiles</c> даёт список объектов метаданных верхнего уровня
/// (пара «тип, имя») с классификацией Добавлен/Изменён/Удалён/Без изменений.
/// </summary>
public enum DiffChangeKind
{
    /// <summary>Объект есть только в правой выгрузке (появился).</summary>
    Added,

    /// <summary>Объект есть в обеих выгрузках, но содержимое отличается.</summary>
    Changed,

    /// <summary>Объект есть только в левой выгрузке (удалён).</summary>
    Removed,

    /// <summary>Объект есть в обеих выгрузках, содержимое идентично.</summary>
    Unchanged
}

/// <summary>
/// Один объект метаданных верхнего уровня: пара «тип, имя» из каталога выгрузки
/// (например тип <c>Document</c>, имя <c>ЗаказКлиента</c>), статус сравнения,
/// число файлов объекта и суммарный размер. Для файлового объекта это сам файл
/// <c>Имя.xml</c>; для составного — все файлы его каталога <c>Имя/</c>.
/// </summary>
public sealed record MetadataObject(
    string TypeDir,
    string Name,
    DiffChangeKind Kind,
    int FileCount,
    long TotalBytes)
{
    /// <summary>Относительный путь объекта в выгрузке: <c>Configuration/<Тип>/<Имя></c>.</summary>
    public string Key => $"Configuration/{TypeDir}/{Name}";
}

/// <summary>
/// Итог сравнения двух конфигураций: подписи сторон, список объектов,
/// флаг изменения корневого <c>Configuration.xml</c> (версия конфигурации —
/// выводится отдельной строкой в шапке, а не засоряет таблицу объектов)
/// и затраченное время.
/// </summary>
public sealed record ConfigurationDiffResult(
    string LeftLabel,
    string RightLabel,
    IReadOnlyList<MetadataObject> Objects,
    bool RootFileChanged,
    TimeSpan Elapsed)
{
    /// <summary>Добавленные объекты (есть только справа).</summary>
    public int AddedCount => CountOf(DiffChangeKind.Added);

    /// <summary>Изменённые объекты (есть с обеих сторон, содержимое отличается).</summary>
    public int ChangedCount => CountOf(DiffChangeKind.Changed);

    /// <summary>Удалённые объекты (есть только слева).</summary>
    public int RemovedCount => CountOf(DiffChangeKind.Removed);

    /// <summary>Неизменённые объекты (идентичны с обеих сторон).</summary>
    public int UnchangedCount => CountOf(DiffChangeKind.Unchanged);

    private int CountOf(DiffChangeKind kind)
        => Objects?.Count(o => o.Kind == kind) ?? 0;
}

/// <summary>
/// Снимок одной выгрузки конфигурации: путь к каталогу выгрузки, хэши объектов
/// (ключ — относительный путь <c>Configuration/<Тип>/<Имя></c>) и хэш
/// корневого <c>Configuration.xml</c> (null, если файл отсутствует). Внутренняя
/// модель движка <see cref="Services.ConfigurationDiffEngine"/>; наружу не выходит.
/// </summary>
internal sealed record ConfigurationSnapshot(
    string RootPath,
    IReadOnlyDictionary<string, SnapshotObjectInfo> Objects,
    string? RootFileHash);

/// <summary>Хэш и размер одного объекта снимка выгрузки.</summary>
internal sealed record SnapshotObjectInfo(string Hash, int FileCount, long TotalBytes);