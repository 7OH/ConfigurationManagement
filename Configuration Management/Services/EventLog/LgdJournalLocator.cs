using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services.EventLog;

/// <summary>
/// Поиск каталога журнала регистрации информационной базы и определение его
/// физического формата.
/// Для файловых баз журнал лежит в подкаталоге 1Cv8Log рядом с 1Cv8.1CD.
/// Для клиент-серверных и веб-баз путь к журналу находится на сервере 1С
/// (srvinfo\reg_<port>\<GUID>\1Cv8Log) и на первом этапе клиенту
/// недоступен — такие базы открывают журнал по локальной копии через команду
/// «Открыть журнал из файла…» (этап 0.3.9.164).
/// </summary>
public static class LgdJournalLocator
{
    /// <summary>Имя подкаталога журнала в каталоге файловой базы.</summary>
    public const string LogDirectoryName = "1Cv8Log";

    /// <summary>Имя SQLite-файла журнала (новый формат).</summary>
    public const string SqliteFileName = "1Cv8.lgd";

    /// <summary>Имя описательного файла последовательного формата.</summary>
    public const string LgfFileName = "1Cv8.lgf";

    /// <summary>Расширение файлов-фрагментов последовательного формата.</summary>
    public const string LgpExtension = ".lgp";

    /// <summary>
    /// Ищет журнал регистрации для информационной базы. Для не-файловых баз
    /// возвращает невалидную локацию с пояснением в
    /// <see cref="JournalLocation.ErrorMessage"/>.
    /// </summary>
    public static JournalLocation Resolve(Infobase ib)
    {
        if (ib.Connection.Type != ConnectionType.File)
        {
            return new JournalLocation
            {
                ErrorMessage = "Журнал регистрации клиент-серверной (веб) базы хранится на сервере 1С; " +
                               "откройте локальную копию журнала через «Открыть журнал из файла…»."
            };
        }

        var baseDir = InfobaseMaintenanceService.GetFileBaseDirectory(ib);
        if (string.IsNullOrEmpty(baseDir))
        {
            return new JournalLocation
            {
                ErrorMessage = "Каталог файловой базы не определён."
            };
        }

        return FromDirectory(Path.Combine(baseDir, LogDirectoryName));
    }

    /// <summary>
    /// Определяет формат журнала в каталоге <paramref name="logDir"/>.
    /// Приоритет: файл 1Cv8.lgd (SQLite), затем 1Cv8.lgf + *.lgp (последовательный).
    /// Имена файлов сравниваются без учёта регистра (Linux пишет их в точном
    /// регистре, но лишняя устойчивость ничего не стоит).
    /// </summary>
    public static JournalLocation FromDirectory(string? logDir)
    {
        if (string.IsNullOrEmpty(logDir) || !Directory.Exists(logDir))
        {
            return new JournalLocation
            {
                LogDir = logDir ?? string.Empty,
                ErrorMessage = $"Каталог журнала регистрации не найден: {logDir ?? "(пусто)"}"
            };
        }

        var sqliteFile = FindFileIgnoreCase(logDir, SqliteFileName);
        if (sqliteFile is not null)
        {
            return new JournalLocation
            {
                LogDir = logDir,
                Format = LgdFormat.Sqlite,
                MainFile = sqliteFile,
                DataFiles = new[] { sqliteFile }
            };
        }

        var lgfFile = FindFileIgnoreCase(logDir, LgfFileName);
        if (lgfFile is not null)
        {
            var fragments = Directory.EnumerateFiles(logDir)
                .Where(f => string.Equals(Path.GetExtension(f), LgpExtension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new JournalLocation
            {
                LogDir = logDir,
                Format = fragments.Count > 0 ? LgdFormat.Sequential : LgdFormat.Unknown,
                MainFile = lgfFile,
                DataFiles = fragments,
                ErrorMessage = fragments.Count == 0
                    ? $"Найден файл {LgfFileName}, но в каталоге нет фрагментов журнала (*.lgp): {logDir}"
                    : null
            };
        }

        return new JournalLocation
        {
            LogDir = logDir,
            ErrorMessage = $"Журнал регистрации не найден в каталоге: {logDir}"
        };
    }

    private static string? FindFileIgnoreCase(string dir, string fileName) =>
        Directory.EnumerateFiles(dir)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
}