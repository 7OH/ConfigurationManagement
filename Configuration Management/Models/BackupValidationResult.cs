using System;
using System.IO;
using Configuration_Management.Localization;

namespace Configuration_Management.Models;

/// <summary>
/// Результат проверки одной резервной копии (функция 8, этап 0.3.9.200).
/// Хранится в JSON-кэше (см. Services.BackupValidationCacheStore) и отображается
/// в колонке «Проверка» окна «Список выгрузок», в задании по расписанию
/// «Проверка резервных копий» и в «Центре обслуживания».
/// </summary>
public class BackupValidationResult
{
    /// <summary>Полный путь к проверенному файлу (нормализованный).</summary>
    public string FilePath { get; set; } = "";

    /// <summary>Статус проверки.</summary>
    public BackupValidationStatus Status { get; set; } = BackupValidationStatus.NotChecked;

    /// <summary>
    /// Тип ошибки при повреждении (ключ локализации: BackupValidation.Error.*)
    /// либо null при успехе / невыполненной проверке.
    /// </summary>
    public string? ErrorKind { get; set; }

    /// <summary>Текст ошибки/детали (вывод ibcmd, сообщение архиватора) — для журнала окна прогресса.</summary>
    public string? ErrorDetail { get; set; }

    /// <summary>Глубина выполненной проверки.</summary>
    public BackupValidationDepth Depth { get; set; } = BackupValidationDepth.Fast;

    /// <summary>Дата и время проверки (локальное).</summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>Длительность проверки.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Размер файла в байтах на момент проверки.</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Отпечаток файла на момент проверки (размер + LastWriteTimeUtc):
    /// если файл изменился после проверки — запись кэша считается устаревшей
    /// (<see cref="IsFresh"/> возвращает false, отображение даёт «не проверена»).
    /// </summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>
    /// Текст колонки «Проверка» по сохранённому состоянию записи:
    /// «валидна 12.03.2026 02:00» / «повреждена …» / «не проверена».
    /// Не учитывает устаревание fingerprint — для актуального отображения
    /// используйте <see cref="DisplayTextFor"/>(filePath).
    /// </summary>
    public string DisplayText => Status switch
    {
        BackupValidationStatus.Valid => $"{LocalizationManager.T("BackupValidation.Status.Valid")} {CheckedAt:dd.MM.yyyy HH:mm}",
        BackupValidationStatus.Corrupt => $"{LocalizationManager.T("BackupValidation.Status.Corrupt")} {CheckedAt:dd.MM.yyyy HH:mm}",
        _ => LocalizationManager.T("BackupValidation.Status.NotChecked")
    };

    /// <summary>
    /// Актуальный текст колонки «Проверка»: если файл не существует или изменился
    /// с момента проверки (не совпадает fingerprint) — «не проверена», иначе —
    /// <see cref="DisplayText"/>.
    /// </summary>
    /// <param name="filePath">Полный путь к файлу копии (может не существовать).</param>
    public string DisplayTextFor(string filePath) =>
        IsFresh(filePath) ? DisplayText : LocalizationManager.T("BackupValidation.Status.NotChecked");

    /// <summary>
    /// Актуальна ли запись кэша для указанного файла: путь существует и
    /// <see cref="Fingerprint"/> совпадает с текущим состоянием файла.
    /// </summary>
    /// <param name="filePath">Полный путь к файлу копии.</param>
    public bool IsFresh(string filePath)
    {
        if (string.IsNullOrWhiteSpace(Fingerprint))
            return false;
        try
        {
            return File.Exists(filePath)
                && string.Equals(Fingerprint, BuildFingerprint(filePath), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Строит отпечаток файла: размер + дата последней записи (UTC).
    /// Используется при сохранении результата и для проверки актуальности кэша.
    /// </summary>
    /// <param name="filePath">Полный путь к файлу копии.</param>
    /// <returns>Строка «<c><size>|<LastWriteTimeUtc:o></c>» или пустая строка при ошибке/отсутствии файла.</returns>
    public static string BuildFingerprint(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            return fi.Exists ? $"{fi.Length}|{fi.LastWriteTimeUtc:o}" : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}