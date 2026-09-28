using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel окна «Хранилище конфигурации…» (0.3.9.128, этап 2 цикла 0.3.9.127–0.3.9.130):
/// подключение к хранилищу конфигурации 1С выбранной базы (адрес и логин — readonly из
/// свойств базы, пароль — в памяти окна и НЕ сохраняется на диск), список версий хранилища
/// (номер/дата/автор/комментарий; при недоступности текстового формата отчёта по истории —
/// одна запись «актуальная версия», см. <c>RepositoryStorageService.GetHistoryAsync</c>) и
/// состав выбранной версии (тип/имя/владелец). Чистый .NET без платформенных зависимостей —
/// обе платформы (WPF и Avalonia); окна только привязываются. Образец —
/// ServerMonitorViewModel / ProcessInspectorViewModel.
/// </summary>
public sealed class RepositoryBrowserViewModel : ViewModelBase
{
    private readonly Infobase _infobase;
    private readonly IRepositoryStorageService _service;
    private readonly IDialogService _dialogs;
    private readonly Action<Action>? _dispatchToUi;

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

    /// <param name="infobase">Выбранная в главном окне база (с заполненным адресом хранилища).</param>
    /// <param name="service">Сервис операций с хранилищем (история, состав версии).</param>
    /// <param name="dialogs">Диалоги (сообщения об ошибках и подтверждения действий этапов 3–4).</param>
    /// <param name="dispatchToUi">
    /// Доставка применения результатов в UI-поток (передаёт окно); null — результаты
    /// применяются прямо из рабочего потока (тесты).
    /// </param>
    public RepositoryBrowserViewModel(
        Infobase infobase,
        IRepositoryStorageService service,
        IDialogService dialogs,
        Action<Action>? dispatchToUi = null)
    {
        _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatchToUi = dispatchToUi;

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
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        NotifyUi(nameof(IsBusy));
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
}