using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сервис создания и распаковки архивов резервных копий.
/// ZIP — через <see cref="System.IO.Compression.ZipArchive"/>; RAR — через внешний
/// архиватор (winrar/rar), доступность которого проверяется методом <see cref="CanProduce"/>.
/// </summary>
public interface IArchiveService
{
    /// <summary>Создаёт архив из указанных файлов.</summary>
    /// <returns>True при успехе.</returns>
    bool CreateArchive(string archivePath, IEnumerable<string> filesToAdd);

    /// <summary>Распаковывает архив в указанный каталог.</summary>
    /// <returns>True при успехе.</returns>
    bool ExtractArchive(string archivePath, string targetDir);

    /// <summary>Поддерживает ли сервис создание указанного формата (например, RAR требует внешнего архиватора).</summary>
    bool CanProduce(BackupFormat format);
}