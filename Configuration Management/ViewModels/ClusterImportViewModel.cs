using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel окна «Импорт из кластера 1С» (0.3.9.173, цикл 0.3.9.172–0.3.9.175):
/// подключение к агенту сервера/серверу администрирования через rac (адрес/порт/логин/
/// пароль), список кластеров, загрузка информационных баз выбранного кластера в чеклист
/// (<see cref="ClusterImportRow"/>) с пометкой дубликатов (по строке подключения) и файловых
/// баз кластера, сводка перед подтверждением и команда «Импортировать» (возвращает отмеченные
/// новые базы через <see cref="SelectedBases"/>). Чистый .NET без платформенных зависимостей —
/// обе платформы (WPF и Avalonia); окна только привязываются.
/// </summary>
public sealed class ClusterImportViewModel : ViewModelBase
{
    private readonly IRacClient _rac;
    private readonly IReadOnlyList<Infobase> _existingInfobases;
    private readonly Action<Action>? _dispatchToUi;
    private readonly Dictionary<Guid, RacClusterInfo?> _clusterInfoCache = new();

    private int _busy;
    private string _serverAddress = "localhost";
    private int _serverPort = IRacClient.DefaultPort;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private Guid? _selectedClusterId;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private int _readyToImportCount;
    private int _duplicateCount;
    private IReadOnlyList<Infobase> _selectedBases = Array.Empty<Infobase>();
    private bool _importCompleted;

    private ICommand? _connectCommand;
    private ICommand? _loadBasesCommand;
    private ICommand? _importCommand;
    private ICommand? _selectAllCommand;
    private ICommand? _selectNoneCommand;

    /// <param name="rac">Клиент rac (кластеры, «cluster info», базы кластера).</param>
    /// <param name="existingInfobases">Базы, уже присутствующие в списке приложения — для пометки дубликатов.</param>
    /// <param name="dispatchToUi">
    /// Доставка применения результатов в UI-поток (передаёт окно); null — результаты
    /// применяются прямо из рабочего потока (тесты).
    /// </param>
    public ClusterImportViewModel(
        IRacClient rac,
        IReadOnlyList<Infobase> existingInfobases,
        Action<Action>? dispatchToUi = null)
    {
        _rac = rac ?? throw new ArgumentNullException(nameof(rac));
        _existingInfobases = existingInfobases ?? throw new ArgumentNullException(nameof(existingInfobases));
        _dispatchToUi = dispatchToUi;
    }

    // ===================== Параметры подключения =====================

    /// <summary>Адрес сервера 1С (host или IP; host:port для нестандартного порта ragent/RAS).</summary>
    public string ServerAddress
    {
        get => _serverAddress;
        set => SetProperty(ref _serverAddress, value?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// Порт агента сервера 1С (ragent), по умолчанию 1540; для RAS — 1545.
    /// Некорректные значения (<see cref="IRacClient.DefaultPort"/> при ≤ 0).
    /// </summary>
    public int ServerPort
    {
        get => _serverPort;
        set => SetProperty(ref _serverPort, value > 0 ? value : IRacClient.DefaultPort);
    }

    /// <summary>Логин администратора кластера (пустая строка — без аутентификации).</summary>
    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value ?? string.Empty);
    }

    /// <summary>
    /// Пароль администратора кластера. Живёт ТОЛЬКО в памяти окна (в тестах сеттер
    /// открытый; в UI пароль передаётся вручную из PasswordBox при подключении),
    /// на диск не сохраняется (см. решения планирования, раздел 5 плана).
    /// </summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value ?? string.Empty);
    }

    // ===================== Кластеры =====================

    /// <summary>Кластеры сервера (модели из rac «cluster list»).</summary>
    public IReadOnlyList<RacCluster> Clusters { get; private set; } = Array.Empty<RacCluster>();

    /// <summary>
    /// Выбранный кластер. При установке значения после подключения запускается
    /// загрузка информационных баз кластера (и «cluster info» для хоста подключения).
    /// </summary>
    public Guid? SelectedClusterId
    {
        get => _selectedClusterId;
        set
        {
            if (!SetProperty(ref _selectedClusterId, value))
                return;
            // Смена кластера сбрасывает предыдущий чеклист и результат импорта.
            ClearRows();
            SelectedBases = Array.Empty<Infobase>();
            ImportCompleted = false;
            OnPropertyChanged(nameof(SelectedBases));
            OnPropertyChanged(nameof(ImportCompleted));
            if (value is Guid id)
                _ = LoadBasesAsync(id);
        }
    }

    // ===================== Чеклист =====================

    /// <summary>Строки чеклиста найденных баз выбранного кластера.</summary>
    public ObservableCollection<ClusterImportRow> Rows { get; } = new();

    /// <summary>Число отмеченных новых баз, готовых к импорту (сводка перед подтверждением).</summary>
    public int ReadyToImportCount
    {
        get => _readyToImportCount;
        private set => SetProperty(ref _readyToImportCount, value);
    }

    /// <summary>Число дубликатов и пропущенных баз («уже есть в списке», «файловая база кластера»).</summary>
    public int DuplicateCount
    {
        get => _duplicateCount;
        private set => SetProperty(ref _duplicateCount, value);
    }

    /// <summary>Результат импорта: отмеченные новые базы (без дубликатов и файловых).</summary>
    public IReadOnlyList<Infobase> SelectedBases
    {
        get => _selectedBases;
        private set => SetProperty(ref _selectedBases, value);
    }

    /// <summary>Флаг успешного выполнения «Импортировать» (окно на этапе 3 использует его для закрытия).</summary>
    public bool ImportCompleted
    {
        get => _importCompleted;
        private set => SetProperty(ref _importCompleted, value);
    }

    /// <summary>
    /// Создавать группу по имени кластера при импорте (базы получают <c>Group = имя кластера</c>).
    /// Группировка станет опцией окна на этапе 3 — здесь параметр по умолчанию включён.
    /// </summary>
    public bool UseClusterGrouping { get; set; } = true;

    // ===================== Команды =====================

    /// <summary>«Подключиться»: список кластеров, при успехе — автовыбор первого и загрузка его баз.</summary>
    public ICommand ConnectCommand =>
        _connectCommand ??= new RelayCommand(async () => await ConnectAsync());

    /// <summary>«Загрузить»: перечитать базы выбранного кластера (после подключения).</summary>
    public ICommand LoadBasesCommand =>
        _loadBasesCommand ??= new RelayCommand(LoadBases);

    /// <summary>«Импортировать»: собрать отмеченные новые базы в <see cref="SelectedBases"/> и установить <see cref="ImportCompleted"/>.</summary>
    public ICommand ImportCommand =>
        _importCommand ??= new RelayCommand(Import);

    /// <summary>«Выделить все»: отметить все импортируемые (не дубликаты, не файловые) строки.</summary>
    public ICommand SelectAllCommand =>
        _selectAllCommand ??= new RelayCommand(SelectAll);

    /// <summary>«Снять все»: снять отметки со всех строк чеклиста.</summary>
    public ICommand SelectNoneCommand =>
        _selectNoneCommand ??= new RelayCommand(SelectNone);

    // ===================== Статус =====================

    /// <summary>Строка состояния («Подключение…», «Загрузка баз…», итоги).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Подробное сообщение последней ошибки (текст rac/stderr как есть).</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Выполняется ли сейчас запрос (флаг занятости исключает наложение, см. ProcessInspectorViewModel).</summary>
    public bool IsBusy => _busy == 1;

    // ===================== Действия =====================

    /// <summary>
    /// «Подключиться»: получить список кластеров (rac «cluster list»); при успехе — выбрать
    /// первый кластер автоматически (он же запускает загрузку баз). Пустой адрес отклоняется
    /// валидацией без обращения к rac. Ошибки не роняют окно — пишутся в ErrorMessage/StatusText.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerAddress))
        {
            ErrorMessage = LocalizationManager.T("ClusterImport.Error.AddressRequired");
            StatusText = string.Empty;
            return;
        }

        if (!TryEnterBusy())
            return;

        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ClusterImport.Progress.Connect");

            var clusters = await _rac.GetClustersAsync(BuildParams()).ConfigureAwait(false);

            ApplyClusters(clusters);

            StatusText = clusters.Count == 0
                ? LocalizationManager.T("ClusterImport.Status.NoClusters")
                : string.Format(LocalizationManager.T("ClusterImport.Status.ConnectedFormat"), clusters.Count);
        }
        catch (RacClientException ex)
        {
            ErrorMessage = ex.Message;
            StatusText = LocalizationManager.T("ClusterImport.Status.ConnectFailed");
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("ClusterImport.Status.Cancelled");
        }
        finally
        {
            ExitBusy();
        }

        // Автовыбор первого кластера — уже вне флага занятости: сеттер запустит загрузку баз.
        if (SelectedClusterId is null && Clusters.Count > 0)
            SelectedClusterId = Clusters[0].Id;
    }

    /// <summary>«Загрузить»: перечитать базы выбранного кластера (без подключения — no-op).</summary>
    public void LoadBases()
    {
        if (SelectedClusterId is Guid id)
            _ = LoadBasesAsync(id);
    }

    /// <summary>
    /// Загружает информационные базы кластера (rac «infobase summary list --cluster=...»)
    /// и «cluster info» для хоста строки подключения (кэшируется по clusterId). После await —
    /// сверка актуальности <see cref="SelectedClusterId"/>: устаревший результат быстрой смены
    /// кластера не применяется, вместо этого запускается загрузка актуального кластера.
    /// </summary>
    public async Task LoadBasesAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        if (!TryEnterBusy())
            return;

        var stale = false;
        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("ClusterImport.Progress.LoadBases");

            var cluster = Clusters.FirstOrDefault(c => c.Id == clusterId);
            if (cluster is null)
                return;

            var parameters = BuildParams();

            if (!_clusterInfoCache.TryGetValue(clusterId, out var info))
            {
                info = await _rac.GetClusterInfoAsync(parameters, clusterId, cancellationToken).ConfigureAwait(false);
                _clusterInfoCache[clusterId] = info;
            }

            var summaries = await _rac.GetInfobasesAsync(parameters, clusterId, cancellationToken).ConfigureAwait(false);

            // Кластер сменился, пока шла загрузка — результаты устаревшего запроса не применяем.
            if (SelectedClusterId != clusterId)
            {
                stale = true;
                return;
            }

            ApplyRows(summaries, cluster.Port, cluster.Name, info?.HostName);

            StatusText = summaries.Count == 0
                ? LocalizationManager.T("ClusterImport.Error.NoBases")
                : string.Format(LocalizationManager.T("ClusterImport.Status.LoadedFormat"), summaries.Count);
        }
        catch (RacClientException ex)
        {
            ErrorMessage = ex.Message;
            StatusText = LocalizationManager.T("ClusterImport.Status.LoadFailed");
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationManager.T("ClusterImport.Status.Cancelled");
        }
        finally
        {
            ExitBusy();
        }

        // Если кластер сменился во время загрузки — запускаем загрузку актуального
        // (после ExitBusy, чтобы флаг занятости не заблокировал повторный вход).
        if (stale && SelectedClusterId is Guid current)
            _ = LoadBasesAsync(current);
    }

    /// <summary>
    /// «Импортировать»: собрать отмеченные новые базы (не дубликаты, не файловые) в
    /// <see cref="SelectedBases"/> и установить <see cref="ImportCompleted"/>. Повторный чек
    /// дубликата/файловой базы пользователем не приводит к импорту — фильтр на этом шаге.
    /// </summary>
    public void Import()
    {
        if (IsBusy)
            return;

        SelectedBases = Rows
            .Where(r => r.IsChecked && !r.IsDuplicate && string.IsNullOrEmpty(r.SkipReason) && r.Tag is not null)
            .Select(r => r.Tag)
            .ToList();
        ImportCompleted = true;
    }

    /// <summary>«Выделить все»: отметить все импортируемые строки (дубликаты и файловые не трогаем).</summary>
    public void SelectAll()
    {
        foreach (var row in Rows)
        {
            if (!row.IsDuplicate && string.IsNullOrEmpty(row.SkipReason))
                row.IsChecked = true;
        }
        UpdateSummary();
    }

    /// <summary>«Снять все»: снять отметки со всех строк чеклиста.</summary>
    public void SelectNone()
    {
        foreach (var row in Rows)
            row.IsChecked = false;
        UpdateSummary();
    }

    // ===================== Внутреннее =====================

    private RacConnectionParams BuildParams() => new()
    {
        Address = string.IsNullOrWhiteSpace(ServerAddress) ? "localhost" : ServerAddress.Trim(),
        Port = ServerPort > 0 ? ServerPort : IRacClient.DefaultPort,
        User = UserName?.Trim() ?? string.Empty,
        Password = Password ?? string.Empty
    };

    private void ApplyClusters(IReadOnlyList<RacCluster> clusters)
    {
        void Apply()
        {
            Clusters = clusters;
            OnPropertyChanged(nameof(Clusters));
            // Сброс выбора: подключение к другому серверу — чужие базы не показываем.
            SelectedClusterId = null;
            ClearRows();
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    private void ApplyRows(
        IReadOnlyList<RacInfobaseSummary> summaries,
        int clusterPort,
        string clusterName,
        string? clusterHostName)
    {
        void Apply()
        {
            ClearRows();
            foreach (var source in summaries)
            {
                var row = BuildRow(source, clusterPort, clusterName, clusterHostName);
                row.PropertyChanged += RowOnPropertyChanged;
                Rows.Add(row);
            }
            UpdateSummary();
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    /// <summary>
    /// Строит строку чеклиста: маппинг в базу списка приложения
    /// (<see cref="RacInfobaseMapper.ToInfobase"/>, группа — имя кластера при
    /// <see cref="UseClusterGrouping"/>), пометка дубликатов по строке подключения
    /// (<see cref="RacInfobaseMapper.IsDuplicate"/> против существующих баз) и файловых баз
    /// кластера (пустой <c>Dbms</c>) — такие строки приходят со снятым флажком и причиной пропуска.
    /// </summary>
    private ClusterImportRow BuildRow(
        RacInfobaseSummary source,
        int clusterPort,
        string clusterName,
        string? clusterHostName)
    {
        var groupName = UseClusterGrouping ? clusterName : string.Empty;
        var infobase = RacInfobaseMapper.ToInfobase(source, ServerAddress, clusterPort, clusterHostName, groupName);

        string? skipReason = null;
        var isDuplicate = false;
        if (string.IsNullOrWhiteSpace(source.Dbms))
        {
            // Файловая ИБ внутри кластера: для импорта нужен file-descriptor (вне цикла, п. 11 плана).
            skipReason = LocalizationManager.T("ClusterImport.SkipFileBase");
        }
        else if (RacInfobaseMapper.IsDuplicate(_existingInfobases, infobase))
        {
            skipReason = LocalizationManager.T("ClusterImport.AlreadyExists");
            isDuplicate = true;
        }

        return new ClusterImportRow(
            source.Name,
            BuildSubtitle(source),
            clusterName,
            infobase.Connection?.ToConnectionString() ?? string.Empty,
            isChecked: skipReason is null,
            isDuplicate: isDuplicate,
            skipReason: skipReason,
            tag: infobase);
    }

    /// <summary>
    /// Подпись строки: описание базы и сведения о СУБД (тип • сервер • имя БД в СУБД),
    /// разделённые « • ». Пустые части опускаются.
    /// </summary>
    private static string BuildSubtitle(RacInfobaseSummary source)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(source.Descr))
            parts.Add(source.Descr.Trim());

        if (!string.IsNullOrWhiteSpace(source.Dbms))
        {
            var dbParts = new List<string> { source.Dbms.Trim() };
            if (!string.IsNullOrWhiteSpace(source.DbServer))
                dbParts.Add(source.DbServer.Trim());
            if (!string.IsNullOrWhiteSpace(source.DbName))
                dbParts.Add(source.DbName.Trim());
            parts.Add(string.Join(" • ", dbParts));
        }

        return string.Join(" • ", parts);
    }

    private void ClearRows()
    {
        foreach (var row in Rows)
            row.PropertyChanged -= RowOnPropertyChanged;
        Rows.Clear();
        UpdateSummary();
    }

    private void RowOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClusterImportRow.IsChecked))
            UpdateSummary();
    }

    private void UpdateSummary()
    {
        ReadyToImportCount = Rows.Count(r => r.IsChecked && !r.IsDuplicate && string.IsNullOrEmpty(r.SkipReason));
        DuplicateCount = Rows.Count(r => r.IsDuplicate || !string.IsNullOrEmpty(r.SkipReason));
    }

    private bool TryEnterBusy()
    {
        // Один запрос за раз: длительная rac-команда не должна копить очередь (см. ProcessInspectorViewModel:80).
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return false;
        OnPropertyChanged(nameof(IsBusy));
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        OnPropertyChanged(nameof(IsBusy));
    }
}