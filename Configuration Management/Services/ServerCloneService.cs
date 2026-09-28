using System.IO;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Ошибка операции клонирования клиент-серверной ИБ с человекочитаемым сообщением
/// (текст из лога 1С / код возврата платформы уже замаскирован на уровне OneCLauncher).
/// </summary>
public sealed class ServerCloneException : Exception
{
    /// <summary>
    /// Признак того, что ИБ на сервере могла быть создана до сбоя (например, упал
    /// RestoreIB после успешного CREATEINFOBASE). Безопасного автоудаления серверной
    /// ИБ в приложении нет — вызывающий показывает предупреждение о частичной копии.
    /// </summary>
    public bool InfobasePossiblyCreated { get; }

    public ServerCloneException(string message, bool infobasePossiblyCreated = false)
        : base(message)
    {
        InfobasePossiblyCreated = infobasePossiblyCreated;
    }
}

/// <summary>
/// Оркестратор клонирования клиент-серверной ИБ (0.3.9.100, функция №10):
/// конвейер «выгрузка → создание → загрузка» через существующие примитивы
/// <see cref="OneCLauncher"/> (DumpIB/DumpCfg/RestoreIB + CreateInfoBase).
/// Временный каталог <c>%TEMP%\cm_clonesrv_<guid></c> гарантированно удаляется
/// в <c>finally</c>; при сбое ПОСЛЕ создания ИБ запись не удаляется автоматически
/// (предупреждение формирует вызывающий). Основной образец —
/// <see cref="ConfigurationDiffService"/>.
/// </summary>
public sealed class ServerCloneService
{
    /// <summary>Таймаут ожидания одного запуска 1cv8 DESIGNER (DumpIB/DumpCfg/RestoreIB).</summary>
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromMinutes(60);

    /// <summary>Таймаут создания ИБ на сервере (CREATEINFOBASE, большие конфигурации).</summary>
    private const int CreateInfoBaseTimeoutMs = 30 * 60 * 1000;

    /// <summary>
    /// Выполняет клонирование клиент-серверной ИБ. По успеху возвращает подключение
    /// клона (<see cref="ConnectionSettings"/>) — запись в списке собирает вызывающий.
    /// </summary>
    public Task<ConnectionSettings> CloneAsync(
        ServerCloneRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = ServerClonePlanner.Validate(request);
        if (validation != ServerCloneValidationError.None)
        {
            throw new ServerCloneException(
                validation == ServerCloneValidationError.NoPlatform
                    ? LocalizationManager.T("CloneServer.ErrNoPlatform")
                    : LocalizationManager.T("CloneServer.ErrInvalidRequest"));
        }

        return Task.Run(() =>
        {
            var tmpRoot = Path.Combine(Path.GetTempPath(), "cm_clonesrv_" + Guid.NewGuid().ToString("N"));
            try
            {
                var mode = request.Mode;
                var dumpOp = mode == ServerCloneMode.ConfigurationOnly
                    ? OneCLauncher.DesignerBatchOperation.DumpCfg
                    : OneCLauncher.DesignerBatchOperation.DumpIB;
                var dumpFile = Path.Combine(
                    tmpRoot,
                    mode == ServerCloneMode.ConfigurationOnly ? "config.cf" : "clone.dt");

                // ---- Этап 1: выгрузка исходной базы (.dt — данные+конфигурация, .cf — конфигурация) ----
                progress?.Report(LocalizationManager.T("CloneServer.StageDump"));
                cancellationToken.ThrowIfCancellationRequested();

                var started = OneCLauncher.RunDesignerBatch(request.Source, dumpOp, dumpFile);
                if (!started)
                    throw new ServerCloneException(LocalizationManager.T("CloneServer.ErrOperationBlocked"));

                var dumpInfo = WaitForBatchAsync(dumpOp, dumpFile, BatchTimeout).GetAwaiter().GetResult();
                if (dumpInfo is null)
                    throw new ServerCloneException(LocalizationManager.T("CloneServer.ErrTimeoutFormat"));
                if (!dumpInfo.Success)
                {
                    throw new ServerCloneException(string.Format(
                        LocalizationManager.T("CloneServer.ErrDumpFailedFormat"),
                        dumpInfo.ErrorMessage ?? string.Empty));
                }

                // ---- Этап 2: создание ИБ на целевом сервере.
                // Полный режим — пустая база; «только конфигурация» — /UseTemplate"…\config.cf"
                // (создание и загрузка конфигурации одним запуском). ----
                progress?.Report(LocalizationManager.T("CloneServer.StageCreate"));
                cancellationToken.ThrowIfCancellationRequested();

                var (ok, error) = OneCLauncher.CreateInfoBase(
                    platformVersion: request.PlatformVersion,
                    isFile: false,
                    filePath: null,
                    server: request.Server,
                    databaseName: request.DatabaseName,
                    templatePath: mode == ServerCloneMode.ConfigurationOnly ? dumpFile : null,
                    dbms: request.Dbms,
                    dbServer: request.DbServer,
                    dbName: request.DbName,
                    dbUser: request.DbUser,
                    dbPassword: request.DbPassword,
                    createSqlDatabase: request.CreateSqlDatabase,
                    blockScheduledJobs: request.BlockScheduledJobs,
                    timeoutMs: CreateInfoBaseTimeoutMs);
                if (!ok)
                {
                    // Сбой на создании: серверная ИБ НЕ создана (или создана частично самой
                    // платформой); автоудаления нет — сообщение об ошибке с текстом платформы.
                    throw new ServerCloneException(string.Format(
                        LocalizationManager.T("CloneServer.ErrCreateFailedFormat"),
                        error ?? string.Empty));
                }

                // ---- Этап 3 (только полный режим): восстановление данных в копию. ----
                if (mode == ServerCloneMode.FullCopy)
                {
                    progress?.Report(LocalizationManager.T("CloneServer.StageRestore"));
                    cancellationToken.ThrowIfCancellationRequested();

                    var target = BuildTargetInfobase(request);
                    var restoreStarted = OneCLauncher.RunDesignerBatch(
                        target, OneCLauncher.DesignerBatchOperation.RestoreIB, dumpFile);
                    if (!restoreStarted)
                        throw new ServerCloneException(LocalizationManager.T("CloneServer.ErrOperationBlocked"));

                    var restoreInfo = WaitForBatchAsync(
                        OneCLauncher.DesignerBatchOperation.RestoreIB,
                        dumpFile,
                        BatchTimeout).GetAwaiter().GetResult();
                    if (restoreInfo is null)
                        throw new ServerCloneException(LocalizationManager.T("CloneServer.ErrTimeoutFormat"));
                    if (!restoreInfo.Success)
                    {
                        // Сбой на восстановлении: ИБ на сервере уже создана (возможно, без
                        // данных) — предупреждение о частично созданной копии формирует
                        // вызывающий (без автоудаления: безопасного механизма нет).
                        throw new ServerCloneException(string.Format(
                            LocalizationManager.T("CloneServer.ErrRestoreFailedFormat"),
                            restoreInfo.ErrorMessage ?? string.Empty),
                            infobasePossiblyCreated: true);
                    }
                }

                progress?.Report(LocalizationManager.T("CloneServer.StageDone"));

                var connection = new ConnectionSettings
                {
                    Type = ConnectionType.ClientServer,
                    BlockScheduledJobs = request.BlockScheduledJobs
                };
                // «host:port» разбирается на Server и Port (как при импорте из ibases.v8i).
                ConnectionSettings.ParseServerAndPort(request.Server, connection);
                return connection;
            }
            finally
            {
                TryDeleteDirectory(tmpRoot);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Собирает временный объект <see cref="Infobase"/> для приёмника (RestoreIB):
    /// клиент-серверная ИБ на целевом сервере. Для только что созданной базы
    /// блокировок и сеансов нет — <c>IsDesignerBlocked</c> сработает корректно.
    /// </summary>
    private static Infobase BuildTargetInfobase(ServerCloneRequest request)
        => new()
        {
            Name = request.CloneName,
            PlatformVersion = request.PlatformVersion,
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = request.Server,
                DatabaseName = request.DatabaseName
            }
        };

    /// <summary>
    /// Ожидает завершения конкретной пакетной операции DESIGNER через событие
    /// <see cref="OneCLauncher.DesignerBatchCompleted"/> с таймаутом (копия паттерна
    /// <c>ConfigurationDiffService.WaitForBatchAsync</c>).
    /// </summary>
    private static async Task<OneCLauncher.DesignerBatchInfo?> WaitForBatchAsync(
        OneCLauncher.DesignerBatchOperation operation,
        string outputPath,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var tcs = new TaskCompletionSource<OneCLauncher.DesignerBatchInfo?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

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