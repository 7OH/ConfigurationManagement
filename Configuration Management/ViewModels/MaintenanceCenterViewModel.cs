using System.Collections.ObjectModel;
using System.ComponentModel;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка дашборда «Центр обслуживания» (0.3.9.89): сводка состояния одной базы
/// по колонкам «Доступность / последняя копия / размер ИБ / кэш / конфигурация /
/// возраст данных / проверка обновлений». Чистый .NET без платформенных
/// зависимостей — подключается в Linux-сборку явно.
/// </summary>
public sealed class MaintenanceCenterRowViewModel : ViewModelBase, IDisposable
{
    /// <summary>Возраст резервной копии, после которого база попадает в «Только проблемы».</summary>
    public static readonly TimeSpan BackupMaxAge = TimeSpan.FromDays(7);

    /// <summary>Размер кэша 1С, после которого база попадает в «Только проблемы» (1 ГБ).</summary>
    public const long CacheProblemBytes = 1024L * 1024 * 1024;

    private readonly Infobase _infobase;
    private readonly int _freeSpaceWarningGb;
    private DateTime? _lastCheckedAt;

    /// <param name="infobase">Информационная база.</param>
    /// <param name="updateResult">Последний результат проверки обновлений конфигурации
    /// (из UpdateCheckCache по UpdateConfigCode; null — проверка не выполнялась).</param>
    /// <param name="freeSpaceWarningGb">Порог предупреждения «Свободно на диске» в ГБ
    /// (0 — не предупреждать). По умолчанию — 10 ГБ (0.3.9.96).</param>
    public MaintenanceCenterRowViewModel(
        Infobase infobase,
        ConfigUpdateCheckResult? updateResult,
        int freeSpaceWarningGb = DiskFreeSpaceHelper.DefaultWarningGb)
    {
        _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));
        _freeSpaceWarningGb = Math.Max(0, freeSpaceWarningGb);
        UpdateResult = updateResult;
        _infobase.PropertyChanged += OnInfobasePropertyChanged;
        RefreshFromInfobase();
    }

    /// <summary>База, для которой построена строка.</summary>
    public Infobase Infobase => _infobase;

    /// <summary>Результат последней проверки обновлений конфигурации (по UpdateConfigCode).</summary>
    public ConfigUpdateCheckResult? UpdateResult { get; }

    /// <summary>Имя базы.</summary>
    public string Name => _infobase.Name;

    /// <summary>Пояснение под именем: группа и сервер/база.</summary>
    public string Subtitle { get; private set; } = string.Empty;

    /// <summary>Доступность базы (расчётная либо результат ручной проверки).</summary>
    public bool IsAvailable => _infobase.IsAvailable;

    /// <summary>Колонка «Доступность»: статус и время последней проверки (если была).</summary>
    public string AvailabilityText { get; private set; } = string.Empty;

    /// <summary>Колонка «Последняя копия» (значение из 0.3.9.86).</summary>
    public string LastBackupText { get; private set; } = string.Empty;

    /// <summary>Колонка «Размер ИБ».</summary>
    public string SizeText { get; private set; } = string.Empty;

    /// <summary>Колонка «Кэш» (обновляется фоновым RefreshCacheSizeAsync при открытии окна).</summary>
    public string CacheText { get; private set; } = string.Empty;

    /// <summary>Колонка «Конфигурация / версия».</summary>
    public string ConfigurationText { get; private set; } = string.Empty;

    /// <summary>Колонка «Возраст данных» (FileLastWriteTimeUtc + возраст в днях).</summary>
    public string DataAgeText { get; private set; } = string.Empty;

    /// <summary>Колонка «Обновление» — последний результат проверки из UpdateCheckCache.</summary>
    public string UpdateText { get; private set; } = string.Empty;

    /// <summary>Колонка «Свободно на диске» (0.3.9.96): «C:\ — 23,4 ГБ (12%)» для
    /// файловых баз, «—» для клиент-серверных/веб-баз и недоступных дисков.</summary>
    public string FreeSpaceDisplay { get; private set; } = string.Empty;

    /// <summary>True — свободное место на диске файловой базы ниже порога
    /// MaintenanceFreeSpaceWarningGb (0 — предупреждение отключено).</summary>
    public bool FreeSpaceIsProblem { get; private set; }

    /// <summary>True — база попадает под фильтр «Только проблемы».</summary>
    public bool HasProblem { get; private set; }

    /// <inheritdoc />
    public void Dispose() => _infobase.PropertyChanged -= OnInfobasePropertyChanged;

    private void OnInfobasePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Ручная проверка доступности меняет IsAvailable (SetCheckedAvailability) —
        // фиксируем время последней проверки для колонки «Доступность».
        if (e.PropertyName == nameof(Infobase.IsAvailable))
            _lastCheckedAt = DateTime.Now;
        RefreshFromInfobase();
    }

    private void RefreshFromInfobase()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_infobase.Group))
            parts.Add(_infobase.Group);
        var server = _infobase.ServerDatabaseDisplay;
        if (!string.IsNullOrWhiteSpace(server) && server != "—")
            parts.Add(server);
        Subtitle = string.Join("  •  ", parts);

        var available = LocalizationManager.T(
            _infobase.IsAvailable ? "Maintenance.Available" : "Maintenance.Unavailable");
        AvailabilityText = _lastCheckedAt.HasValue
            ? $"{available} · {_lastCheckedAt.Value:HH:mm:ss}"
            : available;

        LastBackupText = _infobase.LastBackupDisplay;
        SizeText = _infobase.FileSizeDisplay;
        CacheText = _infobase.CacheSizeDisplay;

        var config = (_infobase.ConfigurationName ?? string.Empty).Trim();
        var version = (_infobase.ConfigurationVersion ?? string.Empty).Trim();
        ConfigurationText = config.Length == 0 && version.Length == 0
            ? "—"
            : string.Join(" ", new[] { config, version }.Where(s => s.Length > 0));

        DataAgeText = FormatDataAge(_infobase.FileLastWriteTimeUtc);
        UpdateText = FormatUpdate(UpdateResult);

        // Свободное место на диске (0.3.9.96): только для файловых баз с путём.
        // Ошибки резолвера (недоступный сетевой диск, исключения DriveInfo) —
        // тихая деградация в «—».
        if (_infobase.Connection.Type == ConnectionType.File
            && !string.IsNullOrWhiteSpace(_infobase.Connection.FilePath))
        {
            var driveName = DiskFreeSpaceHelper.ResolveDriveName(_infobase.Connection.FilePath);
            var info = DiskFreeSpaceHelper.TryGetInfo(
                _infobase.Connection.FilePath, DiskFreeSpaceHelper.DefaultDriveResolver);
            FreeSpaceDisplay = driveName is null || info is null
                ? "—"
                : DiskFreeSpaceHelper.FormatDisplay(driveName, info);
            FreeSpaceIsProblem = info is not null
                && DiskFreeSpaceHelper.IsWarning(info.FreeBytes, _freeSpaceWarningGb);
        }
        else
        {
            FreeSpaceDisplay = "—";
            FreeSpaceIsProblem = false;
        }

        var backupTooOld = !_infobase.LastBackupUtc.HasValue
            || DateTime.UtcNow - _infobase.LastBackupUtc.Value > BackupMaxAge;
        var cacheTooBig = _infobase.CacheSizeBytes > CacheProblemBytes;
        HasProblem = !IsAvailable || backupTooOld || cacheTooBig
            || FreeSpaceIsProblem || UpdateResult?.HasNewer == true;

        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(LastBackupText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(CacheText));
        OnPropertyChanged(nameof(ConfigurationText));
        OnPropertyChanged(nameof(DataAgeText));
        OnPropertyChanged(nameof(UpdateText));
        OnPropertyChanged(nameof(FreeSpaceDisplay));
        OnPropertyChanged(nameof(FreeSpaceIsProblem));
        OnPropertyChanged(nameof(HasProblem));
    }

    private static string FormatDataAge(DateTime? lastWriteUtc)
    {
        if (!lastWriteUtc.HasValue)
            return "—";
        var local = lastWriteUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        var age = (int)Math.Round((DateTime.UtcNow - lastWriteUtc.Value).TotalDays);
        return age <= 0
            ? $"{local} · {LocalizationManager.T("Maintenance.Today")}"
            : $"{local} · {age} {LocalizationManager.T("Maintenance.DaysAgo")}";
    }

    private static string FormatUpdate(ConfigUpdateCheckResult? result)
    {
        if (result is null)
            return "—";
        return result.Status switch
        {
            ConfigUpdateStatus.UpToDate => LocalizationManager.T("Updates.UpToDate"),
            ConfigUpdateStatus.NewerAvailable => string.IsNullOrWhiteSpace(result.LatestVersion)
                ? LocalizationManager.T("Updates.NewerAvailable")
                : $"{LocalizationManager.T("Updates.NewerAvailable")} · {result.LatestVersion}",
            ConfigUpdateStatus.Unavailable => LocalizationManager.T("Updates.Unavailable"),
            ConfigUpdateStatus.Failed => LocalizationManager.T("Updates.CheckFailed"),
            _ => "—"
        };
    }
}

/// <summary>
/// ViewModel окна «Центр обслуживания» (0.3.9.89): таблица всех баз со сводкой
/// состояния (доступность, последняя копия, размер, кэш, конфигурация, возраст
/// данных, проверка обновлений), фильтр-переключатель «Только проблемы» и кнопки
/// «Проверить доступность» / «Открыть базу». Чистый .NET, обе платформы
/// (WPF и Avalonia). Окна только вызывают методы и привязываются к коллекциям —
/// вся логика здесь.
/// </summary>
public sealed class MaintenanceCenterViewModel : ViewModelBase
{
    private readonly Action _checkAvailability;
    private readonly Action<Infobase> _openBase;
    private bool _onlyProblems;

    /// <param name="infobases">Все базы списка.</param>
    /// <param name="checkAvailability">Запуск общей проверки доступности (команда главного окна).</param>
    /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
    /// <param name="freeSpaceWarningGb">Порог предупреждения «Свободно на диске» в ГБ
    /// (0 — не предупреждать), по умолчанию 10 ГБ (0.3.9.96).</param>
    public MaintenanceCenterViewModel(
        IEnumerable<Infobase> infobases,
        Action checkAvailability,
        Action<Infobase> openBase,
        int freeSpaceWarningGb = DiskFreeSpaceHelper.DefaultWarningGb)
    {
        _checkAvailability = checkAvailability;
        _openBase = openBase;

        var cache = LoadUpdateCache();
        foreach (var ib in infobases)
        {
            if (ib is null)
                continue;
            var row = new MaintenanceCenterRowViewModel(
                ib, ResolveUpdateResult(ib, cache), freeSpaceWarningGb);
            AllRows.Add(row);
            // Размер кэша вычисляется асинхронно при открытии окна (0.3.9.89).
            ib.RefreshCacheSizeAsync();
        }
        ApplyFilter();
    }

    /// <summary>Все строки (до применения фильтра).</summary>
    public ObservableCollection<MaintenanceCenterRowViewModel> AllRows { get; } = new();

    /// <summary>Строки, видимые в таблице (с учётом фильтра «Только проблемы»).</summary>
    public ObservableCollection<MaintenanceCenterRowViewModel> Rows { get; } = new();

    /// <summary>Фильтр-переключатель «Только проблемы».</summary>
    public bool OnlyProblems
    {
        get => _onlyProblems;
        set
        {
            if (SetProperty(ref _onlyProblems, value))
                ApplyFilter();
        }
    }

    /// <summary>Итоговая строка: сколько баз с проблемами из общего числа.</summary>
    public string SummaryText { get; private set; } = string.Empty;

    /// <summary>Есть ли хоть одна база в списке.</summary>
    public bool HasBases => AllRows.Count > 0;

    /// <summary>«Проверить доступность» — запускает общую проверку в главном окне.</summary>
    public void CheckAvailability() => _checkAvailability();

    /// <summary>«Открыть базу» — переход к строке в главном окне (FindInList-механика).</summary>
    public void OpenBase(MaintenanceCenterRowViewModel row)
    {
        if (row is not null)
            _openBase(row.Infobase);
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in AllRows)
        {
            if (!_onlyProblems || row.HasProblem)
                Rows.Add(row);
        }

        SummaryText = string.Format(
            LocalizationManager.T("Maintenance.ProblemsCountFormat"),
            AllRows.Count(r => r.HasProblem),
            AllRows.Count);
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasBases));
    }

    /// <summary>Читает кэш проверок обновлений конфигураций из настроек приложения.</summary>
    private static IReadOnlyDictionary<string, ConfigUpdateCheckResult> LoadUpdateCache()
    {
        try
        {
            var settings = AppServices.GetRequiredService<IInfobaseRepository>().LoadSettings();
            return settings.UpdateCheckCache
                ?? new Dictionary<string, ConfigUpdateCheckResult>();
        }
        catch
        {
            // Настройки не читаются — считаем, что кэша нет (окно остаётся рабочим).
            return new Dictionary<string, ConfigUpdateCheckResult>();
        }
    }

    /// <summary>Находит последний результат проверки обновлений по коду связи конфигурации.</summary>
    private static ConfigUpdateCheckResult? ResolveUpdateResult(
        Infobase ib, IReadOnlyDictionary<string, ConfigUpdateCheckResult> cache)
    {
        var code = ib.UpdateConfigCode;
        if (string.IsNullOrWhiteSpace(code))
            return null;
        return cache.TryGetValue(code, out var result) ? result : null;
    }
}