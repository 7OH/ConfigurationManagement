using System.IO;
using System.Diagnostics;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>Режим сравнения конфигураций.</summary>
public enum ConfigDiffMode
{
    /// <summary>Конфигурация реальной базы сравнивается с эталонным файлом .cf.</summary>
    BaseVsCf,

    /// <summary>Сравниваются два файла .cf между собой.</summary>
    CfVsCf
}

/// <summary>Параметры операции сравнения конфигураций.</summary>
public sealed class ConfigurationDiffRequest
{
    /// <summary>Режим сравнения.</summary>
    public required ConfigDiffMode Mode { get; init; }

    /// <summary>База для режима <see cref="ConfigDiffMode.BaseVsCf"/> (левая сторона).</summary>
    public Infobase? Base { get; init; }

    /// <summary>Левый файл .cf (режим <see cref="ConfigDiffMode.CfVsCf"/>).</summary>
    public string? LeftCfPath { get; init; }

    /// <summary>Правый файл .cf (оба режима).</summary>
    public string? RightCfPath { get; init; }

    /// <summary>Версия платформы 1С для создания временных ИБ из .cf.</summary>
    public required string PlatformVersion { get; init; }

    /// <summary>Подпись левой стороны для отчёта.</summary>
    public required string LeftLabel { get; init; }

    /// <summary>Подпись правой стороны для отчёта.</summary>
    public required string RightLabel { get; init; }
}

/// <summary>Ошибка операции сравнения конфигураций с человекочитаемым сообщением.</summary>
public sealed class ConfigurationDiffException : Exception
{
    public ConfigurationDiffException(string message) : base(message) { }
}

/// <summary>
/// Оркестратор сравнения конфигураций (0.3.9.99, функция №9): конвейер
/// «выгрузка → XML-файлы» для обоих режимов («База ↔ .cf» и «.cf ↔ .cf»).
/// Каждый .cf распаковывается во временную файловую ИБ (<see cref="OneCLauncher.CreateInfoBase"/>
/// с <c>/UseTemplate</c>), из временной ИБ и реальной базы конфигурация выгружается
/// ключом <c>/DumpConfigToFiles</c>, затем деревья сравниваются чистым движком
/// <see cref="ConfigurationDiffEngine"/>. Временный каталог удаляется в <c>finally</c>.
/// </summary>
public sealed class ConfigurationDiffService
{
    /// <summary>Таймаут ожидания одного запуска 1cv8 DESIGNER /DumpConfigToFiles.</summary>
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromMinutes(60);

    /// <summary>Таймаут создания временной ИБ из .cf (большие конфигурации).</summary>
    private const int CreateInfoBaseTimeoutMs = 30 * 60 * 1000;

    /// <summary>
    /// Выполняет сравнение: готовит снимки обеих сторон и возвращает отчёт.
    /// Временный каталог <c>%TEMP%\cm_configdiff_<guid></c> гарантированно удаляется.
    /// </summary>
    public Task<ConfigurationDiffResult> CompareAsync(
        ConfigurationDiffRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.PlatformVersion))
            throw new ConfigurationDiffException(LocalizationManager.T("ConfigDiff.ErrNoPlatform"));

        return Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var tmpRoot = Path.Combine(Path.GetTempPath(), "cm_configdiff_" + Guid.NewGuid().ToString("N"));
            try
            {
                ConfigurationSnapshot left = request.Mode switch
                {
                    ConfigDiffMode.BaseVsCf => PrepareSnapshotFromBase(request.Base, tmpRoot, "L", progress, cancellationToken),
                    _ => PrepareSnapshotFromCf(request.LeftCfPath!, request.PlatformVersion, tmpRoot, "L", progress, cancellationToken)
                };

                progress?.Report(LocalizationManager.T("ConfigDiff.StageDumpRight"));
                var right = PrepareSnapshotFromCf(request.RightCfPath!, request.PlatformVersion, tmpRoot, "R", progress, cancellationToken);

                progress?.Report(LocalizationManager.T("ConfigDiff.StageCompare"));
                return ConfigurationDiffEngine.Compare(left, right, request.LeftLabel, request.RightLabel, stopwatch.Elapsed);
            }
            finally
            {
                TryDeleteDirectory(tmpRoot);
            }
        }, cancellationToken);
    }

    /// <summary>Готовит снимок из .cf: CREATEINFOBASE (с /UseTemplate) + DumpConfigToFiles.</summary>
    private static ConfigurationSnapshot PrepareSnapshotFromCf(
        string cfPath,
        string platformVersion,
        string tmpRoot,
        string side,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(cfPath) || !File.Exists(cfPath))
            throw new ConfigurationDiffException(LocalizationManager.T("ConfigDiff.ErrCfNotExists"));

        var ibDir = Path.Combine(tmpRoot, "ib" + side);
        var dumpDir = Path.Combine(tmpRoot, "dump" + side);

        progress?.Report(LocalizationManager.T("ConfigDiff.StageCreateTemp"));
        var created = OneCLauncher.CreateInfoBase(
            platformVersion,
            isFile: true,
            filePath: ibDir,
            server: null,
            databaseName: null,
            templatePath: cfPath,
            timeoutMs: CreateInfoBaseTimeoutMs);
        if (!created.Ok)
            throw new ConfigurationDiffException(created.Error ?? LocalizationManager.T("ConfigDiff.ErrOperationFailed"));

        return DumpAndSnapshot(CreateTempBase(platformVersion, ibDir, side), dumpDir, progress, cancellationToken);
    }

    /// <summary>Готовит снимок из реальной базы: сразу DumpConfigToFiles.</summary>
    private static ConfigurationSnapshot PrepareSnapshotFromBase(
        Infobase? infobase,
        string tmpRoot,
        string side,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (infobase is null)
            throw new ConfigurationDiffException(LocalizationManager.T("ConfigDiff.ErrNoBase"));

        var dumpDir = Path.Combine(tmpRoot, "dump" + side);
        return DumpAndSnapshot(infobase, dumpDir, progress, cancellationToken);
    }

    /// <summary>Запускает выгрузку конфигурации в каталог и строит снимок.</summary>
    private static ConfigurationSnapshot DumpAndSnapshot(
        Infobase infobase,
        string dumpDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Каталог выгрузки создаёт сам RunDesignerBatch (ветка DumpConfigToFiles),
        // но здесь подстраховываемся: он должен существовать до запуска.
        try { Directory.CreateDirectory(dumpDir); } catch { /* создаст RunDesignerBatch */ }

        var started = OneCLauncher.RunDesignerBatch(
            infobase,
            OneCLauncher.DesignerBatchOperation.DumpConfigToFiles,
            dumpDir);
        if (!started)
            throw new ConfigurationDiffException(LocalizationManager.T("ConfigDiff.ErrOperationFailed"));

        var info = WaitForBatchAsync(
            OneCLauncher.DesignerBatchOperation.DumpConfigToFiles,
            dumpDir,
            BatchTimeout).GetAwaiter().GetResult();

        if (info is null)
            throw new ConfigurationDiffException(LocalizationManager.T("ConfigDiff.ErrTimeout"));
        if (!info.Success)
            throw new ConfigurationDiffException(info.ErrorMessage ?? LocalizationManager.T("ConfigDiff.ErrOperationFailed"));

        return ConfigurationDiffEngine.BuildSnapshot(dumpDir);
    }

    /// <summary>Собирает Infobase, указывающую на временную файловую ИБ.</summary>
    private static Infobase CreateTempBase(string platformVersion, string ibDir, string side)
        => new()
        {
            Name = $"cm_configdiff_{side}",
            PlatformVersion = platformVersion,
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = ibDir
            }
        };

    /// <summary>
    /// Ожидает завершения конкретной пакетной операции DESIGNER через событие
    /// <see cref="OneCLauncher.DesignerBatchCompleted"/> с таймаутом (копия паттерна
    /// <c>BackupService.WaitForCompletionAsync</c>).
    /// </summary>
    private static async Task<OneCLauncher.DesignerBatchInfo?> WaitForBatchAsync(
        OneCLauncher.DesignerBatchOperation operation,
        string outputPath,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var tcs = new TaskCompletionSource<OneCLauncher.DesignerBatchInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<OneCLauncher.DesignerBatchInfo> handler = (_, info) =>
        {
            if (info.Operation == operation &&
                string.Equals(info.OutputPath, outputPath, StringComparison.OrdinalIgnoreCase))
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