#if WINDOWS
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно «Пользовательские действия» (Windows/WPF): список действий контекстного меню
/// (функция 7, цикл 0.3.9.193–199) с действиями «Добавить», «Изменить», «Удалить».
/// После закрытия окна вызывающий код (MainViewModel) перечитывает кэш действий
/// (<see cref="ViewModels.MainViewModel.ReloadCustomActions"/>), поэтому подменю
/// актуализируется при следующем открытии.
/// </summary>
public partial class CustomActionsWindow : Window
{
    private readonly ICustomActionsStore _store;
    private readonly IDialogService _dialogs;

    public CustomActionsWindow()
    {
        InitializeComponent();
        _store = AppServices.GetRequiredService<ICustomActionsStore>();
        _dialogs = AppServices.GetRequiredService<IDialogService>();

        AddButton.Content = T("Common.Add");
        EditButton.Content = T("Common.Edit");
        DeleteButton.Content = T("Common.Delete");

        LoadActions();
    }

    private static string T(string key) => LocalizationManager.T(key);

    /// <summary>
    /// Перечитывает список действий из хранилища. Порядок задаёт хранилище (сортировка
    /// по имени); выделение восстанавливается по Id — после редактирования строки выбор
    /// не сбрасывается, как требует логика окна настроек.
    /// </summary>
    private void LoadActions()
    {
        var selectedId = Selected?.Id;
        ActionsList.ItemsSource = _store.LoadAll()
            .Select(a => new CustomActionItemViewModel(a, edit: OnEditItem, delete: OnDeleteItem))
            .ToList();

        if (selectedId is not null)
        {
            foreach (var obj in ActionsList.Items)
            {
                if (obj is CustomActionItemViewModel vm && vm.Id == selectedId)
                {
                    ActionsList.SelectedItem = vm;
                    break;
                }
            }
        }
    }

    private CustomActionItemViewModel? Selected => ActionsList.SelectedItem as CustomActionItemViewModel;

    private void ActionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var has = Selected is not null;
        EditButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private void ActionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not null)
            OnEditItem(Selected);
    }

    private void OnAdd_Click(object sender, RoutedEventArgs e)
    {
        // Существующие действия передаются в редактор для валидации конфликтов хоткеев (0.3.9.198).
        var edit = new CustomActionEditWindow(null, _store.LoadAll());
        // Модальность относительно списка действий, как в окне сценариев (issue #291):
        // без владельца редактор мог оказаться под активированным извне главным окном.
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } created)
        {
            _store.Save(created);
            LoadActions();
        }
    }

    private void OnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
            OnEditItem(item);
    }

    /// <summary>Открывает редактор действия (кнопка «Изменить», двойной клик, колбэк строки).</summary>
    private void OnEditItem(CustomActionItemViewModel item)
    {
        if (item is null)
            return;
        var edit = new CustomActionEditWindow(item.Action, _store.LoadAll());
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } updated)
        {
            _store.Save(updated);
            LoadActions();
        }
    }

    private void OnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
            OnDeleteItem(item);
    }

    /// <summary>Удаляет действие с подтверждением (кнопка «Удалить», колбэк строки).</summary>
    private void OnDeleteItem(CustomActionItemViewModel item)
    {
        if (item is null)
            return;
        if (_dialogs.Confirm(string.Format(T("CustomAction.DeleteConfirm"), item.Name), T("CustomAction.DeleteTitle")))
        {
            _store.Delete(item.Id);
            LoadActions();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
#endif