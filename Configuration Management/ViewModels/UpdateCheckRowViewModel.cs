using System;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка окна проверки обновлений конфигурации для выбранной ИБ (<c>UpdateCheckWindow</c>, F9).
/// Привязана к <see cref="Infobase"/>: текущая версия читается/пишется в модель базы,
/// последняя версия и статус — результат сетевой проверки. Содержит команду загрузки.
/// </summary>
public class UpdateCheckRowViewModel : ViewModelBase
{
    /// <summary>Информационная база, для которой выполняется проверка.</summary>
    public Infobase Infobase { get; }

    /// <summary>Имя базы (неизменяемо в рамках окна).</summary>
    public string Name => Infobase.Name;

    /// <summary>Текущая версия конфигурации базы.</summary>
    public string CurrentVersion
    {
        get => Infobase.ConfigurationVersion ?? string.Empty;
        set
        {
            if (Infobase.ConfigurationVersion == value)
                return;
            Infobase.ConfigurationVersion = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

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

    /// <summary>Адрес каталога релизов, по которому выполнялась проверка.</summary>
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

    /// <summary>Выполняется ли сетевая проверка.</summary>
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

    /// <summary>True — обнаружен релиз новее текущей версии.</summary>
    public bool HasNewer => _status == ConfigUpdateStatus.NewerAvailable;

    /// <summary>True — проверка завершена (не Unknown).</summary>
    public bool IsFinished => _status != ConfigUpdateStatus.Unknown;

    /// <summary>Доступна ли загрузка: найден новый релиз, нет активных операций и есть ссылка.</summary>
    public bool CanDownload => !_isChecking && !_isDownloading && HasNewer && !string.IsNullOrWhiteSpace(_url);

    /// <summary>Команда загрузки дистрибутива (реализация — в окне).</summary>
    public RelayCommand DownloadCommand { get; }

    /// <param name="infobase">Информационная база.</param>
    /// <param name="download">Действие «начать загрузку».</param>
    public UpdateCheckRowViewModel(Infobase infobase, Action<UpdateCheckRowViewModel> download)
    {
        Infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));
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

    /// <summary>Обновляет привязку текущей версии после изменения модели базы.</summary>
    public void RefreshInfobase()
    {
        OnPropertyChanged(nameof(CurrentVersion));
        OnPropertyChanged(nameof(Name));
    }
}