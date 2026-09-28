using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Оркестратор операций с хранилищем конфигурации 1С («Обозреватель хранилища конфигурации»,
/// 0.3.9.127, этап 1): пакетный режим DESIGNER с ключами /ConfigurationRepositoryF/N/P и
/// операциями DumpCfg/Report/Lock/Unlock (та же механика, что у «Пакетного обновления из
/// хранилищ» 0.3.9.88 и ConfigurationDiff 0.3.9.99). Ожидание завершения — через событие
/// <see cref="OneCLauncher.DesignerBatchCompleted"/> (копия паттерна
/// <c>ConfigurationDiffService.WaitForBatchAsync</c>, таймаут 60 мин); ошибки — человекочитаемые
/// <see cref="RepositoryStorageException"/> (образец — <see cref="ConfigurationDiffException"/>).
///
/// РАЗВЕДКА ЭТАПА 1 (платформа 8.3.27.2325): грамматика repository-ключей подтверждена на реальной
/// платформе (см. комментарий в <see cref="RepositoryHistoryParser"/>), но создание файлового
/// хранилища на этой машине блокируется политикой записи (1AC5-5074/0E2D-1589), поэтому
/// фактический формат отчёта по истории не проверен: по документации это табличный документ
/// (.mxl), текстовые форматы не документированы → история ограничивается актуальной версией
/// (см. <see cref="GetHistoryAsync"/>), а парсер <see cref="RepositoryHistoryParser"/> носит
/// запасной характер.
/// </summary>
public sealed class RepositoryStorageService : IRepositoryStorageService
{
    /// <summary>Таймаут ожидания одного запуска 1cv8 DESIGNER (как ConfigurationDiffService).</summary>
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromMinutes(60);

    /// <summary>Таймаут создания временной ИБ из .cf версии хранилища (большие конфигурации).</summary>
    private const int CreateInfoBaseTimeoutMs = 30 * 60 * 1000;

    /// <summary>Префикс временного каталога: %TEMP%\cm_repo_<guid>.</summary>
    private const string TempRootPrefix = "cm_repo_";

    /// <inheritdoc />
    public Task<string> DumpVersionToCfAsync(Infobase infobase, int? version, string cfPath,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (infobase is null) throw new ArgumentNullException(nameof(infobase));

        return Task.Run(() => DumpVersionToCfCore(infobase, version, cfPath, progress, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RepositoryObjectInfo>> LoadVersionObjectsAsync(Infobase infobase, int? version,
        string platformVersion, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (infobase is null) throw new ArgumentNullException(nameof(infobase));

        return Task.Run(
            () => LoadVersionObjectsCore(infobase, version, platformVersion, progress, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RepositoryVersion>> GetHistoryAsync(Infobase infobase, int? nBegin = null, int? nEnd = null,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (infobase is null) throw new ArgumentNullException(nameof(infobase));

        return Task.Run(() => GetHistoryCore(infobase, nBegin, nEnd, progress, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task LockAsync(Infobase infobase, string? objectsXmlPath = null, CancellationToken cancellationToken = default)
    {
        if (infobase is null) throw new ArgumentNullException(nameof(infobase));

        return Task.Run(
            () => LockUnlockCore(infobase, OneCLauncher.DesignerBatchOperation.RepositoryLock, objectsXmlPath, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UnlockAsync(Infobase infobase, string? objectsXmlPath = null, CancellationToken cancellationToken = default)
    {
        if (infobase is null) throw new ArgumentNullException(nameof(infobase));

        return Task.Run(
            () => LockUnlockCore(infobase, OneCLauncher.DesignerBatchOperation.RepositoryUnlock, objectsXmlPath, cancellationToken),
            cancellationToken);
    }

    // ------------------------------------------------------------------
    // Core-реализации (выполняются в Task.Run, как ConfigurationDiffService)
    // ------------------------------------------------------------------

    /// <summary>Выгрузка версии хранилища в .cf (/ConfigurationRepositoryDumpCfg "файл" [-v N]).</summary>
    private string DumpVersionToCfCore(Infobase infobase, int? version, string cfPath,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(cfPath))
            throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrNoCfPath"));

        EnsureHasRepository(infobase);

        progress?.Report(string.Format(LocalizationManager.T("Repo.StageDumpVersion"), version?.ToString() ?? "-1"));
        var started = OneCLauncher.RunDesignerBatch(
            infobase,
            OneCLauncher.DesignerBatchOperation.RepositoryDumpCfg,
            cfPath,
            repositoryVersion: version);
        if (!started)
            throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrOperationFailed"));

        var info = WaitForBatchAsync(
            OneCLauncher.DesignerBatchOperation.RepositoryDumpCfg,
            cfPath,
            BatchTimeout).GetAwaiter().GetResult();

        if (info is null)
            throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrTimeout"));
        if (!info.Success)
            throw new RepositoryStorageException(info.ErrorMessage ?? LocalizationManager.T("Repo.ErrOperationFailed"));

        return cfPath;
    }

    /// <summary>
    /// Состав версии хранилища: DumpCfg во временный .cf → CREATEINFOBASE (с /UseTemplate) →
    /// /DumpConfigToFiles → <see cref="ConfigurationDiffEngine.BuildObjectList"/>.
    /// Временный каталог %TEMP%\cm_repo_<guid> удаляется в finally.
    /// </summary>
    private IReadOnlyList<RepositoryObjectInfo> LoadVersionObjectsCore(Infobase infobase, int? version,
        string platformVersion, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(platformVersion))
            throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrNoPlatform"));

        var tmpRoot = Path.Combine(Path.GetTempPath(), TempRootPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmpRoot);

            // 1. Выгрузка версии хранилища во временный .cf (операция хранилища).
            var cfPath = Path.Combine(tmpRoot, "version.cf");
            DumpVersionToCfCore(infobase, version, cfPath, progress, cancellationToken);

            // 2. Распаковка .cf во временную файловую ИБ — приём ConfigurationDiffService.PrepareSnapshotFromCf.
            var ibDir = Path.Combine(tmpRoot, "ib");
            progress?.Report(LocalizationManager.T("Repo.StageCreateTempBase"));
            var created = OneCLauncher.CreateInfoBase(
                platformVersion,
                isFile: true,
                filePath: ibDir,
                server: null,
                databaseName: null,
                templatePath: cfPath,
                timeoutMs: CreateInfoBaseTimeoutMs);
            if (!created.Ok)
                throw new RepositoryStorageException(created.Error ?? LocalizationManager.T("Repo.ErrOperationFailed"));

            // 3. Выгрузка конфигурации временной базы в каталог XML-файлов.
            var dumpDir = Path.Combine(tmpRoot, "dump");
            DumpTempBaseToFiles(platformVersion, ibDir, dumpDir, progress, cancellationToken);

            // 4. Чистый обход дерева выгрузки: верхний уровень + вложенные объекты с владельцами.
            progress?.Report(LocalizationManager.T("Repo.StageParseObjects"));
            return ConfigurationDiffEngine.BuildObjectList(dumpDir, LocalizationManager.T);
        }
        finally
        {
            TryDeleteDirectory(tmpRoot);
        }
    }

    /// <summary>
    /// История версий хранилища (/ConfigurationRepositoryReport "файл" [-NBegin N] [-NEnd N] + парсер).
    /// РЕШЕНИЕ РАЗВЕДКИ ЭТАПА 1: фактический формат отчёта — табличный документ (.mxl),
    /// текстовые форматы не документированы; если файл отчёта не читается как текст (обычный
    /// случай) или парсер не нашёл ни одной строки — возвращается одна запись «актуальная
    /// версия» { Number = -1, Comment = "актуальная версия", IsCurrent = true }, а состав
    /// версии доступен через <see cref="LoadVersionObjectsAsync"/> с version = null (DumpCfg
    /// без -v). Ограничение фиксируется в UI-hint и README (этапы 2–4).
    /// </summary>
    private IReadOnlyList<RepositoryVersion> GetHistoryCore(Infobase infobase, int? nBegin, int? nEnd,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureHasRepository(infobase);

        var tmpRoot = Path.Combine(Path.GetTempPath(), TempRootPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmpRoot);
            var reportPath = Path.Combine(tmpRoot, "history_report.txt");

            progress?.Report(LocalizationManager.T("Repo.StageReport"));
            var started = OneCLauncher.RunDesignerBatch(
                infobase,
                OneCLauncher.DesignerBatchOperation.RepositoryReport,
                reportPath,
                repositoryReportBegin: nBegin,
                repositoryReportEnd: nEnd);
            if (!started)
                throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrOperationFailed"));

            var info = WaitForBatchAsync(
                OneCLauncher.DesignerBatchOperation.RepositoryReport,
                reportPath,
                BatchTimeout).GetAwaiter().GetResult();

            if (info is null)
                throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrTimeout"));
            if (!info.Success)
                throw new RepositoryStorageException(info.ErrorMessage ?? LocalizationManager.T("Repo.ErrOperationFailed"));

            // Отчёт — табличный документ: текстовый формат обычно недоступен, читаем запасным
            // парсером только если файл содержит текст.
            string text;
            try { text = File.ReadAllText(reportPath); }
            catch { text = string.Empty; }

            var versions = RepositoryHistoryParser.ParseReport(text);
            if (versions.Count > 0)
                return versions;

            // Формат отчёта недоступен для чтения (mxl/двоичный) — история ограничена актуальной версией.
            return new[] { new RepositoryVersion(-1, DateTime.MinValue, string.Empty,
                LocalizationManager.T("Repo.CurrentVersionComment"), IsCurrent: true) };
        }
        finally
        {
            TryDeleteDirectory(tmpRoot);
        }
    }

    /// <summary>Захват/отмена захвата объектов хранилища (/ConfigurationRepositoryLock|Unlock [-objects]).</summary>
    private void LockUnlockCore(Infobase infobase, OneCLauncher.DesignerBatchOperation operation,
        string? objectsXmlPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureHasRepository(infobase);

        // Комментарий операции платформа НЕ принимает (ограничение разведки плана §3.3):
        // комментарий ведётся локально (журнал окна + AddLaunchHistory, этап 4).

        var started = OneCLauncher.RunDesignerBatch(infobase, operation, objectsXmlPath);
        if (!started)
            throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrOperationFailed"));

        // Для Lock/Unlock сопоставление события — только по операции (выходного файла нет;
        // активна всегда одна пакетная операция — IsDesignerBlocked).
        var info = WaitForBatchAsync(operation, outputPath: null, BatchTimeout).GetAwaiter().GetResult();

        if (info is null)
            throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrTimeout"));
        if (!info.Success)
            throw new RepositoryStorageException(info.ErrorMessage ?? LocalizationManager.T("Repo.ErrOperationFailed"));
    }

    // ------------------------------------------------------------------
    // Вспомогательные
    // ------------------------------------------------------------------

    /// <summary>Проверяет, что у базы задан адрес хранилища конфигурации.</summary>
    private static void EnsureHasRepository(Infobase infobase)
    {
        if (infobase.Repository is null || !infobase.Repository.HasServer)
            throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrNoRepository"));
    }

    /// <summary>Выгружает конфигурацию временной файловой ИБ в каталог XML-файлов (/DumpConfigToFiles).</summary>
    private void DumpTempBaseToFiles(string platformVersion, string ibDir, string dumpDir,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try { Directory.CreateDirectory(dumpDir); } catch { /* каталог создаст RunDesignerBatch */ }

        var tempBase = new Infobase
        {
            Name = "cm_repo_temp",
            PlatformVersion = platformVersion,
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = ibDir }
        };

        progress?.Report(LocalizationManager.T("Repo.StageDumpConfig"));
        var started = OneCLauncher.RunDesignerBatch(
            tempBase,
            OneCLauncher.DesignerBatchOperation.DumpConfigToFiles,
            dumpDir);
        if (!started)
            throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrOperationFailed"));

        var info = WaitForBatchAsync(
            OneCLauncher.DesignerBatchOperation.DumpConfigToFiles,
            dumpDir,
            BatchTimeout).GetAwaiter().GetResult();

        if (info is null)
            throw new RepositoryStorageException(LocalizationManager.T("Repo.ErrTimeout"));
        if (!info.Success)
            throw new RepositoryStorageException(info.ErrorMessage ?? LocalizationManager.T("Repo.ErrOperationFailed"));
    }

    /// <summary>
    /// Ожидает завершения конкретной пакетной операции DESIGNER через событие
    /// <see cref="OneCLauncher.DesignerBatchCompleted"/> с таймаутом (копия паттерна
    /// <c>ConfigurationDiffService.WaitForBatchAsync</c>; 60 мин). Для операций без выходного
    /// файла (Lock/Unlock, outputPath == null) сопоставление выполняется только по операции —
    /// активна всегда одна пакетная операция.
    /// </summary>
    private static async Task<OneCLauncher.DesignerBatchInfo?> WaitForBatchAsync(
        OneCLauncher.DesignerBatchOperation operation,
        string? outputPath,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var tcs = new TaskCompletionSource<OneCLauncher.DesignerBatchInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<OneCLauncher.DesignerBatchInfo> handler = (_, info) =>
        {
            if (info.Operation == operation &&
                string.Equals(info.OutputPath ?? string.Empty, outputPath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(info);
            }
        };
        OneCLauncher.DesignerBatchCompleted += handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
            return completed == tcs.Task ? tcs.Task.Result : null;
        }
        finally
        {
            OneCLauncher.DesignerBatchCompleted -= handler;
        }
    }

    /// <summary>Рекурсивно удаляет каталог; ошибки игнорируются (лучше осиротевший каталог, чем падение).</summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Занят процессом/антивирусом — попытка при следующем запуске очистится сама.
        }
    }
}