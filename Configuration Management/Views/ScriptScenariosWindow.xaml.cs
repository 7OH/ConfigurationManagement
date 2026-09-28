#if WINDOWS
using System.Linq;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно «Настройка сценариев» (Windows/WPF): список сценариев запуска скриптов
/// с действиями «Добавить», «Изменить», «Удалить» (issue #308).
/// </summary>
public partial class ScriptScenariosWindow : Window
{
    private readonly IScriptScenarioStore _store;
    private readonly IDialogService _dialogs;

    public ScriptScenariosWindow()
    {
        InitializeComponent();
        _store = AppServices.GetRequiredService<IScriptScenarioStore>();
        _dialogs = AppServices.GetRequiredService<IDialogService>();

        Title = T("Script.WindowTitle");
        AddButton.Content = T("Common.Add");
        EditButton.Content = T("Common.Edit");
        DeleteButton.Content = T("Common.Delete");

        LoadScenarios();
    }

    private static string T(string key) => LocalizationManager.T(key);

    private void LoadScenarios()
    {
        ScenariosList.ItemsSource = _store.LoadAll()
            .Select(s => new ScriptScenarioItemViewModel(s))
            .ToList();
    }

    private ScriptScenarioItemViewModel? Selected => ScenariosList.SelectedItem as ScriptScenarioItemViewModel;

    private void ScenariosList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var has = Selected is not null;
        EditButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private void ScenariosList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Selected is not null)
            Edit_Click(sender, e);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var edit = new ScriptScenarioEditWindow();
        // Модальность относительно списка сценариев (issue #291): без владельца окно
        // редактирования могло оказаться под активированным извне главным окном.
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } scenario)
        {
            _store.Save(scenario);
            LoadScenarios();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        var edit = new ScriptScenarioEditWindow(item.Scenario);
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } updated)
        {
            _store.Save(updated);
            LoadScenarios();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        if (_dialogs.Confirm(string.Format(T("Script.DeleteConfirm"), item.Name), T("Script.DeleteTitle")))
        {
            _store.Delete(item.Id);
            LoadScenarios();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
#endif