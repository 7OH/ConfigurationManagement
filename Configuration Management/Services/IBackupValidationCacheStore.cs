using System.Collections.Generic;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Хранилище результатов проверки резервных копий (функция 8, этап 0.3.9.200):
/// единый читаемый JSON-файл <c>backup_validation_cache.json</c> в каталоге данных
/// профиля (рядом с <c>settings.json</c>), по образцу
/// <see cref="ICustomActionsStore"/>. Запись — атомарная (временный файл + замена),
/// JSON с отступами и читаемой кириллицей. Ключ записи — нормализованный полный
/// путь к файлу копии (регистронезависимое сравнение).
/// </summary>
public interface IBackupValidationCacheStore
{
    /// <summary>Путь к файлу кэша (каталог данных профиля / явный override для тестов).</summary>
    string FilePath { get; }

    /// <summary>
    /// Загружает все результаты (битый/отсутствующий файл — пустой список,
    /// сортировка по нормализованному пути).
    /// </summary>
    IReadOnlyList<BackupValidationResult> LoadAll();

    /// <summary>Возвращает результат по пути файла или <c>null</c>.</summary>
    BackupValidationResult? Get(string filePath);

    /// <summary>
    /// Сохраняет результат (создаёт или перезаписывает запись по пути; атомарная запись).
    /// Пустой путь игнорируется.
    /// </summary>
    void Save(BackupValidationResult result);

    /// <summary>Удаляет запись по пути файла (нет записи/файла — no-op).</summary>
    void Delete(string filePath);
}