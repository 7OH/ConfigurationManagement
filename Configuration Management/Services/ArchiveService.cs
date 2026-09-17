using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IArchiveService"/>. ZIP упаковывается стандартными средствами .NET
/// (<see cref="ZipFile"/>), RAR — внешним архиватором (winrar/rar), найденным в настройках
/// (<see cref="AppSettings.RarExecutablePath"/>) или в PATH. Все операции в try/catch — ошибки
/// не роняют UI, а возвращаются кодом результата.
/// </summary>
public class ArchiveService : IArchiveService
{
    private readonly IInfobaseRepository? _repository;
    private readonly IAppLogger? _logger;

    public ArchiveService(IInfobaseRepository? repository = null, IAppLogger? logger = null)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool CanProduce(BackupFormat format)
    {
        if (format != BackupFormat.Rar)
            return true;
        return !string.IsNullOrWhiteSpace(ResolveRarExecutable());
    }

    /// <inheritdoc />
    public bool CreateArchive(string archivePath, IEnumerable<string> filesToAdd)
    {
        var files = (filesToAdd?.Where(File.Exists) ?? Enumerable.Empty<string>()).ToList();
        if (files.Count == 0)
            return false;

        try
        {
            var dir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            if (ext == ".rar")
                return CreateRarArchive(archivePath, files);

            using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            foreach (var file in files)
                zip.CreateEntryFromFile(file, Path.GetFileName(file));
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Error("Ошибка создания архива резервной копии", ex);
            return false;
        }
    }

    /// <inheritdoc />
    public bool ExtractArchive(string archivePath, string targetDir)
    {
        try
        {
            Directory.CreateDirectory(targetDir);
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            if (ext == ".rar")
                return ExtractRarArchive(archivePath, targetDir);

            ZipFile.ExtractToDirectory(archivePath, targetDir);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Error("Ошибка распаковки архива резервной копии", ex);
            return false;
        }
    }

    private bool CreateRarArchive(string archivePath, List<string> files)
    {
        var rar = ResolveRarExecutable();
        if (string.IsNullOrWhiteSpace(rar))
            return false;

        // a -ep1 "archive.rar" "file1" "file2"
        var args = new List<string> { "a", "-ep1", $"\"{archivePath}\"" };
        args.AddRange(files.Select(f => $"\"{f}\""));
        return RunArchiver(rar, string.Join(" ", args));
    }

    private bool ExtractRarArchive(string archivePath, string targetDir)
    {
        var rar = ResolveRarExecutable();
        if (string.IsNullOrWhiteSpace(rar))
            return false;

        // x -o+ -y "archive.rar" "targetDir\"
        var target = targetDir.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return RunArchiver(rar, $"x -o+ -y \"{archivePath}\" \"{target}\"");
    }

    private bool RunArchiver(string exe, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
                return false;
            if (!process.WaitForExit(10 * 60 * 1000))
            {
                try { process.Kill(); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger?.Error("Ошибка запуска внешнего архиватора", ex);
            return false;
        }
    }

    private string? ResolveRarExecutable()
    {
        try
        {
            var configured = _repository?.LoadSettings()?.RarExecutablePath?.Trim();
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;

            foreach (var name in new[] { "WinRAR.exe", "winrar.exe", "Rar.exe", "rar.exe", "rar" })
            {
                var found = FindInPath(name);
                if (found is not null)
                    return found;
            }
        }
        catch
        {
            // Настройки недоступны — пробуем PATH дальше.
        }
        return null;
    }

    private static string? FindInPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            try
            {
                var candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Недопустимый каталог в PATH — пропускаем.
            }
        }
        return null;
    }
}