using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты ViewModel окна «Скачивание версии платформы 1С» (issue #330):
/// получение каталога версий, выбор файла под разрядность/тип, учётная запись ИТС
/// (выбранная/основная), скачивание в целевую папку с прогрессом и ИТОГ БЕЗ
/// автоматической установки (установщик вызывается только явной командой).
/// fake-сервисы без сети и UI.
/// </summary>
public sealed class PlatformDownloadViewModelTests
{
    private static PlatformRelease Release(string version, params PlatformReleaseFile[] files)
    {
        var release = new PlatformRelease { Version = version };
        release.Files.AddRange(files);
        return release;
    }

    private static PlatformReleaseFile File(string name, string? arch, PlatformDistributionKind kind)
        => new()
        {
            FileName = name,
            Url = $"https://releases.1c.ru/dist/{name}",
            Architecture = arch,
            Kind = kind,
            SizeBytes = 100,
        };

    private static PlatformDownloadViewModel CreateVm(
        FakeCatalogService? service = null,
        ItsAccount? account = null,
        Func<string, string, IProgress<double>?, CancellationToken, Task<string?>>? download = null,
        Action<string>? onRunInstaller = null,
        string? directory = null,
        bool is64Bit = true,
        bool isWindows = true)
    {
        var dir = directory ?? Path.Combine(Path.GetTempPath(), "cm_platformdl_" + Guid.NewGuid().ToString("N"));
        return new PlatformDownloadViewModel(
            service ?? new FakeCatalogService(),
            () => account,
            download ?? ((url, target, progress, ct) => Task.FromResult<string?>(target)),
            _ => true,
            path => { onRunInstaller?.Invoke(path); return true; },
            is64Bit: is64Bit,
            defaultDirectory: dir,
            isWindows: isWindows);
    }

    // ---------- Каталог и выбор файла ----------

    [Fact]
    public async Task LoadCatalogAsync_FillsReleases_AndPicksFirstFile()
    {
        var fileX64 = File("8.3.27.2214_x64.zip", "x64", PlatformDistributionKind.WindowsSetupZip);
        var service = new FakeCatalogService
        {
            AvailableResult = new PlatformCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new[]
                {
                    Release("8.3.27.2214", fileX64, File("8.3.27.2214_x86.zip", "x86", PlatformDistributionKind.WindowsSetupZip)),
                    Release("8.3.27.1688"),
                },
            },
        };
        var vm = CreateVm(service);

        await vm.LoadCatalogAsync();

        Assert.Equal(2, vm.Releases.Count);
        Assert.Equal("8.3.27.2214", vm.SelectedRelease!.Version);
        Assert.NotNull(vm.PickedFile);
        Assert.Equal("8.3.27.2214_x64.zip", vm.PickedFile!.FileName);
        Assert.Contains("8.3.27.2214", vm.PickedFile.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwitchTo32Bit_PicksX86File()
    {
        var fileX64 = File("8.3.27.2214_x64.zip", "x64", PlatformDistributionKind.WindowsSetupZip);
        var fileX86 = File("8.3.27.2214_x86.zip", "x86", PlatformDistributionKind.WindowsSetupZip);
        var service = new FakeCatalogService
        {
            AvailableResult = new PlatformCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new[] { Release("8.3.27.2214", fileX64, fileX86) },
            },
        };
        var vm = CreateVm(service, is64Bit: true);

        await vm.LoadCatalogAsync();
        Assert.NotNull(vm.PickedFile);
        Assert.Equal("8.3.27.2214_x64.zip", vm.PickedFile!.FileName);

        vm.Is64Bit = false;
        Assert.Equal("8.3.27.2214_x86.zip", vm.PickedFile!.FileName);
    }

    [Fact]
    public async Task ThinClientType_PicksThinZip()
    {
        var fileFull = File("8.3.27.2214_x64.zip", "x64", PlatformDistributionKind.WindowsSetupZip);
        var fileThin = File("8.3.27.2214_thin_1c_x64.zip", "x64", PlatformDistributionKind.WindowsSetupZip);
        var service = new FakeCatalogService
        {
            AvailableResult = new PlatformCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new[] { Release("8.3.27.2214", fileFull, fileThin) },
            },
        };
        var vm = CreateVm(service);

        await vm.LoadCatalogAsync();
        vm.DownloadType = PlatformDownloadType.ThinClient;

        Assert.NotNull(vm.PickedFile);
        Assert.Contains("thin", vm.PickedFile!.FileName, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Учётная запись ИТС ----------

    [Fact]
    public void ResolveAccount_SetsNameAndFlag()
    {
        var account = new ItsAccount { Id = "a1", Name = "Основная", Login = "user@mail.ru", IsPrimary = true };
        var vm = CreateVm(account: account);

        Assert.True(vm.HasAccount);
        Assert.Equal("Основная", vm.AccountName);
    }

    [Fact]
    public void ResolveAccount_NullAccount_NoFlag()
    {
        var vm = CreateVm(account: null);

        Assert.False(vm.HasAccount);
        Assert.Empty(vm.AccountName);
    }

    [Fact]
    public async Task DownloadAsync_WithoutAccount_WarnsInLog()
    {
        var service = new FakeCatalogService
        {
            AvailableResult = new PlatformCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new[]
                {
                    Release("8.3.27.2214", File("8.3.27.2214_x64.zip", "x64", PlatformDistributionKind.WindowsSetupZip)),
                },
            },
        };
        var vm = CreateVm(service, account: null);

        await vm.LoadCatalogAsync();
        await vm.DownloadAsync();

        Assert.Contains(LocalizationManager.T("PlatformDownload.WarnNoAccount"), vm.LogText);
        Assert.True(vm.HasDownloaded);
    }

    // ---------- Скачивание ----------

    [Fact]
    public async Task DownloadAsync_SavesToTargetDirectory_SetsDownloadedPath()
    {
        var service = new FakeCatalogService
        {
            AvailableResult = new PlatformCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new[]
                {
                    Release("8.3.27.2214", File("8.3.27.2214_x64.zip", "x64", PlatformDistributionKind.WindowsSetupZip)),
                },
            },
        };
        var dir = Path.Combine(Path.GetTempPath(), "cm_dl_" + Guid.NewGuid().ToString("N"));
        var vm = CreateVm(service, account: new ItsAccount { Name = "ИТС", Login = "login" }, directory: dir);

        await vm.LoadCatalogAsync();
        Assert.True(vm.DownloadCommand.CanExecute(null));

        await vm.DownloadAsync();

        Assert.False(string.IsNullOrWhiteSpace(vm.DownloadedPath));
        Assert.StartsWith(dir, vm.DownloadedPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.HasDownloaded);
        Assert.False(string.IsNullOrWhiteSpace(vm.ResultText));
        Assert.Contains("8.3.27.2214_x64.zip", vm.DownloadedPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadAsync_DoesNotRunInstallerAutomatically()
    {
        var installerCalls = new List<string>();
        var service = new FakeCatalogService
        {
            AvailableResult = new PlatformCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new[]
                {
                    Release("8.3.27.2214", File("8.3.27.2214_x64.zip", "x64", PlatformDistributionKind.WindowsSetupZip)),
                },
            },
        };
        var vm = CreateVm(service, account: new ItsAccount { Name = "ИТС", Login = "login" },
            onRunInstaller: path => installerCalls.Add(path));

        await vm.LoadCatalogAsync();
        await vm.DownloadAsync();

        // Установщик не вызывается автоматически (требование issue #330).
        Assert.Empty(installerCalls);

        vm.RunInstaller();
        Assert.Single(installerCalls);
        Assert.Equal(vm.DownloadedPath, installerCalls[0]);
    }

    [Fact]
    public async Task DownloadAsync_ProgressReported()
    {
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeCatalogService
        {
            AvailableResult = new PlatformCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new[]
                {
                    Release("8.3.27.2214", File("8.3.27.2214_x64.zip", "x64", PlatformDistributionKind.WindowsSetupZip)),
                },
            },
        };
        var vm = CreateVm(service,
            account: new ItsAccount { Name = "ИТС", Login = "login" },
            download: (url, target, progress, ct) =>
            {
                progress?.Report(0.5);
                return gate.Task;
            });

        await vm.LoadCatalogAsync();
        var operation = vm.DownloadAsync();

        // Progress<T> может доставлять отчёт асинхронно (захваченный SynchronizationContext
        // в среде тестов), поэтому ждём достижения 0.5, а не полагаемся на синхронность.
        var reached = await WaitUntilAsync(() => vm.Progress >= 0.5, TimeSpan.FromSeconds(5));
        Assert.True(reached, "Прогресс не дошёл до 0.5 во время загрузки");

        gate.SetResult(Path.Combine(vm.TargetDirectory, "8.3.27.2214_8.3.27.2214_x64.zip"));
        await operation;

        Assert.Equal(1.0, vm.Progress, 3);
        Assert.True(vm.HasDownloaded);
    }

    [Fact]
    public async Task AppendLog_FromBackgroundThread_RaisesPropertyChangedWithoutException()
    {
        // Регрессия issue #330: AppendLog вызывается из фоновых задач (LoadCatalogAsync
        // использует ConfigureAwait(false)), и обработчики UI получают уведомление на
        // фоновом потоке. Контракт VM: уведомление поднимается, исключений не бросается —
        // потокозависимые UI-действия (ScrollToEnd) выполняет само окно через Dispatcher.
        var vm = CreateVm();
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        await Task.Run(() => vm.AppendLog("Фоновая строка журнала"));

        Assert.Contains(nameof(PlatformDownloadViewModel.LogText), notifications);
        Assert.Contains("Фоновая строка журнала", vm.LogText);
    }

    // ---------- Fake-сервис ----------

    /// <summary>Ждёт выполнения условия с таймаутом (для асинхронных отчётов прогресса).</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > timeout)
                return false;
            await Task.Delay(20);
        }

        return true;
    }

    private sealed class FakeCatalogService : IPlatformUpdateService
    {
        public PlatformCatalogResult AvailableResult { get; set; } = new() { Status = PortalFetchStatus.Ok };

        public Task<PlatformCatalogResult> GetAvailableReleasesAsync(CancellationToken ct = default)
            => Task.FromResult(AvailableResult);

        public Task<PlatformCatalogResult> LoadReleaseFilesAsync(PlatformRelease release, CancellationToken ct = default)
            => Task.FromResult(new PlatformCatalogResult { Status = PortalFetchStatus.Ok, Release = release });

        public PlatformReleaseFile? PickDistribution(IReadOnlyList<PlatformReleaseFile> files)
            => files.FirstOrDefault();
    }
}