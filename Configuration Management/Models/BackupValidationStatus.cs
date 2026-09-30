namespace Configuration_Management.Models;

/// <summary>
/// Статус проверки резервной копии (функция 8, цикл 0.3.9.200–0.3.9.207).
/// Результаты проверок хранятся в JSON-кэше (см. Services.BackupValidationCacheStore)
/// и отображаются в «Списке выгрузок» и «Центре обслуживания».
/// </summary>
public enum BackupValidationStatus
{
    /// <summary>
    /// Копия не проверялась либо запись кэша устарела после изменения файла
    /// (не совпадает fingerprint — размер/дата последней записи).
    /// </summary>
    NotChecked,

    /// <summary>Копия валидна: тестовое восстановление/чтение завершилось успешно.</summary>
    Valid,

    /// <summary>Копия повреждена: восстановление/чтение не удалось (указан тип ошибки).</summary>
    Corrupt
}