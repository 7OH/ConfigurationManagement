using System;
using System.Linq;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка списка типовых конфигураций 1С в окне редактирования
/// (<c>ConfigTypesEditWindow</c>) и в окне «Актуальные релизы»: имя, сегмент URL,
/// признак предопределённой, флаг «отслеживать» и команды правки/удаления.
/// Обёртка над <see cref="OneCConfigType"/> — правки сразу отражаются в модели.
/// </summary>
public class ConfigTypeItemViewModel : ViewModelBase
{
    private readonly IItsAccountsStore? _itsAccounts;

    /// <summary>Оборачиваемая типовая конфигурация.</summary>
    public OneCConfigType Model { get; }

    /// <summary>Отображаемое имя конфигурации.</summary>
    public string Name => Model.Name;

    /// <summary>Стабильный код конфигурации (используется для связи ИБ ↔ конфигурация).</summary>
    public string Code => Model.Code;

    /// <summary>Сегмент web-адреса обновлений (UrlCode), либо имя, если сегмент пуст.</summary>
    public string UrlCode => string.IsNullOrWhiteSpace(Model.UrlCode) ? Model.Name : Model.UrlCode;

    /// <summary>
    /// Имя конфигурации в метаданных 1С (issue #321), например «БухгалтерияПредприятия».
    /// Пусто — сопоставление по имени не выполняется.
    /// </summary>
    public string ConfigName => Model.ConfigName;

    /// <summary>Ник конфигурации на releases.1c.ru (пустая строка — не задан).</summary>
    public string Nick => Model.Nick;

    /// <summary>Признак предопределённой конфигурации из встроенного набора.</summary>
    public bool IsBuiltIn => Model.IsBuiltIn;

    /// <summary>Признак пользовательской копии предопределённой конфигурации (правка встроенной
    /// строки, issue #321): строка заменяет встроенную с тем же кодом в общем списке.</summary>
    public bool IsOverride => Model.OverridesBuiltIn;

    /// <summary>Краткая сводка редакций (имена через запятую).</summary>
    public string EditionsSummary =>
        Model.Editions.Count == 0
            ? string.Empty
            : string.Join(", ", Model.Editions.Select(e => e.ToString()));

    /// <summary>Отображаемое имя учётной записи ИТС (issue #333): имя выбранной записи
    /// справочника либо «Основная», если запись не указана или не найдена.</summary>
    public string AccountDisplay
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Model.AccountId)
                ? null
                : _itsAccounts?.GetById(Model.AccountId)?.Name;
            return name ?? LocalizationManager.T("ItsAccounts.Primary");
        }
    }

    /// <summary>Флаг «отслеживать» в окне «Актуальные релизы».</summary>
    public bool IsTracked
    {
        get => Model.IsTracked;
        set
        {
            if (Model.IsTracked == value)
                return;
            Model.IsTracked = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Команда открытия редактора конфигурации.</summary>
    public RelayCommand EditCommand { get; }

    /// <summary>Команда удаления конфигурации (недоступна для предопределённых).</summary>
    public RelayCommand DeleteCommand { get; }

    /// <param name="model">Типовая конфигурация. Не может быть null.</param>
    /// <param name="edit">Действие «открыть редактор».</param>
    /// <param name="delete">Действие «удалить».</param>
    /// <param name="itsAccounts">Хранилище учётных записей ИТС для колонки «Учётная запись»
    /// (необязательно: окно списка типовых передаёт, остальные окна могут не передавать).</param>
    public ConfigTypeItemViewModel(
        OneCConfigType model,
        Action<ConfigTypeItemViewModel> edit,
        Action<ConfigTypeItemViewModel> delete,
        IItsAccountsStore? itsAccounts = null)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _itsAccounts = itsAccounts;
        EditCommand = new RelayCommand(() => edit(this));
        DeleteCommand = new RelayCommand(() => delete(this), () => !model.IsBuiltIn);
    }

    /// <summary>Обновляет привязки после редактирования модели (код, имя, имя конфигурации,
    /// сегмент, ник, редакции, учётная запись).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Code));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ConfigName));
        OnPropertyChanged(nameof(UrlCode));
        OnPropertyChanged(nameof(Nick));
        OnPropertyChanged(nameof(EditionsSummary));
        OnPropertyChanged(nameof(AccountDisplay));
        OnPropertyChanged(nameof(IsOverride));
        OnPropertyChanged(nameof(IsTracked));
    }
}