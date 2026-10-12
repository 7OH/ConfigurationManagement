#if WINDOWS
using System.Linq;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно «Задания по расписанию» (Windows/WPF, issue #286): список заданий с действиями
/// «Выполнить сейчас», «Добавить», «Изменить», «Удалить».
/// </summary>
public partial class ScheduledTasksWindow : Window
{
    private readonly IScheduledTaskStore _store;
    private readonly IDialogService _dialogs;
    private readonly SchedulerService _scheduler;

    public ScheduledTasksWindow()
    {
        InitializeComponent();
        _store = AppServices.GetRequiredService<IScheduledTaskStore>();
        _dialogs = AppServices.GetRequiredService<IDialogService>();
        _scheduler = AppServices.GetRequiredService<SchedulerService>();

        Title = T("Schedule.Title");
        RunButton.Content = T("Schedule.RunNow");
        AddButton.Content = T("Common.Add");
        EditButton.Content = T("Common.Edit");
        CopyButton.Content = T("Common.Copy");
        DeleteButton.Content = T("Common.Delete");

        LoadTasks();
    }

    private static string T(string key) => LocalizationManager.T(key);

    private void LoadTasks()
    {
        TasksList.ItemsSource = _store.LoadAll()
            .Select(t => new ScheduledTaskItemViewModel(t))
            .ToList();
    }

    private ScheduledTaskItemViewModel? Selected => TasksList.SelectedItem as ScheduledTaskItemViewModel;

    private void TasksList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var has = Selected is not null;
        EditButton.IsEnabled = has;
        CopyButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var edit = new ScheduledTaskEditWindow();
        // Модальность относительно списка заданий (issue #291): без владельца окно
        // редактирования могло оказаться под активированным извне главным окном.
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } task)
        {
            _store.Save(task);
            LoadTasks();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        var edit = new ScheduledTaskEditWindow(item.Task);
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } updated)
        {
            _store.Save(updated);
            LoadTasks();
        }
    }

    /// <summary>
    /// Копирует выбранное задание (новый Id, имя «… — копия») и открывает окно
    /// редактирования СРАЗУ (issue #370, комментарий 7OH): копия не записывается
    /// на диск — пользователь видит окно создания новой записи, заполненное данными
    /// исходной строки, и может отредактировать или отказаться (Сохранить не нажат —
    /// копия не появится). Паттерн как в «Человеческие...» — «Пользовательские действия».
    /// </summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        var copy = item.Task.CreateCopy(string.Format(T("Settings.CopyOf"), item.Name));
        var edit = new ScheduledTaskEditWindow(copy);
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } savedCopy)
        {
            _store.Save(savedCopy);
            LoadTasks();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        if (_dialogs.Confirm(string.Format(T("Schedule.DeleteConfirm"), item.Name), T("Schedule.DeleteTitle")))
        {
            _store.Delete(item.Id);
            LoadTasks();
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        var result = await _scheduler.RunNowAsync(item.Task);
        if (result is not null)
        {
            if (result.Success)
                _dialogs.ShowInfo(string.Join("\n", result.CreatedFiles), T("Schedule.RunNow"));
            else
                _dialogs.ShowError(result.ErrorMessage ?? T("Schedule.RunFailed"), T("Schedule.RunNow"));
        }
        LoadTasks();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
#endif