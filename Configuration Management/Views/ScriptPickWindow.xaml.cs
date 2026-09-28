#if WINDOWS
using System.Linq;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно «Выбор скрипта» (Windows/WPF, issue #308): список сценариев запуска скриптов
/// с живой подсказкой полной командной строки (подстановки выполнены для выбранной
/// базы). Запуск — двойным кликом или кнопкой «Выполнить».
/// </summary>
public partial class ScriptPickWindow : Window
{
    private readonly IScriptScenarioStore _store;
    private readonly Infobase _infobase;
    private readonly MainViewModel _vm;

    public ScriptPickWindow(Infobase infobase, MainViewModel vm)
    {
        InitializeComponent();
        _store = AppServices.GetRequiredService<IScriptScenarioStore>();
        _infobase = infobase;
        _vm = vm;

        Title = T("Script.PickTitle");
        HintText.Text = string.Format(T("Script.PickHint"), infobase.Name);
        PreviewLabel.Text = T("Script.CommandLinePreview");
        RunButton.Content = T("Script.RunShort");

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
        RunButton.IsEnabled = Selected is not null;
        UpdatePreview();
    }

    private void ScenariosList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Selected is not null)
            Run_Click(sender, e);
    }

    private void UpdatePreview()
    {
        PreviewBox.Text = Selected is { } item
            ? ScriptScenarioEditViewModel.BuildExampleCommandLine(item.Scenario, _infobase)
            : "—";
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        await _vm.RunScriptAsync(_infobase, item.Scenario);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
#endif