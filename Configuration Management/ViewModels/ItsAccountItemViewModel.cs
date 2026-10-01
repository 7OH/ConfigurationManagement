using System;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка списка учётных записей ИТС в окне справочника «Учетные данные ИТС»
/// (<c>ItsAccountsWindow</c>, issue #333): наименование, логин и флажок «Основная»
/// (только для чтения — смена основной только через кнопку «Задать основным»).
/// Обёртка над <see cref="ItsAccount"/>; правки сразу отражаются в модели.
/// </summary>
public class ItsAccountItemViewModel : ViewModelBase
{
    /// <summary>Оборачиваемая учётная запись.</summary>
    public ItsAccount Model { get; }

    /// <summary>Идентификатор записи.</summary>
    public string Id => Model.Id;

    /// <summary>Наименование записи.</summary>
    public string Name => Model.Name;

    /// <summary>Логин учётной записи.</summary>
    public string Login => Model.Login;

    /// <summary>Признак «Основная» — отображается только для чтения (issue #333).</summary>
    public bool IsPrimary => Model.IsPrimary;

    /// <summary>Команда открытия редактора записи.</summary>
    public RelayCommand EditCommand { get; }

    /// <summary>Команда удаления записи.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Команда «Задать основным» (недоступна для уже основной записи).</summary>
    public RelayCommand SetPrimaryCommand { get; }

    /// <param name="model">Учётная запись. Не может быть null.</param>
    /// <param name="edit">Действие «открыть редактор».</param>
    /// <param name="delete">Действие «удалить».</param>
    /// <param name="setPrimary">Действие «задать основной».</param>
    public ItsAccountItemViewModel(
        ItsAccount model,
        Action<ItsAccountItemViewModel> edit,
        Action<ItsAccountItemViewModel> delete,
        Action<ItsAccountItemViewModel> setPrimary)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        EditCommand = new RelayCommand(() => edit(this));
        DeleteCommand = new RelayCommand(() => delete(this));
        SetPrimaryCommand = new RelayCommand(() => setPrimary(this), () => !model.IsPrimary);
    }

    /// <summary>Обновляет привязки после правки/смены основной записи.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Login));
        OnPropertyChanged(nameof(IsPrimary));
        OnPropertyChanged(nameof(SetPrimaryCommand));
        SetPrimaryCommand.RaiseCanExecuteChanged();
    }
}