using System;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="ISessionLockService"/>. Установка/снятие блокировки сеансов
/// файловой ИБ выполняется пакетным запуском конфигуратора (<see cref="OneCLauncher.RunDesignerBatch"/>
/// с операциями <c>LockIB</c>/<c>UnlockIB</c>) и ожиданием его завершения через событие
/// <see cref="OneCLauncher.DesignerBatchCompleted"/> (TaskCompletionSource с таймаутом),
/// как и другие пакетные операции конфигуратора.
/// </summary>
public class SessionLockService : ISessionLockService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private readonly IAppLogger? _logger;

    public SessionLockService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SessionLockResult> LockAsync(Infobase infobase, SessionLockOptions options)
    {
        if (infobase is null)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.NoBase"));

        if (options is null)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.Failed"));

        var lockString = options.BuildSessionLockString();

        var started = OneCLauncher.RunDesignerBatch(infobase, OneCLauncher.DesignerBatchOperation.LockIB, lockString);
        if (!started)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.StartFailed"));

        return await WaitForCompletionAsync(OneCLauncher.DesignerBatchOperation.LockIB, lockString);
    }

    /// <inheritdoc />
    public async Task<SessionLockResult> UnlockAsync(Infobase infobase)
    {
        if (infobase is null)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.NoBase"));

        var started = OneCLauncher.RunDesignerBatch(infobase, OneCLauncher.DesignerBatchOperation.UnlockIB, string.Empty);
        if (!started)
            return SessionLockResult.Fail(LocalizationManager.T("SessionLock.StartFailed"));

        return await WaitForCompletionAsync(OneCLauncher.DesignerBatchOperation.UnlockIB, string.Empty);
    }

    /// <summary>
    /// Ожидает завершения операции блокировки через событие
    /// <see cref="OneCLauncher.DesignerBatchCompleted"/> с таймаутом.
    /// </summary>
    private async Task<SessionLockResult> WaitForCompletionAsync(
        OneCLauncher.DesignerBatchOperation operation, string token)
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        var tcs = new TaskCompletionSource<SessionLockResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<OneCLauncher.DesignerBatchInfo> handler = (_, info) =>
        {
            if (info.Operation == operation &&
                string.Equals(info.OutputPath ?? string.Empty, token ?? string.Empty, StringComparison.Ordinal))
            {
                tcs.TrySetResult(info.Success
                    ? SessionLockResult.Ok()
                    : SessionLockResult.Fail(info.ErrorMessage ?? LocalizationManager.T("SessionLock.Failed")));
            }
        };
        OneCLauncher.DesignerBatchCompleted += handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
            return completed == tcs.Task
                ? tcs.Task.Result
                : SessionLockResult.Fail(LocalizationManager.T("SessionLock.Timeout"));
        }
        finally
        {
            OneCLauncher.DesignerBatchCompleted -= handler;
        }
    }
}