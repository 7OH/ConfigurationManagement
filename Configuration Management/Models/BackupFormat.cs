namespace Configuration_Management.Models;

/// <summary>
/// Формат файла резервной копии информационной базы.
/// DT — выгрузка данных ИБ (.dt), CF — выгрузка конфигурации (.cf),
/// ZIP — архив через System.IO.Compression, RAR — архив через внешний архиватор.
/// </summary>
public enum BackupFormat
{
    Dt,
    Cf,
    Zip,
    Rar
}