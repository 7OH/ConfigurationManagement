using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel окна «Обозреватель метаданных…» (цикл 0.3.9.132–0.3.9.136, этап 2).
/// Чистый .NET без платформенных зависимостей — используется обеими платформами
/// (WPF и Avalonia); окна только привязываются.
///
/// Панель источника: выбранная ИБ (база) либо файл .cf; <see cref="LoadCommand"/>
/// запускает выгрузку через <see cref="IMetadataExplorerService"/> (прогресс — событие
/// <see cref="StageChanged"/> и <c>IProgress<string></c>) и строит ленивое дерево
/// «Конфигурация → Подсистемы (+ Без подсистемы)» через <see cref="MetadataTreeNodeViewModel"/>:
/// дети подгружаются при раскрытии узла. Детали выбранного объекта читаются по запросу
/// (<see cref="MetadataXmlParser.ReadObjectDetails"/>) и показываются на панели справа.
///
/// Образец — <see cref="RepositoryBrowserViewModel"/>: делегат <paramref name="dispatchToUi"/>
/// для тестов (null — применение из рабочего потока), флаг занятости через
/// <see cref="Interlocked"/>, статус/ошибки через статус-строку, ошибки не роняют окно.
///
/// <see cref="Dispose"/> удаляет временный каталог выгрузки <c>%TEMP%\cm_metaeplorer_*</c>
/// (вызывается окном в Closed).
/// </summary>
public sealed class MetadataExplorerViewModel : ViewModelBase
{
    /// <summary>Источник выгрузки: информационная база или файл .cf.</summary>
    public enum SourceMode
    {
        /// <summary>Выгрузка конфигурации выбранной ИБ напрямую.</summary>
        Base,

        /// <summary>Распаковка файла .cf во временную файловую ИБ и выгрузка из неё.</summary>
        Cf
    }

    private readonly IMetadataExplorerService _service;
    private readonly IDialogService _dialogs;
    private readonly Action<Action>? _dispatchToUi;

    /// <summary>Задержка поиска по мере ввода (debounce), мс.</summary>
    private const int SearchDebounceMs = 300;

    /// <summary>Максимум результатов поиска: при превышении — статус-предупреждение.</summary>
    public const int MaxSearchResults = 500;

    private CancellationTokenSource? _searchCts;
    private readonly List<MetadataObjectSummary> _searchIndex = new();
    private readonly List<MetadataTreeNodeViewModel> _searchNodes = new();
    private bool _indexBuilt;
    private int _lastIndexProgress;

    private int _busy;
    private MetadataDump? _dump;
    private SourceMode _sourceMode = SourceMode.Base;
    private Infobase? _selectedBase;
    private string _cfPath = string.Empty;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private string _configurationTitle = string.Empty;
    private MetadataTreeNodeViewModel? _selectedNode;
    private string _searchText = string.Empty;
    private string _activeTypeFilter = string.Empty;
    private bool _isSearchActive;
    private int _searchResultsCount;
    private MetadataTypeFilterOption? _typeFilter;
    private IReadOnlyList<string> _availableTypes = Array.Empty<string>();

    private string _detailsName = string.Empty;
    private string _detailsSynonym = string.Empty;
    private string _detailsComment = string.Empty;
    private string _detailsAttributesText = string.Empty;
    private string _detailsTabularSectionsText = string.Empty;
    private string _detailsFormsText = string.Empty;
    private string _detailsCommandsText = string.Empty;
    private string _detailsHierarchicalText = string.Empty;
    private string _detailsSizeText = string.Empty;
    private string _detailsRelPath = string.Empty;
    private bool _hasDetails;

    private ICommand? _loadCommand;
    private ICommand? _browseCfCommand;

    /// <param name="bases">Все информационные базы списка (для ComboBox).</param>
    /// <param name="selectedBase">Предвыбранная база (выбранная в главном окне; может быть null).</param>
    /// <param name="service">Сервис выгрузки конфигурации (реальный — DI, fake — в тестах).</param>
    /// <param name="dialogs">Диалоги (выбор файла .cf, предупреждения).</param>
    /// <param name="dispatchToUi">
    /// Доставка применения результатов в UI-поток (передаёт окно); null — результаты
    /// применяются прямо из рабочего потока (тесты).
    /// </param>
    public MetadataExplorerViewModel(
        IReadOnlyList<Infobase> bases,
        Infobase? selectedBase,
        IMetadataExplorerService service,
        IDialogService dialogs,
        Action<Action>? dispatchToUi = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatchToUi = dispatchToUi;

        foreach (var infobase in bases ?? Array.Empty<Infobase>())
            Bases.Add(infobase);

        if (selectedBase is not null && Bases.Contains(selectedBase))
            SelectedBase = selectedBase;
        else if (Bases.Count > 0)
            SelectedBase = Bases[0];
    }

    /// <summary>Смена этапа операции (текст для окна прогресса). Может вызываться из фонового потока.</summary>
    public event Action<string>? StageChanged;

    // ===================== Панель источника =====================

    /// <summary>Все информационные базы списка (ComboBox).</summary>
    public ObservableCollection<Infobase> Bases { get; } = new();

    /// <summary>Выбранная база (источник «База»).</summary>
    public Infobase? SelectedBase
    {
        get => _selectedBase;
        set => SetProperty(ref _selectedBase, value);
    }

    /// <summary>Текущий источник выгрузки.</summary>
    public SourceMode Mode
    {
        get => _sourceMode;
        set
        {
            if (SetProperty(ref _sourceMode, value))
            {
                OnPropertyChanged(nameof(IsSourceBase));
                OnPropertyChanged(nameof(IsSourceCf));
            }
        }
    }

    /// <summary>Радиокнопка «База»: true, когда источник — выбранная ИБ.</summary>
    public bool IsSourceBase
    {
        get => _sourceMode == SourceMode.Base;
        set { if (value) Mode = SourceMode.Base; }
    }

    /// <summary>Радиокнопка «Файл .cf»: true, когда источник — файл конфигурации.</summary>
    public bool IsSourceCf
    {
        get => _sourceMode == SourceMode.Cf;
        set { if (value) Mode = SourceMode.Cf; }
    }

    /// <summary>Путь к файлу .cf (источник «Файл .cf»).</summary>
    public string CfPath
    {
        get => _cfPath;
        set => SetProperty(ref _cfPath, value ?? string.Empty);
    }

    /// <summary>«Обзор…»: выбор файла .cf через <see cref="IDialogService.OpenFileDialog"/>.</summary>
    public ICommand BrowseCfCommand =>
        _browseCfCommand ??= new RelayCommand(_ => BrowseCf());

    /// <summary>«Загрузить»: валидация → выгрузка с прогрессом → построение дерева.</summary>
    public ICommand LoadCommand =>
        _loadCommand ??= new RelayCommand(async () => await LoadAsync(), () => !IsBusy);

    // ===================== Поиск и фильтр (этап 3) =====================

    /// <summary>
    /// Текст поиска по имени объекта (по мере ввода, debounce ~300 мс). При каждом
    /// вводе предыдущий отложенный поиск отменяется; запрос короче 2 символов
    /// возвращает режим дерева без подсветки.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty))
                return;
            ScheduleSearch();
        }
    }

    /// <summary>Поиск активен (применён запрос из ≥ 2 символов).</summary>
    public bool IsSearchActive
    {
        get => _isSearchActive;
        private set => SetProperty(ref _isSearchActive, value);
    }

    /// <summary>Число найденных объектов (может превышать <see cref="MaxSearchResults"/>).</summary>
    public int SearchResultsCount
    {
        get => _searchResultsCount;
        private set => SetProperty(ref _searchResultsCount, value);
    }

    /// <summary>Типы метаданных, фактически присутствующие в выгрузке
    /// (порядок <see cref="MetadataTypeLocalizer.SortTypes"/>).</summary>
    public IReadOnlyList<string> AvailableTypes
    {
        get => _availableTypes;
        private set => SetProperty(ref _availableTypes, value);
    }

    /// <summary>Варианты фильтра типа для ComboBox: «Все типы» + типы выгрузки.</summary>
    public ObservableCollection<MetadataTypeFilterOption> TypeFilterOptions { get; } = new();

    /// <summary>Выбранный фильтр типа (null до загрузки); смена сразу применяет
    /// <see cref="ApplyFilters"/> к видимости узлов дерева.</summary>
    public MetadataTypeFilterOption? TypeFilter
    {
        get => _typeFilter;
        set
        {
            if (!SetProperty(ref _typeFilter, value))
                return;
            _activeTypeFilter = value?.Value ?? string.Empty;
            ApplyFilters();
        }
    }

    /// <summary>Плоский индекс объектов дерева (строится при первом поиске;
    /// повторные поиски идут по нему без переобхода выгрузки).</summary>
    public IReadOnlyList<MetadataObjectSummary> SearchIndex => _searchIndex;

    /// <summary>«Очистить»: сбрасывает текст поиска (возврат в режим дерева).</summary>
    public void ClearSearch() => SearchText = string.Empty;

    // ===================== Дерево и детали =====================

    /// <summary>Корневые узлы дерева (ItemsSource TreeView); после загрузки — один корень.</summary>
    public ObservableCollection<MetadataTreeNodeViewModel> TreeNodes { get; } = new();

    /// <summary>Шапка окна: «<имя> <версия>» (из <see cref="MetadataDump"/>); пусто до загрузки.</summary>
    public string ConfigurationTitle
    {
        get => _configurationTitle;
        private set => SetProperty(ref _configurationTitle, value);
    }

    /// <summary>Выполняется ли сейчас выгрузка (флаг занятости через <see cref="Interlocked"/>).</summary>
    public bool IsBusy => _busy == 1;

    /// <summary>
    /// Выбранный узел дерева. При выборе объекта загружает детали
    /// (<see cref="MetadataXmlParser.ReadObjectDetails"/>) на панель справа.
    /// </summary>
    public MetadataTreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value))
                return;
            if (value is { Kind: MetadataTreeNodeKind.Object, ObjectSummary: not null } node)
                _ = LoadDetailsAsync(node);
            else
                ApplyDetails(null);
        }
    }

    /// <summary>Панель деталей заполнена (выбран объект).</summary>
    public bool HasDetails
    {
        get => _hasDetails;
        private set
        {
            if (SetProperty(ref _hasDetails, value))
                OnPropertyChanged(nameof(HasNoDetails));
        }
    }

    /// <summary>Обратный флаг для подсказки «выберите объект» (Avalonia: прямая привязка bool).</summary>
    public bool HasNoDetails => !_hasDetails;

    // ===================== Статус =====================

    /// <summary>Строка состояния («Выгрузка…», «Загружено», ошибки).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Подробное сообщение последней ошибки (для статус-строки).</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    // ===================== Поля панели деталей =====================

    public string DetailsName { get => _detailsName; private set => SetProperty(ref _detailsName, value); }
    public string DetailsSynonym { get => _detailsSynonym; private set => SetProperty(ref _detailsSynonym, value); }
    public string DetailsComment { get => _detailsComment; private set => SetProperty(ref _detailsComment, value); }
    public string DetailsAttributesText { get => _detailsAttributesText; private set => SetProperty(ref _detailsAttributesText, value); }
    public string DetailsTabularSectionsText { get => _detailsTabularSectionsText; private set => SetProperty(ref _detailsTabularSectionsText, value); }
    public string DetailsFormsText { get => _detailsFormsText; private set => SetProperty(ref _detailsFormsText, value); }
    public string DetailsCommandsText { get => _detailsCommandsText; private set => SetProperty(ref _detailsCommandsText, value); }
    public string DetailsHierarchicalText { get => _detailsHierarchicalText; private set => SetProperty(ref _detailsHierarchicalText, value); }
    public string DetailsSizeText { get => _detailsSizeText; private set => SetProperty(ref _detailsSizeText, value); }
    public string DetailsRelPath { get => _detailsRelPath; private set => SetProperty(ref _detailsRelPath, value); }

    // ===================== Действия =====================

    /// <summary>«Обзор…» для файла .cf.</summary>
    public void BrowseCf()
    {
        var path = _dialogs.OpenFileDialog(
            LocalizationManager.T("MetadataExplorer.SelectCfFile"),
            LocalizationManager.T("MetadataExplorer.CfFileFilter"));
        if (!string.IsNullOrWhiteSpace(path))
            CfPath = path;
    }

    /// <summary>
    /// «Загрузить»: валидация источника → <see cref="IMetadataExplorerService.DumpFromBaseAsync"/>
    /// или <see cref="IMetadataExplorerService.DumpFromCfAsync"/> с прогрессом
    /// (<see cref="StageChanged"/> + <c>IProgress<string></c>) → <see cref="ApplyDump"/>
    /// строит корень дерева. Ошибки — в статус-строку, окно не ронять.
    /// </summary>
    public async Task LoadAsync()
    {
        if (!TryEnterBusy())
            return;

        try
        {
            ApplyBeforeLoad();

            MetadataDump dump;
            var progress = new Progress<string>(ReportStage);
            if (Mode == SourceMode.Base)
            {
                var infobase = SelectedBase;
                if (infobase is null)
                {
                    ApplyValidationError(LocalizationManager.T("MetadataExplorer.Errors.NoBase"));
                    return;
                }

                StageChanged?.Invoke(LocalizationManager.T("MetadataExplorer.Status.Dumping"));
                dump = await _service.DumpFromBaseAsync(infobase, progress).ConfigureAwait(false);
            }
            else
            {
                var cfPath = CfPath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cfPath))
                {
                    ApplyValidationError(LocalizationManager.T("MetadataExplorer.Errors.NoCf"));
                    return;
                }
                if (!File.Exists(cfPath))
                {
                    ApplyValidationError(LocalizationManager.T("MetadataExplorer.Errors.CfNotExists"));
                    return;
                }

                StageChanged?.Invoke(LocalizationManager.T("MetadataExplorer.Status.Dumping"));
                dump = await _service
                    .DumpFromCfAsync(cfPath, ResolvePlatformVersion(), progress)
                    .ConfigureAwait(false);
            }

            ApplyDump(dump);
        }
        catch (Exception ex)
        {
            ApplyError(ex);
        }
        finally
        {
            ExitBusy();
        }
    }

    /// <summary>
    /// Детали выбранного объекта (публичный для тестов): <see cref="MetadataXmlParser.ReadObjectDetails"/>
    /// по объекту выбранного узла. Ошибки — статус-строка, окно не ронять.
    /// </summary>
    public async Task LoadDetailsAsync(MetadataTreeNodeViewModel? node)
    {
        var summary = node?.ObjectSummary;
        if (summary is null)
        {
            ApplyDetails(null);
            return;
        }

        try
        {
            var details = MetadataXmlParser.ReadObjectDetails(
                _dump?.RootPath ?? string.Empty, summary.TypeDir, summary.Name);
            ApplyDetails(details);
        }
        catch (Exception ex)
        {
            ApplyError(ex);
        }
    }

    /// <summary>
    /// Удаляет временный каталог выгрузки <c>%TEMP%\cm_metaeplorer_*</c>
    /// (<see cref="MetadataDump.Delete"/>) и отменяет отложенный поиск.
    /// Вызывается окном в Closed.
    /// </summary>
    public void Dispose()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _dump?.Delete();
        _dump = null;
    }

    // ===================== Поиск и фильтр (этап 3) =====================

    /// <summary>
    /// Debounce ввода: отменяет предыдущий отложенный поиск и планирует новый через
    /// <see cref="SearchDebounceMs"/> мс (<c>CancellationTokenSource</c> + <c>Task.Delay</c>;
    /// при каждом вводе предыдущий токен отменяется).
    /// </summary>
    private void ScheduleSearch()
    {
        var cts = new CancellationTokenSource();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = cts;
        _ = DebounceSearchAsync(cts.Token);
    }

    private async Task DebounceSearchAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // Пользователь ввёл новый текст — прежний поиск отменён.
        }

        if (token.IsCancellationRequested)
            return;
        ApplySearch();
    }

    /// <summary>
    /// Применяет текущий текст поиска к дереву: при первом поиске строит индекс
    /// объектов (догрузка недостающей части дерева с прогрессом «Индексация…»),
    /// помечает совпадения <see cref="MetadataTreeNodeViewModel.IsMatch"/> и раскрывает
    /// их пути. Публичен для тестов (debounce проверяется отдельно ожиданием).
    /// </summary>
    public void ApplySearch()
    {
        if (TreeNodes.Count == 0 || _dump is null)
        {
            IsSearchActive = false;
            SearchResultsCount = 0;
            return;
        }

        var query = (_searchText ?? string.Empty).Trim();
        if (query.Length < 2)
        {
            // Запрос короче 2 символов — режим дерева без подсветки.
            ClearHighlights();
            IsSearchActive = false;
            SearchResultsCount = 0;
            return;
        }

        EnsureSearchIndex();

        var matches = 0;
        var highlighted = 0;
        foreach (var node in _searchNodes)
        {
            var name = node.ObjectSummary?.Name ?? string.Empty;
            if (!name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                node.IsMatch = false;
                continue;
            }

            matches++;
            if (highlighted < MaxSearchResults)
            {
                // Подсветка и автораскрытие пути — только для первых MaxSearchResults
                // совпадений; остальные учитываются в счётчике, но не подсвечиваются.
                node.IsMatch = true;
                ExpandPath(node);
                highlighted++;
            }
            else
            {
                node.IsMatch = false;
            }
        }

        SearchResultsCount = matches;
        IsSearchActive = true;

        if (matches > MaxSearchResults)
            StatusText = LocalizationManager.T("MetadataExplorer.Status.TooManyResults");
        else if (matches == 0)
            StatusText = LocalizationManager.T("MetadataExplorer.Status.NoResults");
        else
            StatusText = string.Format(
                LocalizationManager.T("MetadataExplorer.Status.FoundFormat"),
                matches, _searchIndex.Count);
    }

    /// <summary>
    /// Строит плоский индекс объектов (один раз): последовательно догружает недостающую
    /// часть дерева (все подсистемы, типы, объекты) через
    /// <see cref="MetadataTreeNodeViewModel.EnsureLoaded"/> и собирает объекты с путями.
    /// Прогресс «Индексация… N объектов» — в статус-строке. Повторные поиски идут
    /// по индексу без переобхода выгрузки.
    /// </summary>
    private void EnsureSearchIndex()
    {
        if (_indexBuilt || TreeNodes.Count == 0)
            return;

        StatusText = LocalizationManager.T("MetadataExplorer.Status.Indexing");
        _searchIndex.Clear();
        _searchNodes.Clear();
        _lastIndexProgress = 0;

        foreach (var root in TreeNodes)
            CollectIndexFromNode(root);

        _indexBuilt = true;
    }

    private void CollectIndexFromNode(MetadataTreeNodeViewModel node)
    {
        node.EnsureLoaded();
        if (node is { Kind: MetadataTreeNodeKind.Object, ObjectSummary: not null })
        {
            _searchIndex.Add(node.ObjectSummary);
            _searchNodes.Add(node);
        }

        // Прогресс индексации — не на каждый объект, а порциями (~200).
        if (_searchIndex.Count - _lastIndexProgress >= 200)
        {
            _lastIndexProgress = _searchIndex.Count;
            StatusText = string.Format(
                LocalizationManager.T("MetadataExplorer.Status.IndexingCountFormat"),
                _searchIndex.Count);
        }

        foreach (var child in node.Children)
            CollectIndexFromNode(child);
    }

    /// <summary>Снимает пометки <see cref="MetadataTreeNodeViewModel.IsMatch"/> с индексированных узлов.</summary>
    private void ClearHighlights()
    {
        foreach (var node in _searchNodes)
            node.IsMatch = false;
    }

    /// <summary>Раскрывает путь к узлу: все предки от корня получают IsExpanded = true.</summary>
    private static void ExpandPath(MetadataTreeNodeViewModel node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            current.IsExpanded = true;
    }

    /// <summary>
    /// Фильтр по типу: скрывает/показывает узлы типов и объектов по выбранному
    /// <see cref="TypeFilter"/> (пустое значение — «Все типы»). Применяется к уже
    /// загруженным узлам; новые узлы создаются сразу с учётом активного фильтра.
    /// </summary>
    public void ApplyFilters()
    {
        foreach (var root in TreeNodes)
            ApplyFilterToNode(root);
    }

    private void ApplyFilterToNode(MetadataTreeNodeViewModel node)
    {
        switch (node.Kind)
        {
            case MetadataTreeNodeKind.Type:
                node.IsVisible = IsTypeVisible(node.TypeDir);
                break;
            case MetadataTreeNodeKind.Object:
                node.IsVisible = IsTypeVisible(node.ObjectSummary?.TypeDir ?? string.Empty);
                break;
        }

        foreach (var child in node.Children)
            ApplyFilterToNode(child);
    }

    private bool IsTypeVisible(string typeDir)
        => _activeTypeFilter.Length == 0
           || string.Equals(typeDir, _activeTypeFilter, StringComparison.Ordinal);

    /// <summary>Варианты фильтра: «Все типы» + фактические типы выгрузки
    /// (локализованные имена, порядок <see cref="MetadataTypeLocalizer.SortTypes"/>).</summary>
    private static List<MetadataTypeFilterOption> BuildTypeFilterOptions(IReadOnlyList<string> typeDirs)
    {
        var options = new List<MetadataTypeFilterOption>
        {
            new(string.Empty, LocalizationManager.T("MetadataExplorer.TypeFilter.All"))
        };
        foreach (var typeDir in MetadataTypeLocalizer.SortTypes(typeDirs))
            options.Add(new MetadataTypeFilterOption(
                typeDir,
                MetadataTypeLocalizer.GetDisplayName(typeDir, LocalizationManager.T)));
        return options;
    }

    /// <summary>Сбрасывает поисковое состояние при новой загрузке (индекс, подсветка, текст).</summary>
    private void ResetSearchState()
    {
        _indexBuilt = false;
        _searchIndex.Clear();
        _searchNodes.Clear();
        IsSearchActive = false;
        SearchResultsCount = 0;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        SetProperty(ref _searchText, string.Empty, nameof(SearchText));
    }

    // ===================== Внутреннее =====================

    /// <summary>Сброс перед новой загрузкой: шапка, дерево, детали.</summary>
    private void ApplyBeforeLoad()
    {
        void Apply()
        {
            ErrorMessage = string.Empty;
            ConfigurationTitle = string.Empty;
            TreeNodes.Clear();
            ApplyDetails(null);
            StatusText = LocalizationManager.T("MetadataExplorer.Status.Dumping");
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    /// <summary>Ошибка валидации — в статус-строку без исключения.</summary>
    private void ApplyValidationError(string message)
    {
        void Apply()
        {
            ErrorMessage = message;
            StatusText = message;
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    private void ApplyError(Exception ex)
    {
        void Apply()
        {
            ErrorMessage = BuildErrorMessage(ex);
            StatusText = LocalizationManager.T("MetadataExplorer.Errors.OperationFailed");
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    /// <summary>
    /// Применяет результат выгрузки: сохраняет <see cref="MetadataDump"/> (владелец — окно,
    /// удаление в <see cref="Dispose"/>), строит корень дерева. Пустая конфигурация
    /// (нет каталога объектов — разведка 8.3.24+: <c>Configuration.xml</c> + <c>Languages/</c>,
    /// без <c>Configuration/</c> и каталогов типов) → дерево без узлов, статус «конфигурация пуста».
    /// </summary>
    private void ApplyDump(MetadataDump dump)
    {
        _dump?.Delete();
        _dump = dump;

        var typeDirs = MetadataXmlParser.EnumerateTypeDirs(dump.RootPath);
        var root = BuildRootNode(dump, typeDirs);
        var emptyConfig = root is null;
        var filterOptions = BuildTypeFilterOptions(typeDirs);
        var availableTypes = MetadataTypeLocalizer.SortTypes(typeDirs).ToList();

        void Apply()
        {
            ConfigurationTitle = BuildConfigurationTitle(dump);
            TreeNodes.Clear();
            ResetSearchState();
            TypeFilterOptions.Clear();
            foreach (var option in filterOptions)
                TypeFilterOptions.Add(option);
            AvailableTypes = availableTypes;
            TypeFilter = TypeFilterOptions.FirstOrDefault(o => o.Value.Length == 0);
            if (emptyConfig)
            {
                StatusText = LocalizationManager.T("MetadataExplorer.Empty.Configuration");
                return;
            }

            TreeNodes.Add(root!);
            StatusText = string.Format(
                LocalizationManager.T("MetadataExplorer.Status.LoadedFormat"),
                dump.ConfigurationName, dump.ConfigurationVersion);
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    /// <summary>Корень дерева: «Конфигурация имя версия» + «Подсистемы» + «Без подсистемы»
    /// (или один узел «Все объекты» при отсутствии подсистем); null — пустая конфигурация.</summary>
    private MetadataTreeNodeViewModel? BuildRootNode(MetadataDump dump, IReadOnlyList<string> typeDirs)
    {
        var subsystems = MetadataXmlParser.ReadSubsystems(dump.RootPath);

        // Пустая конфигурация: нет ни типов, ни подсистем (нет каталога Configuration/
        // и каталогов типов верхнего уровня — разведка этапа 1).
        if (typeDirs.Count == 0 && subsystems.Count == 0)
            return null;

        var root = new MetadataTreeNodeViewModel(
            MetadataTreeNodeKind.Root,
            string.Format(LocalizationManager.T("MetadataExplorer.ConfigNodeFormat"),
                dump.ConfigurationName, dump.ConfigurationVersion));

        if (subsystems.Count == 0)
        {
            root.AddChild(CreateTypeGroupNode(
                dump.RootPath,
                LocalizationManager.T("MetadataExplorer.AllObjects"),
                typeDirs,
                onlyNames: null));
        }
        else
        {
            root.AddChild(CreateSubsystemsNode(dump.RootPath, subsystems));
            root.AddChild(CreateNoSubsystemNode(dump.RootPath, typeDirs, subsystems));
        }

        return root;
    }

    /// <summary>Узел «Подсистемы»: верхний уровень подсистем грузится лениво при раскрытии.</summary>
    private MetadataTreeNodeViewModel CreateSubsystemsNode(
        string dumpRoot, IReadOnlyList<MetadataSubsystem> subsystems)
    {
        return new MetadataTreeNodeViewModel(
            MetadataTreeNodeKind.Subsystem,
            LocalizationManager.T("MetadataExplorer.Subsystems"),
            childrenLoader: () =>
            {
                var result = new List<MetadataTreeNodeViewModel>();
                foreach (var sub in subsystems)
                    result.Add(CreateSubsystemNode(dumpRoot, sub));
                return result;
            },
            hasChildren: subsystems.Count > 0);
    }

    /// <summary>Узел подсистемы: вложенные подсистемы + типы из состава Content (лениво).</summary>
    private MetadataTreeNodeViewModel CreateSubsystemNode(string dumpRoot, MetadataSubsystem sub)
    {
        var displayName = string.IsNullOrWhiteSpace(sub.Synonym) ? sub.Name : sub.Synonym;
        return new MetadataTreeNodeViewModel(
            MetadataTreeNodeKind.Subsystem,
            displayName,
            childrenLoader: () => BuildSubsystemChildren(dumpRoot, sub),
            hasChildren: sub.Children.Count > 0 || sub.ContentKeys.Count > 0,
            subsystem: sub);
    }

    /// <summary>Дети подсистемы: вложенные подсистемы + узлы типов по составу Content.</summary>
    private List<MetadataTreeNodeViewModel> BuildSubsystemChildren(string dumpRoot, MetadataSubsystem sub)
    {
        var result = new List<MetadataTreeNodeViewModel>();
        foreach (var child in sub.Children)
            result.Add(CreateSubsystemNode(dumpRoot, child));

        // Группируем полные имена состава «Тип.Имя» по типу.
        var byType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var key in sub.ContentKeys)
        {
            var split = SplitContentKey(key);
            if (split is null)
                continue;
            if (!byType.TryGetValue(split.Value.TypeDir, out var names))
                byType[split.Value.TypeDir] = names = new List<string>();
            names.Add(split.Value.Name);
        }

        foreach (var typeDir in MetadataTypeLocalizer.SortTypes(byType.Keys))
            result.Add(CreateTypeNode(dumpRoot, typeDir, byType[typeDir]));

        return result;
    }

    /// <summary>Узел «Без подсистемы»: типы → объекты, не входящие ни в одну подсистему (лениво).</summary>
    private MetadataTreeNodeViewModel CreateNoSubsystemNode(
        string dumpRoot, IReadOnlyList<string> typeDirs, IReadOnlyList<MetadataSubsystem> subsystems)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        CollectContentKeys(subsystems, knownKeys);

        return new MetadataTreeNodeViewModel(
            MetadataTreeNodeKind.NoSubsystemGroup,
            LocalizationManager.T("MetadataExplorer.NoSubsystems"),
            childrenLoader: () =>
            {
                var result = new List<MetadataTreeNodeViewModel>();
                foreach (var typeDir in typeDirs)
                {
                    var objects = MetadataXmlParser.EnumerateObjects(dumpRoot, typeDir)
                        .Where(o => !knownKeys.Contains($"{typeDir}.{o.Name}"))
                        .ToList();
                    if (objects.Count == 0)
                        continue;
                    result.Add(CreateTypeNode(dumpRoot, typeDir, objects.Select(o => o.Name).ToList()));
                }
                return result;
            },
            hasChildren: typeDirs.Count > 0);
    }

    /// <summary>Группа типов («Все объекты»): типы → объекты (лениво).</summary>
    private MetadataTreeNodeViewModel CreateTypeGroupNode(
        string dumpRoot, string displayName, IReadOnlyList<string> typeDirs, IReadOnlyList<string>? onlyNames)
    {
        return new MetadataTreeNodeViewModel(
            MetadataTreeNodeKind.NoSubsystemGroup,
            displayName,
            childrenLoader: () =>
            {
                var result = new List<MetadataTreeNodeViewModel>();
                foreach (var typeDir in typeDirs)
                    result.Add(CreateTypeNode(dumpRoot, typeDir, onlyNames));
                return result;
            },
            hasChildren: typeDirs.Count > 0);
    }

    /// <summary>Узел типа: локализованное имя через <see cref="MetadataTypeLocalizer"/>,
    /// счётчик «(N)»; объекты подгружаются лениво при раскрытии.</summary>
    private MetadataTreeNodeViewModel CreateTypeNode(
        string dumpRoot, string typeDir, IReadOnlyList<string>? onlyNames = null)
    {
        var summaries = MetadataXmlParser.EnumerateObjects(dumpRoot, typeDir);
        if (onlyNames is { Count: > 0 })
        {
            var names = new HashSet<string>(onlyNames, StringComparer.Ordinal);
            summaries = summaries.Where(s => names.Contains(s.Name)).ToList();
        }

        var typeNode = new MetadataTreeNodeViewModel(
            MetadataTreeNodeKind.Type,
            MetadataTypeLocalizer.GetDisplayName(typeDir, LocalizationManager.T),
            countText: summaries.Count > 0 ? $"({summaries.Count})" : string.Empty,
            childrenLoader: () => summaries.Select(CreateObjectNode),
            hasChildren: summaries.Count > 0,
            typeDir: typeDir);
        typeNode.IsVisible = IsTypeVisible(typeDir);
        return typeNode;
    }

    /// <summary>Узел объекта метаданных (лист дерева; детали — по запросу при выборе).</summary>
    private MetadataTreeNodeViewModel CreateObjectNode(MetadataObjectSummary summary)
    {
        var node = new MetadataTreeNodeViewModel(MetadataTreeNodeKind.Object, summary.Name, summary: summary);
        node.IsVisible = IsTypeVisible(summary.TypeDir);
        return node;
    }

    /// <summary>Рекурсивно собирает полные имена состава всех подсистем («Тип.Имя»).</summary>
    private static void CollectContentKeys(IReadOnlyList<MetadataSubsystem> subsystems, ISet<string> target)
    {
        foreach (var sub in subsystems)
        {
            foreach (var key in sub.ContentKeys)
                target.Add(key);
            CollectContentKeys(sub.Children, target);
        }
    }

    /// <summary>Разбирает полное имя объекта состава подсистемы «Тип.Имя» (первая точка — разделитель).</summary>
    private static (string TypeDir, string Name)? SplitContentKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var dot = key.IndexOf('.');
        if (dot <= 0 || dot == key.Length - 1)
            return null;
        return (key.Substring(0, dot), key.Substring(dot + 1));
    }

    /// <summary>Шапка «имя версия»; пустая версия — только имя.</summary>
    private static string BuildConfigurationTitle(MetadataDump dump)
    {
        if (string.IsNullOrWhiteSpace(dump.ConfigurationVersion))
            return dump.ConfigurationName;
        return $"{dump.ConfigurationName} {dump.ConfigurationVersion}";
    }

    /// <summary>Применяет детали объекта на панель справа (null — очистка).</summary>
    private void ApplyDetails(MetadataObjectDetails? details)
    {
        void Apply()
        {
            if (details is null)
            {
                DetailsName = string.Empty;
                DetailsSynonym = string.Empty;
                DetailsComment = string.Empty;
                DetailsAttributesText = string.Empty;
                DetailsTabularSectionsText = string.Empty;
                DetailsFormsText = string.Empty;
                DetailsCommandsText = string.Empty;
                DetailsHierarchicalText = string.Empty;
                DetailsSizeText = string.Empty;
                DetailsRelPath = string.Empty;
                HasDetails = false;
                return;
            }

            DetailsName = details.Name;
            DetailsSynonym = details.Synonym;
            DetailsComment = details.Comment;
            DetailsAttributesText = details.AttributeCount.ToString();
            DetailsTabularSectionsText = details.TabularSectionCount.ToString();
            DetailsFormsText = details.FormCount.ToString();
            DetailsCommandsText = details.CommandCount.ToString();
            DetailsHierarchicalText = details.IsHierarchical || details.IsOrderedHierarchical
                ? LocalizationManager.T("Common.Yes")
                : LocalizationManager.T("Common.No");
            DetailsSizeText = Infobase.FormatSize(details.TotalBytes);
            DetailsRelPath = details.RelPath;
            HasDetails = true;
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    /// <summary>Версия платформы для распаковки .cf (образец RepositoryBrowserViewModel.ResolvePlatformVersion).</summary>
    private string ResolvePlatformVersion()
    {
        if (SelectedBase is { PlatformVersion: not null and not "" })
            return SelectedBase.PlatformVersion;

        try
        {
            var settings = AppServices.GetRequiredService<IInfobaseRepository>().LoadSettings();
            if (!string.IsNullOrWhiteSpace(settings?.LastFileCreatePlatformVersion))
                return settings.LastFileCreatePlatformVersion;
        }
        catch
        {
            // Контейнер не настроен (тесты) — переходим к установленным платформам.
        }

        try
        {
            var installed = PlatformVersionService.FindInstalledVersions();
            if (installed.Count > 0)
                return installed[0];
        }
        catch
        {
            // Платформы не найдены — вернём пустую строку (сервис сообщит об ошибке).
        }

        return string.Empty;
    }

    private void ReportStage(string stage) => StageChanged?.Invoke(stage);

    private bool TryEnterBusy()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return false;
        NotifyUi(nameof(IsBusy));
        (_loadCommand as RelayCommand)?.RaiseCanExecuteChanged();
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        NotifyUi(nameof(IsBusy));
        (_loadCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void NotifyUi(string propertyName)
    {
        if (_dispatchToUi is null)
            OnPropertyChanged(propertyName);
        else
            _dispatchToUi(() => OnPropertyChanged(propertyName));
    }

    private static string BuildErrorMessage(Exception ex)
    {
        var message = ex.Message;
        return string.IsNullOrWhiteSpace(message)
            ? LocalizationManager.T("MetadataExplorer.Errors.Unknown")
            : message;
    }
}

/// <summary>
/// Вариант фильтра по типу в ComboBox обозревателя (этап 3): пустой
/// <see cref="Value"/> означает «Все типы», иначе — каталог типа метаданных
/// в выгрузке (например <c>Catalog</c>); <see cref="DisplayName"/> — локализованное имя.
/// </summary>
public sealed record MetadataTypeFilterOption(string Value, string DisplayName);