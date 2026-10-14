using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты задания по расписанию «Проверить наличие обновления конфигурации» (issue #367):
/// чистая логика сравнения установленных версий с каталогом релизов <c>releases.1c.ru</c>
/// и оркестрация через <see cref="SchedulerService.RunNowAsync"/> на фейках. Паттерн
/// повторяет тесты проверки платформы (issue #366).
/// </summary>
public sealed class ConfigurationUpdateCheckTests
{
    private const string ConfigName = "Бухгалтерия предприятия";

    // ---- Имена конфигураций из списка баз (для выбора в форме) ----

    [Fact]
    public void ConfigurationNames_UniqueSortedTrimmed()
    {
        var bases = new[]
        {
            new Infobase { Name = "b1", ConfigurationName = " ЗУП " },
            new Infobase { Name = "b2", ConfigurationName = "Бухгалтерия предприятия" },
            new Infobase { Name = "b3", ConfigurationName = "зуп" },
            new Infobase { Name = "b4", ConfigurationName = "" },
            new Infobase { Name = "b5", ConfigurationName = "   " },
            new Infobase { Name = "b6" }
        };

        var names = ConfigurationUpdateCheckLogic.ConfigurationNames(bases);

        // Дубликаты схлопываются без учёта регистра, пустые имена отбрасываются.
        Assert.Equal(new[] { "Бухгалтерия предприятия", "ЗУП" }, names);
    }

    [Fact]
    public void ConfigurationNames_EmptyOrNull_EmptyList()
    {
        Assert.Empty(ConfigurationUpdateCheckLogic.ConfigurationNames(null));
        Assert.Empty(ConfigurationUpdateCheckLogic.ConfigurationNames(Array.Empty<Infobase>()));
    }

    // ---- Установленные версии выбранной конфигурации ----

    [Fact]
    public void InstalledVersions_FiltersByConfigurationName()
    {
        var bases = new[]
        {
            new Infobase { Name = "a", ConfigurationName = ConfigName, ConfigurationVersion = "3.0.82.500" },
            new Infobase { Name = "b", ConfigurationName = "другая", ConfigurationVersion = "9.9.9.9" },
            new Infobase { Name = "c", ConfigurationName = "бухгалтерия ПРЕДПРИЯТИЯ", ConfigurationVersion = " 3.0.83.1234 " },
            new Infobase { Name = "d", ConfigurationName = ConfigName, ConfigurationVersion = "" },
            new Infobase { Name = "e", ConfigurationName = ConfigName }
        };

        var versions = ConfigurationUpdateCheckLogic.InstalledVersions(bases, ConfigName);

        // Чужие конфигурации и пустые версии отбрасываются, значения обрезаются.
        Assert.Equal(new[] { "3.0.82.500", "3.0.83.1234" }, versions);
    }

    [Fact]
    public void InstalledVersions_UnknownOrEmptyName_EmptyList()
    {
        var bases = new[] { new Infobase { Name = "a", ConfigurationName = ConfigName, ConfigurationVersion = "1.0" } };

        Assert.Empty(ConfigurationUpdateCheckLogic.InstalledVersions(bases, "нет такой"));
        Assert.Empty(ConfigurationUpdateCheckLogic.InstalledVersions(bases, ""));
        Assert.Empty(ConfigurationUpdateCheckLogic.InstalledVersions(bases, null));
    }

    [Fact]
    public void MaxInstalledVersion_TakesNewestByNumericCompare()
    {
        var bases = new[]
        {
            new Infobase { Name = "a", ConfigurationName = ConfigName, ConfigurationVersion = "3.0.9.1000" },
            new Infobase { Name = "b", ConfigurationName = ConfigName, ConfigurationVersion = "3.0.10.100" }
        };

        // «3.0.10.100» новее «3.0.9.1000»: сравнение числовое, а не лексикографическое.
        Assert.Equal("3.0.10.100", ConfigurationUpdateCheckLogic.MaxInstalledVersion(bases, ConfigName));
    }

    [Fact]
    public void MaxInstalledVersion_NoMatchingBases_Null()
    {
        Assert.Null(ConfigurationUpdateCheckLogic.MaxInstalledVersion(
            new[] { new Infobase { Name = "a", ConfigurationName = "другая", ConfigurationVersion = "1.0" } },
            ConfigName));
        Assert.Null(ConfigurationUpdateCheckLogic.MaxInstalledVersion(null, ConfigName));
    }

    // ---- База-представитель для адреса каталога релизов ----

    [Fact]
    public void PickReferenceBase_PrefersUrlOverrideThenConfigCodeThenFirst()
    {
        var withCode = new Infobase { Name = "code", ConfigurationName = ConfigName, UpdateConfigCode = "Accounting" };
        var withUrl = new Infobase { Name = "url", ConfigurationName = ConfigName, UpdateUrlOverride = "https://releases.1c.ru/project/custom" };
        var plain = new Infobase { Name = "plain", ConfigurationName = ConfigName };

        Assert.Same(withUrl, ConfigurationUpdateCheckLogic.PickReferenceBase(
            new Infobase[] { plain, withCode, withUrl }, ConfigName));
        Assert.Same(withCode, ConfigurationUpdateCheckLogic.PickReferenceBase(
            new Infobase[] { plain, withCode }, ConfigName));
        Assert.Same(plain, ConfigurationUpdateCheckLogic.PickReferenceBase(
            new Infobase[] { plain }, ConfigName));
    }

    [Fact]
    public void PickReferenceBase_UnknownOrNull_Null()
    {
        Assert.Null(ConfigurationUpdateCheckLogic.PickReferenceBase(
            new[] { new Infobase { Name = "a", ConfigurationName = "другая" } }, ConfigName));
        Assert.Null(ConfigurationUpdateCheckLogic.PickReferenceBase(null, ConfigName));
        Assert.Null(ConfigurationUpdateCheckLogic.PickReferenceBase(new[] { new Infobase() }, ""));
    }

    // ---- Сравнение установленной версии с каталогом релизов ----

    [Fact]
    public void Check_NoInstalled_AllCatalogVersionsAvailable()
    {
        var releases = new[]
        {
            new PlatformRelease { Version = "3.0.82.500" },
            new PlatformRelease { Version = "3.0.84.1000" },
            new PlatformRelease { Version = "3.0.83.1234" }
        };

        var result = ConfigurationUpdateCheckLogic.Check(Array.Empty<string>(), releases);

        Assert.Null(result.MaxInstalledVersion);
        Assert.True(result.HasUpdate);
        Assert.False(result.InstalledVersionInCatalog);
        Assert.Equal(new[] { "3.0.84.1000", "3.0.83.1234", "3.0.82.500" }, result.AvailableVersions);
    }

    [Fact]
    public void Check_NewerVersionsOnly_SortedDescending()
    {
        var releases = new[]
        {
            new PlatformRelease { Version = "3.0.82.500" },
            new PlatformRelease { Version = "3.0.84.1000" },
            new PlatformRelease { Version = "3.0.83.1234" }
        };

        var result = ConfigurationUpdateCheckLogic.Check(new[] { "3.0.83.1234" }, releases);

        Assert.Equal("3.0.83.1234", result.MaxInstalledVersion);
        Assert.True(result.HasUpdate);
        Assert.True(result.InstalledVersionInCatalog);
        Assert.Equal(new[] { "3.0.84.1000" }, result.AvailableVersions);
    }

    [Fact]
    public void Check_InstalledIsLatest_NoUpdate()
    {
        var releases = new[]
        {
            new PlatformRelease { Version = "3.0.83.1234" },
            new PlatformRelease { Version = "3.0.82.500" }
        };

        var result = ConfigurationUpdateCheckLogic.Check(new[] { "3.0.83.1234" }, releases);

        Assert.Equal("3.0.83.1234", result.MaxInstalledVersion);
        Assert.False(result.HasUpdate);
        Assert.Empty(result.AvailableVersions);
    }

    [Fact]
    public void Check_InstalledMissingFromCatalog_FlagFalse()
    {
        // Установленная версия может отсутствовать в каталоге (issue #367): это не ошибка,
        // флаг сообщает, что версию не удалось найти среди релизов.
        var releases = new[] { new PlatformRelease { Version = "3.0.84.1000" } };

        var result = ConfigurationUpdateCheckLogic.Check(new[] { "3.0.1.999" }, releases);

        Assert.True(result.HasUpdate);
        Assert.False(result.InstalledVersionInCatalog);
        Assert.Equal(new[] { "3.0.84.1000" }, result.AvailableVersions);
    }

    [Fact]
    public void Check_EmptyCatalog_NoUpdate()
    {
        var result = ConfigurationUpdateCheckLogic.Check(
            new[] { "3.0.83.1234" }, Array.Empty<PlatformRelease>());

        Assert.False(result.HasUpdate);
    }

    [Fact]
    public void Check_DuplicateCatalogVersions_Deduplicated()
    {
        var releases = new[]
        {
            new PlatformRelease { Version = "3.0.84.1000" },
            new PlatformRelease { Version = "3.0.84.1000" }
        };

        var result = ConfigurationUpdateCheckLogic.Check(new[] { "3.0.83.1234" }, releases);

        Assert.Equal(new[] { "3.0.84.1000" }, result.AvailableVersions);
    }

    [Fact]
    public void Check_TestReleases_NotFiltered()
    {
        // В отличие от проверки платформы (issue #366, опция «Учесть тестовые»),
        // для конфигураций флаг тестовости релиза не применяется — как в окне F9.
        var releases = new[]
        {
            new PlatformRelease { Version = "3.0.85.100", IsTest = true },
            new PlatformRelease { Version = "3.0.84.1000", IsTest = false }
        };

        var result = ConfigurationUpdateCheckLogic.Check(new[] { "3.0.83.1234" }, releases);

        Assert.Equal(new[] { "3.0.85.100", "3.0.84.1000" }, result.AvailableVersions);
    }

    // ---- Сводный список версий ----

    [Fact]
    public void FormatVersions_LimitsToListAndEllipsis()
    {
        Assert.Equal("", ConfigurationUpdateCheckLogic.FormatVersions(Array.Empty<string>()));
        Assert.Equal("1, 2", ConfigurationUpdateCheckLogic.FormatVersions(new[] { "1", "2" }));
        Assert.Equal(
            "1, 2, 3, 4, 5… (+2)",
            ConfigurationUpdateCheckLogic.FormatVersions(new[] { "1", "2", "3", "4", "5", "6", "7" }));
    }

    // ---- Оркестрация SchedulerService.RunNowAsync ----

    [Fact]
    public async Task RunNow_NewerVersionAvailable_MainWindowNotification()
    {
        var updates = new StubUpdates(
            new ConfigUpdateCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new List<PlatformRelease>
                {
                    new() { Version = "3.0.84.1000" },
                    new() { Version = "3.0.83.1234" }
                }
            });
        var notifier = new FakeMainWindowNotifier();
        var store = new FakeTaskStore();
        var task = NewConfigCheckTask();
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, updates, notifier);
        var result = await scheduler.RunNowAsync(task);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.True(task.LastRunSuccess);
        // В тестовой среде LocalizationManager не инициализирован и T возвращает ключ,
        // поэтому сверяем именно ключ выбранной ветки сообщения.
        Assert.Equal(LocalizationManager.T("Schedule.ConfigUpdateCheck.Found"), result.ScenarioName);
        Assert.Contains($"{task.Name}: {result.ScenarioName}", notifier.LastMessage, StringComparison.Ordinal);
        Assert.Contains("https://releases.1c.ru/project/buh", updates.LastCatalogUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunNow_UpToDate_ReportsInstalledVersion()
    {
        var updates = new StubUpdates(
            new ConfigUpdateCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new List<PlatformRelease> { new() { Version = "3.0.83.1234" } }
            });
        var notifier = new FakeMainWindowNotifier();
        var store = new FakeTaskStore();
        var task = NewConfigCheckTask();
        // issue #367: сводка «обновлений нет» уведомляется только при включённой
        // опции «Уведомлять об отсутствии».
        task.ConfigCheckNotifyWhenNoUpdates = true;
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, updates, notifier);
        var result = await scheduler.RunNowAsync(task);

        Assert.True(result!.Success);
        Assert.True(task.LastRunSuccess);
        Assert.Equal(LocalizationManager.T("Schedule.ConfigUpdateCheck.UpToDate"), result.ScenarioName);
        Assert.Contains(task.Name, notifier.LastMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunNow_UpToDate_NoNotifyWhenOptionOff()
    {
        // issue #367 (7OH): при отсутствии обновлений и выключенной опции «Уведомлять
        // об отсутствии» пустое уведомление не выводится — итог остаётся в LastRunMessage.
        var updates = new StubUpdates(
            new ConfigUpdateCatalogResult
            {
                Status = PortalFetchStatus.Ok,
                Releases = new List<PlatformRelease> { new() { Version = "3.0.83.1234" } }
            });
        var notifier = new FakeMainWindowNotifier();
        var store = new FakeTaskStore();
        var task = NewConfigCheckTask();
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, updates, notifier);
        var result = await scheduler.RunNowAsync(task);

        Assert.True(result!.Success);
        // Сводка «обновлений нет» осталась в журнале задания, хотя уведомление не показано.
        Assert.Equal(LocalizationManager.T("Schedule.ConfigUpdateCheck.UpToDate"), task.LastRunMessage);
        Assert.Null(notifier.LastMessage);
    }

    [Fact]
    public async Task RunNow_CatalogError_FailsWithMessage()
    {
        var updates = new StubUpdates(
            new ConfigUpdateCatalogResult
            {
                Status = PortalFetchStatus.AuthRequired,
                ErrorKey = "Updates.AuthRequired"
            });
        var notifier = new FakeMainWindowNotifier();
        var store = new FakeTaskStore();
        var task = NewConfigCheckTask();
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, updates, notifier);
        var result = await scheduler.RunNowAsync(task);

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(LocalizationManager.T("Updates.AuthRequired"), result.ErrorMessage);
        Assert.False(task.LastRunSuccess);
        // Ошибка попадает в LastRunMessage задания и в канал уведомления задания.
        Assert.Equal(LocalizationManager.T("Updates.AuthRequired"), task.LastRunMessage);
        Assert.Contains(task.Name, notifier.LastMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunNow_NoUrlConfigured_Fails()
    {
        // База без связки с типовой конфигурацией и без переопределённой ссылки: адрес
        // каталога сформировать нельзя — задача завершается честной ошибкой.
        var updates = new StubUpdates(new ConfigUpdateCatalogResult { Status = PortalFetchStatus.Ok });
        updates.BuildUpdateUrlResult = "";
        var notifier = new FakeMainWindowNotifier();
        var store = new FakeTaskStore();
        var task = NewConfigCheckTask();
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, updates, notifier);
        var result = await scheduler.RunNowAsync(task);

        Assert.False(result!.Success);
        Assert.Equal(LocalizationManager.T("Updates.NoUrl"), result.ErrorMessage);
        Assert.Contains(task.Name, notifier.LastMessage, StringComparison.Ordinal);
        Assert.Null(updates.LastCatalogUrl);
    }

    [Fact]
    public async Task RunNow_EmptyConfigurationName_Fails()
    {
        var updates = new StubUpdates(new ConfigUpdateCatalogResult { Status = PortalFetchStatus.Ok });
        var notifier = new FakeMainWindowNotifier();
        var store = new FakeTaskStore();
        var task = NewConfigCheckTask();
        task.ConfigCheckConfiguration = "  ";
        store.Tasks.Add(task);

        using var scheduler = CreateScheduler(store, updates, notifier);
        var result = await scheduler.RunNowAsync(task);

        Assert.False(result!.Success);
        Assert.Equal(LocalizationManager.T("Schedule.ConfigCheck.ConfigurationRequired"), result.ErrorMessage);
        Assert.Contains(task.Name, notifier.LastMessage, StringComparison.Ordinal);
    }


    // ---- Вспомогательные ----

    private static ScheduledTask NewConfigCheckTask() => new()
    {
        Name = "Проверка обновления конфигурации",
        Kind = ScheduledTaskKind.ConfigurationUpdateCheck,
        ConfigCheckConfiguration = ConfigName,
        ConfigCheckNotifyChannel = PlatformUpdateNotifyChannel.MainWindow
    };

    private static SchedulerService CreateScheduler(
        FakeTaskStore store, StubUpdates updates, FakeMainWindowNotifier notifier) => new(
        store,
        new FakeScenarios(),
        new FakeRepository(),
        null!,
        null!,
        null!,
        new GitHubReleaseService(),
        null!,
        new FakeNotifications(),
        new FakePlatformUpdateService(),
        new FakePlatformVersions(),
        updates,
        new FakeConfigTypes(),
        notifier);

    /// <summary>Фейковое хранилище заданий в памяти (без файловой системы).</summary>
    private sealed class FakeTaskStore : IScheduledTaskStore
    {
        public List<ScheduledTask> Tasks { get; } = new();
        public string TasksDirectory => "";
        public IReadOnlyList<ScheduledTask> LoadAll() => Tasks;
        public void Save(ScheduledTask task) { }
        public void Delete(string id) { }
        public ScheduledTask? Get(string id) => Tasks.FirstOrDefault(t => t.Id == id);
    }

    /// <summary>Фейк списка баз: возвращает заранее заданные записи.</summary>
    private sealed class FakeRepository : IInfobaseRepository
    {
        public List<Infobase> Bases { get; set; } = new()
        {
            new Infobase
            {
                Name = "Бухгалтерия",
                ConfigurationName = ConfigName,
                ConfigurationVersion = "3.0.83.1234",
                UpdateUrlOverride = "https://releases.1c.ru/project/buh"
            }
        };

        public List<Infobase> Load() => Bases;
        public void Save(List<Infobase> infobases) { }
        public Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public List<Group> LoadGroups() => new();
        public void SaveGroups(List<Group> groups) { }
        public Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public AppSettings LoadSettings() => new();
        public void SaveSettings(AppSettings settings) { }
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Фейк сервиса каталога релизов: возвращает заготовленный результат.</summary>
    private sealed class StubUpdates : IOneCUpdatesService
    {
        private readonly ConfigUpdateCatalogResult _catalog;

        public StubUpdates(ConfigUpdateCatalogResult catalog) => _catalog = catalog;

        /// <summary>Адрес, который вернёт <see cref="BuildUpdateUrl"/>.</summary>
        public string BuildUpdateUrlResult { get; set; } = "https://releases.1c.ru/project/buh";

        /// <summary>Последний адрес каталога, запрошенный планировщиком.</summary>
        public string? LastCatalogUrl { get; private set; }

        public string BuildUpdateUrl(OneCConfigType? config, OneCConfigEdition? edition, string? urlOverride, string? urlSegment = null)
            => BuildUpdateUrlResult;

        public Task<ConfigUpdateCatalogResult> GetUpdateCatalogAsync(string url, CancellationToken ct = default)
        {
            LastCatalogUrl = url;
            return Task.FromResult(_catalog);
        }

        public Task<ConfigUpdateCheckResult> CheckForUpdatesAsync(
            string configName, string currentVersion, string url, CancellationToken ct = default, string? familyHint = null)
            => throw new NotSupportedException();

        public Task<string?> DownloadUpdateAsync(string url, string targetPath, IProgress<double>? progress = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string?> DownloadDistributionAsync(string url, string targetPath, IProgress<double>? progress = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<UpdateFileChoice>> GetReleaseFileChoicesAsync(string url, CancellationToken ct = default, string? knownLatestVersion = null)
            => throw new NotSupportedException();

        public Task<string?> GetPageTextAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<PortalPageResult> FetchPageAsync(string url, CancellationToken ct = default) => throw new NotSupportedException();

        public IReadOnlyList<OneCConfigType> BuiltInConfigTypes => Array.Empty<OneCConfigType>();
    }

    /// <summary>Фейк хранилища типовых конфигураций: типовых нет, используется переопределённая ссылка базы.</summary>
    private sealed class FakeConfigTypes : ICustomConfigTypesStore
    {
        public string FilePath => "";
        public IReadOnlyList<OneCConfigType> Load() => Array.Empty<OneCConfigType>();
        public void Save(IReadOnlyCollection<OneCConfigType> types) { }
        public IReadOnlyList<OneCConfigType> LoadAll() => Array.Empty<OneCConfigType>();
        public void RestoreDefaults() { }
    }

    /// <summary>Фейк сценариев резервирования (задание проверки их не использует).</summary>
    private sealed class FakeScenarios : IBackupScenarioStore
    {
        public string ScenariosDirectory => "";
        public IReadOnlyList<BackupScenario> LoadAll() => Array.Empty<BackupScenario>();
        public BackupScenario? Get(string id) => null;
        public void Save(BackupScenario scenario) { }
        public void Delete(string id) { }
    }

    /// <summary>Фейк системных уведомлений — только считает вызовы.</summary>
    private sealed class FakeNotifications : INotificationService
    {
        public int ShowCount { get; private set; }

        public void Show(string title, string message) { ShowCount++; }

        public void Show(string title, string message, NotificationKind kind, NotificationEvent evt = NotificationEvent.ManualTest)
        {
            ShowCount++;
        }
    }

    /// <summary>Фейк каталога платформы (задание проверки конфигурации его не использует).</summary>
    private sealed class FakePlatformUpdateService : IPlatformUpdateService
    {
        public Task<PlatformCatalogResult> GetAvailableReleasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformCatalogResult());

        public Task<PlatformCatalogResult> GetAllAvailableReleasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformCatalogResult());

        public Task<PlatformCatalogResult> GetAvailableReleasesForNickAsync(string nick, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformCatalogResult());

        public Task<PlatformCatalogResult> LoadReleaseFilesAsync(PlatformRelease release, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformCatalogResult());

        public Task<PlatformCatalogResult> LoadReleaseFilesForNickAsync(PlatformRelease release, string nick, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlatformCatalogResult());

        public PlatformReleaseFile? PickDistribution(IReadOnlyList<PlatformReleaseFile> files) => null;
    }

    /// <summary>Фейк сканера установленных версий платформы.</summary>
    private sealed class FakePlatformVersions : IPlatformVersionService
    {
        public List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null) => new();
    }

    /// <summary>Фейк показа сообщения в строке состояния главного окна — запоминает текст.</summary>
    private sealed class FakeMainWindowNotifier : IMainWindowNotifier
    {
        public string? LastMessage { get; private set; }

        public void ShowMessage(string message) => LastMessage = message;
    }
}
