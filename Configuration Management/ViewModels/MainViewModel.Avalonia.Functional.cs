#if LINUX
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Режим функциональности приложения (Этап 10 дорожной карты StartManager) для Avalonia/Linux.
/// «Пользователь» / «Специалист» / «Разработчик». В режиме «Пользователь» ограничивается
/// доступ к системному меню. Значение хранится в <see cref="AppSettings.FunctionalMode"/>.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Текущий режим функциональности (каноническая строка <see cref="FunctionalModes"/>).
    /// Сохраняется в настройки сразу при изменении (как и прочие настройки Avalonia).
    /// </summary>
    public string FunctionalMode
    {
        get => Models.FunctionalModes.ToString(
            Models.FunctionalModes.Parse(_settings?.FunctionalMode));
        set
        {
            var normalized = Models.FunctionalModes.ToString(Models.FunctionalModes.Parse(value));
            if (_settings != null)
                _settings.FunctionalMode = normalized;
            SaveSettingsSilently();
            OnPropertyChanged(nameof(FunctionalMode));
            OnPropertyChanged(nameof(IsSystemMenuRestricted));
            OnPropertyChanged(nameof(FunctionalModeLabel));
        }
    }

    /// <summary>
    /// Параметры по умолчанию из <c>1CLaunch.cfg</c> (Этап 10), сохранённые при последнем
    /// чтении. Используются как исходные значения для окна «Параметры» и лаунчером.
    /// </summary>
    public Models.LaunchConfigDefaults LaunchConfigDefaults =>
        _settings?.LaunchConfigDefaults ?? new Models.LaunchConfigDefaults();

    /// <summary>Признак ограничения «системного меню»: true в режиме «Пользователь».</summary>
    public bool IsSystemMenuRestricted => Models.FunctionalModes.IsUser(FunctionalMode);

    /// <summary>Локализованная подпись текущего режима функциональности.</summary>
    public string FunctionalModeLabel =>
        Models.FunctionalModes.Parse(FunctionalMode) switch
        {
            Models.FunctionalMode.User => Localization.LocalizationManager.T("FunctionalMode.User"),
            Models.FunctionalMode.Developer => Localization.LocalizationManager.T("FunctionalMode.Developer"),
            _ => Localization.LocalizationManager.T("FunctionalMode.Specialist")
        };

    /// <summary>
    /// Загружает режим функциональности и параметры <c>1CLaunch.cfg</c> в <see cref="_settings"/>.
    /// Вызывается из <see cref="Initialize"/> после чтения настроек.
    /// </summary>
    private void LoadFunctionalSettings()
    {
        if (_settings == null)
            return;
        _settings.FunctionalMode = Models.FunctionalModes.ToString(
            Models.FunctionalModes.Parse(_settings.FunctionalMode));
        _settings.LaunchConfigDefaults ??= new Models.LaunchConfigDefaults();
        OnPropertyChanged(nameof(FunctionalMode));
        OnPropertyChanged(nameof(IsSystemMenuRestricted));
        OnPropertyChanged(nameof(FunctionalModeLabel));
    }
}
#endif