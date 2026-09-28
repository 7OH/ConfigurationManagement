using System.IO;
using System.Security.Cryptography;
using System.Text;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистое сравнение двух выгрузок конфигурации формата <c>/DumpConfigToFiles</c>
/// (0.3.9.99, функция №9): обход каталога выгрузки, вычисление SHA-256 объектов
/// метаданных и классификация «добавлен/изменён/удалён/без изменений».
/// Никаких операций 1С — работает с файлами на диске, поэтому покрыт юнит-тестами.
/// </summary>
public static class ConfigurationDiffEngine
{
    /// <summary>Служебный файл корня выгрузки, не являющийся объектом метаданных.</summary>
    public const string ConfigDumpInfoFileName = "ConfigDumpInfo.xml";

    /// <summary>Корневой файл конфигурации (версия, имя); не объект, но важен как сигнал.</summary>
    public const string RootConfigurationFileName = "Configuration.xml";

    /// <summary>Каталог объектов метаданных внутри выгрузки.</summary>
    private const string ConfigurationDirName = "Configuration";

    private static readonly StringComparer PathComparer = StringComparer.Ordinal;

    /// <summary>
    /// Строит снимок выгрузки: обходит каталог <paramref name="dumpRoot"/>, вычисляет
    /// хэши объектов метаданных (файл <c>Имя.xml</c> или каталог <c>Имя/</c> с подфайлами)
    /// и хэш корневого <c>Configuration.xml</c>. Служебный <see cref="ConfigDumpInfoFileName"/>
    /// игнорируется; пути сравниваются регистрозависимо (Ordinal).
    /// </summary>
    internal static ConfigurationSnapshot BuildSnapshot(string dumpRoot)
    {
        if (string.IsNullOrWhiteSpace(dumpRoot))
            throw new ArgumentException("Каталог выгрузки не задан.", nameof(dumpRoot));

        var objects = new Dictionary<string, SnapshotObjectInfo>(PathComparer);
        var configurationDir = Path.Combine(dumpRoot, ConfigurationDirName);

        if (Directory.Exists(configurationDir))
        {
            foreach (var typeDir in Directory.EnumerateDirectories(configurationDir))
            {
                var typeName = Path.GetFileName(typeDir);
                if (string.IsNullOrWhiteSpace(typeName))
                    continue;

                foreach (var entry in Directory.EnumerateFileSystemEntries(typeDir))
                {
                    var name = Path.GetFileName(entry);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var key = $"{ConfigurationDirName}/{typeName}/{name}";
                    var info = File.Exists(entry)
                        ? HashFile(entry)
                        : HashDirectory(entry);
                    objects[key] = info;
                }
            }
        }

        var rootFilePath = Path.Combine(dumpRoot, RootConfigurationFileName);
        var rootHash = File.Exists(rootFilePath) ? HashFile(rootFilePath).Hash : null;

        return new ConfigurationSnapshot(dumpRoot, objects, rootHash);
    }

    /// <summary>
    /// Сравнивает два снимка и возвращает отчёт: по каждому объекту (объединение ключей)
    /// определяется статус; корневой <c>Configuration.xml</c> выносится флагом
    /// <see cref="ConfigurationDiffResult.RootFileChanged"/>.
    /// </summary>
    internal static ConfigurationDiffResult Compare(
        ConfigurationSnapshot left,
        ConfigurationSnapshot right,
        string leftLabel,
        string rightLabel,
        TimeSpan elapsed)
    {
        if (left is null) throw new ArgumentNullException(nameof(left));
        if (right is null) throw new ArgumentNullException(nameof(right));

        var keys = new HashSet<string>(left.Objects.Keys, PathComparer);
        keys.UnionWith(right.Objects.Keys);

        var objects = new List<MetadataObject>(keys.Count);
        foreach (var key in keys)
        {
            var hasLeft = left.Objects.TryGetValue(key, out var infoL);
            var hasRight = right.Objects.TryGetValue(key, out var infoR);

            DiffChangeKind kind;
            int fileCount;
            long totalBytes;
            if (!hasLeft)
            {
                kind = DiffChangeKind.Added;
                fileCount = infoR!.FileCount;
                totalBytes = infoR.TotalBytes;
            }
            else if (!hasRight)
            {
                kind = DiffChangeKind.Removed;
                fileCount = infoL!.FileCount;
                totalBytes = infoL.TotalBytes;
            }
            else if (!string.Equals(infoL!.Hash, infoR!.Hash, StringComparison.Ordinal))
            {
                kind = DiffChangeKind.Changed;
                fileCount = infoL.FileCount;
                totalBytes = infoL.TotalBytes;
            }
            else
            {
                kind = DiffChangeKind.Unchanged;
                fileCount = infoL.FileCount;
                totalBytes = infoL.TotalBytes;
            }

            objects.Add(new MetadataObject(
                TypeDirOf(key),
                NameOf(key),
                kind,
                fileCount,
                totalBytes));
        }

        objects.Sort(static (a, b) =>
        {
            var byType = string.Compare(a.TypeDir, b.TypeDir, StringComparison.Ordinal);
            return byType != 0 ? byType : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        var rootChanged = !string.Equals(left.RootFileHash, right.RootFileHash, StringComparison.Ordinal);

        return new ConfigurationDiffResult(leftLabel, rightLabel, objects, rootChanged, elapsed);
    }

    /// <summary>Хэш файла: SHA-256 содержимого, один файл.</summary>
    private static SnapshotObjectInfo HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(stream));
        return new SnapshotObjectInfo(hash, 1, new FileInfo(path).Length);
    }

    /// <summary>
    /// Хэш каталога-объекта: SHA-256 по отсортированному агрегату
    /// «относительный путь|хэш файла» всех файлов каталога. Сортировка делает
    /// хэш независимым от порядка обхода файловой системы.
    /// </summary>
    private static SnapshotObjectInfo HashDirectory(string directoryPath)
    {
        var entries = new List<(string RelPath, string Hash, long Size)>();

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(directoryPath, file).Replace('\\', '/');
            var fileHash = HashFile(file);
            entries.Add((relPath, fileHash.Hash, fileHash.TotalBytes));
        }

        entries.Sort(static (a, b) => string.Compare(a.RelPath, b.RelPath, StringComparison.Ordinal));

        var sb = new StringBuilder();
        long totalBytes = 0;
        foreach (var entry in entries)
        {
            sb.Append(entry.RelPath).Append('|').Append(entry.Hash).Append('\n');
            totalBytes += entry.Size;
        }

        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        return new SnapshotObjectInfo(hash, entries.Count, totalBytes);
    }

    /// <summary>Тип объекта из ключа: вторая часть пути <c>Configuration/<Тип>/<Имя></c>.</summary>
    private static string TypeDirOf(string key)
    {
        var segments = key.Split('/');
        return segments.Length >= 2 ? segments[1] : key;
    }

    /// <summary>Имя объекта из ключа: последняя часть пути.</summary>
    private static string NameOf(string key)
    {
        var segments = key.Split('/');
        return segments.Length > 0 ? segments[^1] : key;
    }
}