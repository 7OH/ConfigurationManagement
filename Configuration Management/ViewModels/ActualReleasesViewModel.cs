using System;
using System.Collections.ObjectModel;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Модель окна «Актуальные релизы» (<c>ActualReleasesWindow</c>, ALT+F9): список
/// отслеживаемых типовых конфигураций и команда пакетной проверки. Сетевые операции
/// выполняются окном через <c>IOneCUpdatesService</c>; модель управляет состоянием строк.
/// </summary>
public class ActualReleasesViewModel : ViewModelBase
{
    /// <summary>Отслеживаемые конфигурации (строки результатов проверки).</summary>
    public ObservableCollection<ActualReleaseRowViewModel> Rows { get; } = new();

    private bool _isCheckingAll;

    /// <summary>Выполняется ли пакетная проверка всех конфигураций.</summary>
    public bool IsCheckingAll
    {
        get => _isCheckingAll;
        set
        {
            if (SetProperty(ref _isCheckingAll, value))
                OnPropertyChanged(nameof(CanCheckAll));
        }
    }

    /// <summary>Доступна ли пакетная проверка (нет активной и есть строки).</summary>
    public bool CanCheckAll => !_isCheckingAll && Rows.Count > 0;

    /// <summary>Команда пакетной проверки всех конфигураций (реализация — в окне).</summary>
    public RelayCommand CheckAllCommand { get; }

    /// <param name="checkAll">Действие «проверить все».</param>
    public ActualReleasesViewModel(Action checkAll)
    {
        CheckAllCommand = new RelayCommand(checkAll, () => CanCheckAll);
    }

    /// <summary>Обновляет доступность команд пакетной проверки после изменения состава списка.</summary>
    public void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanCheckAll));
        CheckAllCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>
/// Строка окна «Актуальные релизы»: типовая конфигурация и результат проверки её
/// каталога релизов, а также команды точечной проверки и загрузки.
/// </summary>
public class ActualReleaseRowViewModel : ViewModelBase
{
    /// <summary>Типовая конфигурация строки.</summary>
    public OneCConfigType Config { get; }

    /// <summary>Редакция (каталог релизов) для проверки. Может быть null.</summary>
    public OneCConfigEdition? Edition { get; }

    /// <summary>Отображаемое имя конфигурации (с редакцией, если она выбрана).</summary>
    public string Name =>
        Edition is not null && !string.IsNullOrWhiteSpace(Edition.ToString())
            ? $"{Config.Name} ({Edition})"
            : Config.Name;

    private string _latestVersion = string.Empty;

    /// <summary>Последняя версия, обнаруженная в каталоге релизов.</summary>
    public string LatestVersion
    {
        get => _latestVersion;
        set => SetProperty(ref _latestVersion, value ?? string.Empty);
    }

    private ConfigUpdateStatus _status;

    /// <summary>Итоговый статус проверки.</summary>
    public ConfigUpdateStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(HasNewer));
                OnPropertyChanged(nameof(IsFinished));
                OnPropertyChanged(nameof(CanDownload));
            }
        }
    }

    private string _url = string.Empty;

    /// <summary>Адрес каталога релизов.</summary>
    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value ?? string.Empty))
                OnPropertyChanged(nameof(CanDownload));
        }
    }

    private string _error = string.Empty;

    /// <summary>Текст ошибки (ключ локализации либо свободный текст).</summary>
    public string Error
    {
        get => _error;
        set => SetProperty(ref _error, value ?? string.Empty);
    }

    private bool _isChecking;

    /// <summary>Выполняется ли сетевая проверка строки.</summary>
    public bool IsChecking
    {
        get => _isChecking;
        set
        {
            if (SetProperty(ref _isChecking, value))
                OnPropertyChanged(nameof(CanDownload));
        }
    }

    private bool _isDownloading;

    /// <summary>Идёт ли загрузка дистрибутива.</summary>
    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (SetProperty(ref _isDownloading, value))
                OnPropertyChanged(nameof(CanDownload));
        }
    }

    private double _progress;

    /// <summary>Прогресс загрузки (0..1).</summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    /// <summary>True — обнаружен новый релиз.</summary>
    public bool HasNewer => _status == ConfigUpdateStatus.NewerAvailable;

    /// <summary>True — проверка завершена (не Unknown).</summary>
    public bool IsFinished => _status != ConfigUpdateStatus.Unknown;

    /// <summary>Доступна ли загрузка.</summary>
    public bool CanDownload => !_isChecking && !_isDownloading && HasNewer && !string.IsNullOrWhiteSpace(_url);

    /// <summary>Команда точечной проверки строки.</summary>
    public RelayCommand CheckCommand { get; }

    /// <summary>Команда загрузки дистрибутива.</summary>
    public RelayCommand DownloadCommand { get; }

    /// <param name="config">Типовая конфигурация.</param>
    /// <param name="edition">Редакция (каталог релизов).</param>
    /// <param name="check">Действие «проверить строку».</param>
    /// <param name="download">Действие «начать загрузку».</param>
    public ActualReleaseRowViewModel(
        OneCConfigType config,
        OneCConfigEdition? edition,
        Action<ActualReleaseRowViewModel> check,
        Action<ActualReleaseRowViewModel> download)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Edition = edition;
        CheckCommand = new RelayCommand(() => check(this), () => !IsChecking && !IsDownloading);
        DownloadCommand = new RelayCommand(() => download(this), () => CanDownload);
    }

    /// <summary>Применяет результат сетевой проверки к строке.</summary>
    public void ApplyResult(ConfigUpdateCheckResult result)
    {
        LatestVersion = result.LatestVersion;
        Status = result.Status;
        Url = result.Url;
        Error = result.Error;
    }
}