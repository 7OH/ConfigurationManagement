using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel окна «Хранилище конфигурации…» (цикл 0.3.9.127–0.3.9.130):
/// подключение к хранилищу конфигурации 1С выбранной базы (адрес и логин — readonly из
/// свойств базы, пароль — в памяти окна и НЕ сохраняется на диск), список версий хранилища
/// (номер/дата/автор/комментарий; при недоступности текстового формата отчёта по истории —
/// одна запись «актуальная версия», см. <c>RepositoryStorageService.GetHistoryAsync</c>) и
/// состав выбранной версии (тип/имя/владелец). Этап 3: действия над версией — сравнение
/// с текущей конфигурацией базы (<see cref="CompareWithBaseAsync"/>, BaseVsCf), сравнение
/// двух версий между собой (<see cref="CompareVersionsAsync"/>, CfVsCf, выбранная ↔
/// предыдущая; недоступно при ограниченной истории) и выгрузка версии в .cf
/// (<see cref="DumpVersionToCfAsync"/>). Чистый .NET без платформенных зависимостей —
/// обе платформы (WPF и Avalonia); окна только привязываются и открывают окна результата.
/// Образец — ServerMonitorViewModel / ProcessInspectorViewModel.
/// </summary>
public sealed class RepositoryBrowserViewModel : ViewModelBase
{
    private readonly Infobase _infobase;
    private readonly IRepositoryStorageService _service;
    private readonly IDialogService _dialogs;
    private readonly Action<Action>? _dispatchToUi;

    /// <summary>Открытие окна отчёта о сравнении (передаёт окно; null — тесты/без результата).</summary>
    private readonly Action<ConfigurationDiffResult>? _showDiffResult;

    /// <summary>Выполнение сравнения (реальный <see cref="ConfigurationDiffService"/> по умолчанию; делегат — для тестов).</summary>
    private readonly Func<ConfigurationDiffRequest, IProgress<string>?, Task<ConfigurationDiffResult>> _compareAsync;

    /// <summary>Сохранение списка баз после записи в историю запусков (передаёт окно; null — тесты).</summary>
    private readonly Action? _persistChanges;

    private int _busy;
    private int _objectsBusy;
    private string _password;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasConnected;
    private bool _isObjectsLoading;
    private RepositoryVersionRow? _selectedVersion;

    private ICommand? _connectCommand;
    private ICommand? _refreshCommand;
    private ICommand? _selectVersionCommand;
    private ICommand? _compareWithBaseCommand;
    private ICommand? _compareVersionsCommand;
    private ICommand? _dumpVersionToCfCommand;

    /// <param name="infobase">Выбранная в главном окне база (с заполненным адресом хранилища).</param>
    /// <param name="service">Сервис операций с хранилищем (история, состав версии).</param>
    /// <param name="dialogs">Диалоги (сообщения об ошибках, выбор файла .cf для выгрузки).</param>
    /// <param name="dispatchToUi">
    /// Доставка применения результатов в UI-поток (передаёт окно); null — результаты
    /// применяются прямо из рабочего потока (тесты).
    /// </param>
    /// <param name="showDiffResult">
    /// Открытие окна отчёта о сравнении (передаёт окно); null — результат не показывается
    /// (тесты проверяют сам вызов).
    /// </param>
    /// <param name="compareAsync">
    /// Выполнение сравнения; по умолчанию — реальный <see cref="ConfigurationDiffService"/>.
    /// Делегат подменяется в тестах, чтобы не запускать платформу 1С (сравнение покрыто
    /// <c>ConfigurationDiffTests</c>).
    /// </param>
    /// <param name="persistChanges">
    /// Сохранение списка баз после записи в историю запусков (передаёт окно); null — без
    /// сохранения (тесты).
    /// </param>
    public RepositoryBrowserViewModel(
        Infobase infobase,
        IRepositoryStorageService service,
        IDialogService dialogs,
        Action<Action>? dispatchToUi = null,
        Action<ConfigurationDiffResult>? showDiffResult = null,
        Func<ConfigurationDiffRequest, IProgress<string>?, Task<ConfigurationDiffResult>>? compareAsync = null,
        Action? persistChanges = null)
    {
        _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatchToUi = dispatchToUi;
        _showDiffResult = showDiffResult;
        _compareAsync = compareAsync ?? ((request, progress) => new ConfigurationDiffService().CompareAsync(request, progress));
        _persistChanges = persistChanges;

        InfobaseAuthResolver.ResolveRepository(infobase, out _, out var repositoryPassword);
        _password = repositoryPassword ?? string.Empty;
    }

    /// <summary>Смена этапа операции (текст для окна прогресса). Может вызываться из фонового потока.</summary>
    public event Action<string>? StageChanged;

    // ===================== Подключение =====================

    /// <summary>Адрес хранилища для отображения (readonly, из настроек базы: «сервер\имя»).</summary>
    public string RepositoryAddress => _infobase.Repository.AddressDisplay;

    /// <summary>Логин пользователя хранилища (readonly, из настроек базы через <see cref="InfobaseAuthResolver"/>).</summary>
    public string UserName
    {
        get
        {
            InfobaseAuthResolver.ResolveRepository(_infobase, out var user, out _);
            return user ?? string.Empty;
        }
    }

    /// <summary>
    /// Пароль пользователя хранилища. Живёт ТОЛЬКО в памяти окна: предзаполнен паролем
    /// базы, редактируется пользователем (в UI передаётся из PasswordBox при подключении),
    /// на диск НЕ сохраняется — новых полей в AppSettings нет.
    /// </summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value ?? string.Empty);
    }

    /// <summary>Выполняется ли сейчас подключение/перезагрузка истории (флаг занятости, образец ProcessInspectorViewModel:80).</summary>
    public bool IsBusy => _busy == 1;

    /// <summary>Загружается ли сейчас состав выбранной версии.</summary>
    public bool IsObjectsLoading => _isObjectsLoading;

    // ===================== Коллекции =====================

    /// <summary>Версии хранилища (после подключения).</summary>
    public ObservableCollection<RepositoryVersionRow> Versions { get; } = new();

    /// <summary>Объекты состава выбранной версии.</summary>
    public ObservableCollection<RepositoryObjectRow> Objects { get; } = new();

    /// <summary>
    /// Выбранная версия. При смене (и после подключения) загружается её состав
    /// через <see cref="SelectVersionCommand"/> → <see cref="LoadObjectsAsync"/>.
    /// </summary>
    public RepositoryVersionRow? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!SetProperty(ref _selectedVersion, value))
                return;
            if (value is not null && HasConnected)
                SelectVersionCommand.Execute(value);
            // Доступность действий зависит от выбранной версии — пересчитываем CanExecute.
            RaiseCommandsCanExecuteChanged();
        }
    }

    // ===================== Команды =====================

    /// <summary>«Подключить»: история версий хранилища через <see cref="IRepositoryStorageService.GetHistoryAsync"/>.</summary>
    public ICommand ConnectCommand =>
        _connectCommand ??= new RelayCommand(async () => await ConnectAsync());

    /// <summary>«Обновить»: перезагрузка истории (после подключения).</summary>
    public ICommand RefreshCommand =>
        _refreshCommand ??= new RelayCommand(async () => await RefreshAsync());

    /// <summary>
    /// «Выбор версии»: загрузка состава версии через
    /// <see cref="IRepositoryStorageService.LoadVersionObjectsAsync"/> с флагом занятости;
    /// ошибки — в статус-строку, окно не ронять.
    /// </summary>
    public ICommand SelectVersionCommand =>
        _selectVersionCommand ??= new RelayCommand(p =>
        {
            if (p is RepositoryVersionRow row)
                _ = LoadObjectsAsync(row);
        });

    /// <summary>
    /// «Сравнить с базой» (этап 3): выбранная версия хранилища ↔ текущая конфигурация базы
    /// (режим <see cref="ConfigDiffMode.BaseVsCf"/>). Доступно после подключения при выбранной
    /// версии; версия выгружается в .cf во временный каталог <c>%TEMP%\cm_repo_<guid></c>
    /// (удаляется в finally), сравнение выполняет <see cref="ConfigurationDiffService"/>.
    /// </summary>
    public ICommand CompareWithBaseCommand =>
        _compareWithBaseCommand ??= new RelayCommand(async () => await CompareWithBaseAsync(),
            () => HasConnected && SelectedVersion is not null && !IsBusy);

    /// <summary>
    /// «Сравнить версии» (этап 3): выбранная версия ↔ предыдущая версия хранилища
    /// (режим <see cref="ConfigDiffMode.CfVsCf"/>). Недоступна при ограниченной истории
    /// (<see cref="IsHistoryLimited"/>) — нет номеров двух произвольных версий.
    /// </summary>
    public ICommand CompareVersionsCommand =>
        _compareVersionsCommand ??= new RelayCommand(async () => await CompareVersionsAsync(),
            () => HasConnected && SelectedVersion is not null && !IsBusy && !IsHistoryLimited);

    /// <summary>
    /// «Выгрузить в .cf» (этап 3): выбранная версия хранилища → файл .cf через диалог
    /// сохранения (образец имени <c><ИмяБД>_v<N>.cf</c>); после успеха —
    /// статус и запись в историю запусков базы (<see cref="Infobase.AddLaunchHistory"/>).
    /// </summary>
    public ICommand DumpVersionToCfCommand =>
        _dumpVersionToCfCommand ??= new RelayCommand(async () => await DumpVersionToCfAsync(),
            () => HasConnected && SelectedVersion is not null && !IsBusy);

    // ===================== Статус =====================

    /// <summary>Строка состояния («Подключение…», «Версий: N», ошибки).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Подробное сообщение последней ошибки (для статус-строки и подсказки).</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Успешно ли установлено подключение (история получена).</summary>
    public bool HasConnected
    {
        get => _hasConnected;
        private set => SetProperty(ref _hasConnected, value);
    }

    /// <summary>
    /// История ограничена одной записью «актуальная версия» (текстовый формат отчёта
    /// по истории хранилища недоступен для чтения — ограничение разведки этапа 1).
    /// </summary>
    public bool IsHistoryLimited => Versions.Count == 1 && Versions[0].Number < 0;

    // ===================== Действия =====================

    /// <summary>
    /// «Подключить»: получить историю версий хранилища (<see cref="IRepositoryStorageService.GetHistoryAsync"/>).
    /// Ошибки не роняют окно — пишутся в статус-строку/ErrorMessage.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (!TryEnterBusy())
            return;

        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("RepositoryBrowser.Status.Connecting");

            var progress = new Progress<string>(ReportStage);
            var versions = await _service
                .GetHistoryAsync(WithEffectivePassword(), progress: progress)
                .ConfigureAwait(false);

            ApplyConnection(versions, connected: true);
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

    /// <summary>«Обновить»: перезагрузка истории; без подключения — no-op.</summary>
    public async Task RefreshAsync()
    {
        if (!HasConnected)
            return;
        await ConnectAsync();
    }

    /// <summary>
    /// «Выбор версии»: загрузка состава версии (<see cref="IRepositoryStorageService.LoadVersionObjectsAsync"/>;
    /// служебная запись «актуальная версия» выгружается без номера — DumpCfg без -v).
    /// Флаг занятости через Interlocked; ошибки — статус-строка, окно не ронять.
    /// </summary>
    public async Task LoadObjectsAsync(RepositoryVersionRow? row)
    {
        if (row is null || !HasConnected)
            return;
        if (Interlocked.Exchange(ref _objectsBusy, 1) == 1)
            return;

        try
        {
            NotifyObjectsLoading(true);
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("RepositoryBrowser.Status.ObjectsLoading");

            int? version = row.Number >= 0 ? row.Number : null;
            var progress = new Progress<string>(ReportStage);
            var objects = await _service
                .LoadVersionObjectsAsync(WithEffectivePassword(), version, _infobase.PlatformVersion, progress)
                .ConfigureAwait(false);

            ApplyObjects(objects, string.Format(
                LocalizationManager.T("RepositoryBrowser.Status.ObjectsLoadedFormat"),
                row.NumberText, objects.Count));
        }
        catch (Exception ex)
        {
            ApplyObjects(Array.Empty<RepositoryObjectInfo>(),
                LocalizationManager.T("RepositoryBrowser.Status.ObjectsLoadFailed"),
                error: BuildErrorMessage(ex));
        }
        finally
        {
            Interlocked.Exchange(ref _objectsBusy, 0);
            NotifyObjectsLoading(false);
        }
    }

    // ===================== Действия (этап 3): сравнение и выгрузка =====================

    /// <summary>
    /// «Сравнить с базой»: выбранная версия хранилища ↔ текущая конфигурация базы.
    /// Версия выгружается через <see cref="IRepositoryStorageService.DumpVersionToCfAsync"/>
    /// во временный каталог <c>%TEMP%\cm_repo_<guid></c> (удаляется в finally), затем
    /// <see cref="ConfigurationDiffService.CompareAsync"/> (режим <see cref="ConfigDiffMode.BaseVsCf"/>,
    /// LeftLabel = «Хранилище vN»/«Актуальная версия», RightLabel = «База <имя>»).
    /// Результат открывает окно отчёта (колбэк окна); ошибки — статус-строка + ShowWarning.
    /// </summary>
    public async Task CompareWithBaseAsync()
    {
        var row = SelectedVersion;
        if (row is null || !HasConnected)
            return;
        if (!TryEnterBusy())
            return;

        string? tmpRoot = null;
        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("RepositoryBrowser.Status.Comparing");
            var progress = new Progress<string>(ReportStage);

            tmpRoot = CreateTempRoot();
            var cfPath = Path.Combine(tmpRoot, "version.cf");
            await _service
                .DumpVersionToCfAsync(WithEffectivePassword(), VersionOf(row), cfPath, progress)
                .ConfigureAwait(false);

            var request = new ConfigurationDiffRequest
            {
                Mode = ConfigDiffMode.BaseVsCf,
                Base = _infobase,
                RightCfPath = cfPath,
                PlatformVersion = ResolvePlatformVersion(),
                LeftLabel = VersionLabel(row),
                RightLabel = BaseLabel()
            };

            var result = await _compareAsync(request, progress).ConfigureAwait(false);
            _showDiffResult?.Invoke(result);
            ApplyStatus(string.Format(
                LocalizationManager.T("RepositoryBrowser.Status.CompareOkFormat"),
                result.LeftLabel, result.RightLabel));
        }
        catch (Exception ex)
        {
            ApplyActionError(ex, LocalizationManager.T("RepositoryBrowser.Status.CompareFailed"));
        }
        finally
        {
            ExitBusy();
            TryDeleteDirectory(tmpRoot);
        }
    }

    /// <summary>
    /// «Сравнить версии»: выбранная версия ↔ предыдущая версия хранилища (режим
    /// <see cref="ConfigDiffMode.CfVsCf"/> — две выгрузки .cf во временный каталог).
    /// Недоступна при ограниченной истории (<see cref="IsHistoryLimited"/>); при отсутствии
    /// предыдущей версии (выбрана самая старая) — предупреждение «нужны две версии».
    /// </summary>
    public async Task CompareVersionsAsync()
    {
        var row = SelectedVersion;
        if (row is null || !HasConnected)
            return;

        if (IsHistoryLimited)
        {
            _dialogs.ShowWarning(
                LocalizationManager.T("RepositoryBrowser.NoTwoVersions"),
                LocalizationManager.T("RepositoryBrowser.Title"));
            return;
        }

        var previous = FindPreviousVersion(row);
        if (previous is null)
        {
            _dialogs.ShowWarning(
                LocalizationManager.T("RepositoryBrowser.NoTwoVersions"),
                LocalizationManager.T("RepositoryBrowser.Title"));
            return;
        }

        if (!TryEnterBusy())
            return;

        string? tmpRoot = null;
        try
        {
            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("RepositoryBrowser.Status.Comparing");
            var progress = new Progress<string>(ReportStage);

            tmpRoot = CreateTempRoot();
            var leftCf = Path.Combine(tmpRoot, "left.cf");
            var rightCf = Path.Combine(tmpRoot, "right.cf");
            await _service
                .DumpVersionToCfAsync(WithEffectivePassword(), VersionOf(previous), leftCf, progress)
                .ConfigureAwait(false);
            await _service
                .DumpVersionToCfAsync(WithEffectivePassword(), VersionOf(row), rightCf, progress)
                .ConfigureAwait(false);

            var request = new ConfigurationDiffRequest
            {
                Mode = ConfigDiffMode.CfVsCf,
                LeftCfPath = leftCf,
                RightCfPath = rightCf,
                PlatformVersion = ResolvePlatformVersion(),
                LeftLabel = VersionLabel(previous),
                RightLabel = VersionLabel(row)
            };

            var result = await _compareAsync(request, progress).ConfigureAwait(false);
            _showDiffResult?.Invoke(result);
            ApplyStatus(string.Format(
                LocalizationManager.T("RepositoryBrowser.Status.CompareOkFormat"),
                result.LeftLabel, result.RightLabel));
        }
        catch (Exception ex)
        {
            ApplyActionError(ex, LocalizationManager.T("RepositoryBrowser.Status.CompareFailed"));
        }
        finally
        {
            ExitBusy();
            TryDeleteDirectory(tmpRoot);
        }
    }

    /// <summary>
    /// «Выгрузить в .cf»: выбранная версия → файл .cf. Путь выбирается через диалог
    /// сохранения (<see cref="IDialogService.SaveFileDialog"/>; предлагаемое имя —
    /// <c><ИмяБД>_v<N>.cf</c>, для актуальной версии — <c><ИмяБД>_current.cf</c>);
    /// при отмене — ничего не делается. После успеха — статус-строка и запись в историю
    /// запусков базы (<see cref="Infobase.AddLaunchHistory"/>, путь и время) + сохранение
    /// списка баз (колбэк окна).
    /// </summary>
    public async Task DumpVersionToCfAsync()
    {
        var row = SelectedVersion;
        if (row is null || !HasConnected)
            return;
        if (!TryEnterBusy())
            return;

        try
        {
            var suffix = row.Number >= 0 ? $"v{row.Number}" : "current";
            var defaultName = $"{SafeFileName(_infobase.Name)}_{suffix}.cf";
            var filePath = _dialogs.SaveFileDialog(
                LocalizationManager.T("RepositoryBrowser.DumpCfTitle"),
                defaultName,
                LocalizationManager.T("RepositoryBrowser.CfFilter"));
            if (string.IsNullOrWhiteSpace(filePath))
                return; // пользователь отменил выбор файла

            ErrorMessage = string.Empty;
            StatusText = LocalizationManager.T("RepositoryBrowser.Status.Dumping");
            var progress = new Progress<string>(ReportStage);
            await _service
                .DumpVersionToCfAsync(WithEffectivePassword(), VersionOf(row), filePath, progress)
                .ConfigureAwait(false);

            _infobase.AddLaunchHistory("RepositoryBrowser",
                string.Format(LocalizationManager.T("RepositoryBrowser.HistoryDumpFormat"),
                    filePath, DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")));
            _persistChanges?.Invoke();

            ApplyStatus(string.Format(
                LocalizationManager.T("RepositoryBrowser.Status.DumpOkFormat"), filePath));
        }
        catch (Exception ex)
        {
            ApplyActionError(ex, LocalizationManager.T("RepositoryBrowser.Status.DumpFailed"));
        }
        finally
        {
            ExitBusy();
        }
    }

    // ===================== Внутреннее =====================

    /// <summary>
    /// Возвращает базу для операций сервиса с эффективным паролем хранилища: если пароль
    /// изменён в окне, создаётся копия базы с новым паролем (оригинальная база не
    /// модифицируется — настройки не сохраняются). Без изменений — оригинал.
    /// </summary>
    private Infobase WithEffectivePassword()
    {
        var repo = _infobase.Repository;
        if (string.Equals(Password, repo.Password ?? string.Empty, StringComparison.Ordinal))
            return _infobase;

        return new Infobase
        {
            Id = _infobase.Id,
            Name = _infobase.Name,
            PlatformVersion = _infobase.PlatformVersion,
            Connection = _infobase.Connection,
            Repository = new RepositorySettings
            {
                Server = repo.Server,
                RepositoryName = repo.RepositoryName,
                User = repo.User,
                Password = Password ?? string.Empty
            }
        };
    }

    private void ApplyConnection(IReadOnlyList<RepositoryVersion> versions, bool connected)
    {
        void Apply()
        {
            Versions.Clear();
            foreach (var version in versions)
                Versions.Add(new RepositoryVersionRow(version));
            SelectedVersion = null;
            Objects.Clear();
            HasConnected = connected;
            ErrorMessage = string.Empty;
            StatusText = versions.Count == 0
                ? LocalizationManager.T("RepositoryBrowser.Empty.Versions")
                : string.Format(LocalizationManager.T("RepositoryBrowser.Status.ConnectedFormat"), versions.Count);
            OnPropertyChanged(nameof(IsHistoryLimited));
            // Доступность действий (сравнение/выгрузка) зависит от состояния подключения и истории.
            RaiseCommandsCanExecuteChanged();
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
            HasConnected = false;
            ErrorMessage = BuildErrorMessage(ex);
            StatusText = LocalizationManager.T("RepositoryBrowser.Status.ConnectFailed");
            RaiseCommandsCanExecuteChanged();
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    private void ApplyObjects(IReadOnlyList<RepositoryObjectInfo> objects, string status, string? error = null)
    {
        void Apply()
        {
            // При ошибке прежний состав не трогаем — только статус и сообщение.
            if (error is null)
            {
                Objects.Clear();
                foreach (var obj in objects)
                    Objects.Add(new RepositoryObjectRow(obj));
            }
            ErrorMessage = error ?? string.Empty;
            StatusText = status;
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    private void ReportStage(string stage) => StageChanged?.Invoke(stage);

    private void NotifyObjectsLoading(bool loading)
    {
        void Apply()
        {
            _isObjectsLoading = loading;
            OnPropertyChanged(nameof(IsObjectsLoading));
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    private bool TryEnterBusy()
    {
        // Одна операция за раз: длительная команда DESIGNER не должна копить очередь (см. ProcessInspectorViewModel:80).
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return false;
        NotifyUi(nameof(IsBusy));
        RaiseCommandsCanExecuteChanged();
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        NotifyUi(nameof(IsBusy));
        RaiseCommandsCanExecuteChanged();
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
            ? LocalizationManager.T("RepositoryBrowser.Errors.Unknown")
            : message;
    }

    /// <summary>Применяет статус-строку в UI-потоке.</summary>
    private void ApplyStatus(string status)
    {
        void Apply() => StatusText = status;

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    /// <summary>
    /// Ошибка действия: статус-строка + предупреждение (IDialogService) на UI-потоке;
    /// окно не роняем. RepositoryStorageException / ConfigurationDiffException дают
    /// человекочитаемый текст, остальные — текст исключения.
    /// </summary>
    private void ApplyActionError(Exception ex, string status)
    {
        void Apply()
        {
            ErrorMessage = BuildErrorMessage(ex);
            StatusText = status;
            _dialogs.ShowWarning(ErrorMessage, LocalizationManager.T("RepositoryBrowser.Title"));
        }

        if (_dispatchToUi is null)
            Apply();
        else
            _dispatchToUi(Apply);
    }

    /// <summary>Пересчитывает CanExecute команд действий (WPF — CommandManager, Avalonia — событие).</summary>
    private void RaiseCommandsCanExecuteChanged()
    {
        (_compareWithBaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (_compareVersionsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (_dumpVersionToCfCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>Номер версии для сервиса: -1 (служебная запись «актуальная версия») → null (DumpCfg без -v).</summary>
    private static int? VersionOf(RepositoryVersionRow row) =>
        row.Number >= 0 ? row.Number : null;

    /// <summary>Подпись стороны сравнения: «Хранилище vN» / «Хранилище — актуальная версия».</summary>
    private static string VersionLabel(RepositoryVersionRow row) => row.Number >= 0
        ? string.Format(LocalizationManager.T("RepositoryBrowser.DiffVersionLabelFormat"), row.Number)
        : LocalizationManager.T("RepositoryBrowser.DiffCurrentVersionLabel");

    /// <summary>Подпись базы для сравнения: «База <имя>».</summary>
    private string BaseLabel() =>
        string.Format(LocalizationManager.T("RepositoryBrowser.DiffBaseLabelFormat"), _infobase.Name);

    /// <summary>Предыдущая версия: строка с максимальным номером, меньшим номера выбранной.</summary>
    private RepositoryVersionRow? FindPreviousVersion(RepositoryVersionRow row)
    {
        RepositoryVersionRow? best = null;
        foreach (var version in Versions)
        {
            if (version.Number < 0 || version.Number >= row.Number)
                continue;
            if (best is null || version.Number > best.Number)
                best = version;
        }
        return best;
    }

    /// <summary>
    /// Версия платформы 1С для сравнения (как в ConfigDiffSetupWindow): платформа базы →
    /// LastFileCreatePlatformVersion из настроек → первая из установленных. Обращение
    /// к настройкам обёрнуто в try/catch (в тестах контейнер не настроен).
    /// </summary>
    private string ResolvePlatformVersion()
    {
        if (!string.IsNullOrWhiteSpace(_infobase.PlatformVersion))
            return _infobase.PlatformVersion;

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
            // Платформы не найдены — вернём пустую строку (сервис сравнения сообщит об ошибке).
        }

        return string.Empty;
    }

    /// <summary>Создаёт временный каталог %TEMP%\cm_repo_<guid> для .cf версий.</summary>
    private static string CreateTempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cm_repo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Безопасное удаление временного каталога (файлы могут быть заняты — не критично).</summary>
    private static void TryDeleteDirectory(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return;
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Временные файлы могут удерживаться процессом 1cv8 — уборка следующего запуска.
        }
    }

    /// <summary>Заменяет символы, недопустимые в имени файла, на '_' (имя базы в имени .cf).</summary>
    private static string SafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "base";
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        return name;
    }
}