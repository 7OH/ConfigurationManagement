using System;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка списка типовых конфигураций 1С в окне редактирования
/// (<c>ConfigTypesEditWindow</c>) и в окне «Актуальные релизы»: имя, сегмент URL,
/// признак предопределённой, флаг «отслеживать» и команды правки/удаления.
/// Обёртка над <see cref="OneCConfigType"/> — правки сразу отражаются в модели.
/// </summary>
public class ConfigTypeItemViewModel : ViewModelBase
{
    /// <summary>Оборачиваемая типовая конфигурация.</summary>
    public OneCConfigType Model { get; }

    /// <summary>Отображаемое имя конфигурации.</summary>
    public string Name => Model.Name;

    /// <summary>Сегмент web-адреса обновлений (UrlCode), либо имя, если сегмент пуст.</summary>
    public string UrlCode => string.IsNullOrWhiteSpace(Model.UrlCode) ? Model.Name : Model.UrlCode;

    /// <summary>Признак предопределённой конфигурации из встроенного набора.</summary>
    public bool IsBuiltIn => Model.IsBuiltIn;

    /// <summary>Краткая сводка редакций (имена через запятую).</summary>
    public string EditionsSummary =>
        Model.Editions.Count == 0
            ? string.Empty
            : string.Join(", ", Model.Editions.Select(e => e.ToString()));

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
    public ConfigTypeItemViewModel(OneCConfigType model, Action<ConfigTypeItemViewModel> edit, Action<ConfigTypeItemViewModel> delete)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        EditCommand = new RelayCommand(() => edit(this));
        DeleteCommand = new RelayCommand(() => delete(this), () => !model.IsBuiltIn);
    }

    /// <summary>Обновляет привязки после редактирования модели (имя, сегмент, редакции).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(UrlCode));
        OnPropertyChanged(nameof(EditionsSummary));
        OnPropertyChanged(nameof(IsTracked));
    }
}