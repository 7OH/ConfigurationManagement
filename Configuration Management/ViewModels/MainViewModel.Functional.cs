#if WINDOWS
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Режим функциональности приложения (Этап 10 дорожной карты StartManager):
/// «Пользователь» / «Специалист» / «Разработчик». Добавлен отдельным partial-файлом,
/// чтобы не раздувать основной конструктор. Управляет ограничением «системного меню»
/// в режиме «Пользователь».
/// </summary>
public partial class MainViewModel
{
    private string _functionalMode = Models.FunctionalModes.Default;
    private Models.LaunchConfigDefaults _launchConfigDefaults = new();

    /// <summary>
    /// Текущий режим функциональности (каноническая строка <see cref="FunctionalModes"/>).
    /// Устанавливается из настроек при запуске и изменяется через окно «Настройки».
    /// </summary>
    public string FunctionalMode
    {
        get => _functionalMode;
        set
        {
            if (SetProperty(ref _functionalMode, Models.FunctionalModes.ToString(
                    Models.FunctionalModes.Parse(value))))
            {
                OnPropertyChanged(nameof(IsSystemMenuRestricted));
                OnPropertyChanged(nameof(FunctionalModeLabel));
            }
        }
    }

    /// <summary>
    /// Параметры по умолчанию из <c>1CLaunch.cfg</c> (Этап 10), сохранённые при последнем
    /// чтении. Используются как исходные значения для окна «Параметры» и лаунчером.
    /// </summary>
    public Models.LaunchConfigDefaults LaunchConfigDefaults
    {
        get => _launchConfigDefaults;
        set => SetProperty(ref _launchConfigDefaults, value ?? new Models.LaunchConfigDefaults());
    }

    /// <summary>
    /// Признак ограничения «системного меню»: true в режиме «Пользователь».
    /// UI скрывает системные операции (редактирование, удаление, выгрузки,
    /// администрирование) при активном ограничении.
    /// </summary>
    public bool IsSystemMenuRestricted => Models.FunctionalModes.IsUser(_functionalMode);

    /// <summary>Локализованная подпись текущего режима функциональности.</summary>
    public string FunctionalModeLabel =>
        Models.FunctionalModes.Parse(_functionalMode) switch
        {
            Models.FunctionalMode.User => Localization.LocalizationManager.T("FunctionalMode.User"),
            Models.FunctionalMode.Developer => Localization.LocalizationManager.T("FunctionalMode.Developer"),
            _ => Localization.LocalizationManager.T("FunctionalMode.Specialist")
        };

    /// <summary>
    /// Загружает режим функциональности и параметры <c>1CLaunch.cfg</c> из настроек.
    /// Вызывается из конструктора после чтения <c>settings.json</c>.
    /// </summary>
    private void LoadFunctionalSettings(Models.AppSettings settings)
    {
        _functionalMode = Models.FunctionalModes.ToString(
            Models.FunctionalModes.Parse(settings.FunctionalMode));
        _launchConfigDefaults = settings.LaunchConfigDefaults ?? new Models.LaunchConfigDefaults();
    }
}
#endif