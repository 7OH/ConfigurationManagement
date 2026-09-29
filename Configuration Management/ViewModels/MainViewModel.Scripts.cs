#if WINDOWS
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Команды и методы функции «Сценарии» (issue #308) для Windows/WPF: настройка
/// сценариев запуска скриптов и выполнение выбранного сценария для ИБ.
/// Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _showScriptsSettingsCommand;
    private ICommand? _runScriptForSelectedCommand;

    /// <summary>Горячая клавиша «Выполнить скрипт» для выбранной базы (F5).</summary>
    public string HotkeyRunScript => "F5";

    /// <summary>Команда открытия окна «Настройка сценариев».</summary>
    public ICommand ShowScriptsSettingsCommand =>
        _showScriptsSettingsCommand ??= new RelayCommand(ExecuteShowScriptsSettings);

    /// <summary>Команда выполнения сценария для выбранной ИБ (F5).</summary>
    public ICommand RunScriptForSelectedCommand =>
        _runScriptForSelectedCommand ??= new RelayCommand(
            ExecuteRunScriptForSelected,
            () => SelectedInfobase is not null);

    private void ExecuteShowScriptsSettings()
    {
        var win = new Configuration_Management.ScriptScenariosWindow();
        // Настоящая модальность (issue #291): владелец — главное окно, иначе повторный
        // запуск программы с ярлыка ставил главное окно поверх диалога, блокируя ввод.
        win.Owner = System.Windows.Application.Current.MainWindow;
        win.ShowDialog();
    }

    /// <summary>Выполняет сценарий: одиночный — сразу, несколько — выбор в окне.</summary>
    private async void ExecuteRunScriptForSelected()
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
        {
            _dialogs.ShowWarning(LocalizationManager.T("Script.NoBaseSelected"), LocalizationManager.T("Script.RunTitle"));
            return;
        }

        var store = AppServices.GetRequiredService<IScriptScenarioStore>();
        var scenarios = store.LoadAll();
        if (scenarios.Count == 0)
        {
            _dialogs.ShowInfo(LocalizationManager.T("Script.NoneConfigured"), LocalizationManager.T("Script.RunTitle"));
            return;
        }

        if (scenarios.Count == 1)
        {
            await RunScriptAsync(infobase, scenarios[0]);
            return;
        }

        var win = new Configuration_Management.ScriptPickWindow(infobase, this);
        // Настоящая модальность (issue #291): владелец — главное окно, как в
        // ExecuteShowScriptsSettings.
        win.Owner = System.Windows.Application.Current.MainWindow;
        win.ShowDialog();
    }

    /// <summary>
    /// Выполняет сценарий запуска скрипта: подставляет свойства базы в параметры,
    /// запускает через системный shell без ожидания (fire-and-forget) и пишет
    /// команду в историю запусков базы.
    /// </summary>
    public async Task RunScriptAsync(Infobase infobase, ScriptScenario scenario)
    {
        if (infobase is null || scenario is null)
            return;

        var values = ScriptParameterResolver.BuildValueMap(infobase);
        var commandBody = ScriptParameterResolver.BuildCommandLine(scenario, values);
        if (string.IsNullOrWhiteSpace(commandBody))
        {
            _dialogs.ShowWarning(LocalizationManager.T("Script.EmptyCommand"), LocalizationManager.T("Script.RunTitle"));
            return;
        }

        // Полная командная строка с обёрткой выбранного интерпретатора (issue #308, п.9)
        // для лога и истории запусков; тело запускается с указанным shell (без повторной обёртки).
        var (shellFile, shellArgs) =
            ExternalCommandRunner.ResolveShellWrapper(scenario.Shell, commandBody, OperatingSystem.IsWindows());
        var commandLine = shellFile + " " + shellArgs;

        try
        {
            // Запуск без ожидания: пользовательский скрипт может выполняться долго,
            // а результат для приложения не критичен (лог + история запусков).
            // Видимость консольного окна — по свойству «Скрывать окно» сценария (issue #308),
            // рабочая папка — по свойству «Папка запуска» (issue #308, п.7),
            // интерпретатор — по свойству «Интерпретатор» (issue #308, п.9).
            ExternalCommandRunner.RunDetached(
                commandBody,
                createNoWindow: !scenario.HideWindow,
                workingDirectory: scenario.WorkingDirectory,
                shell: scenario.Shell);

            infobase.AddLaunchHistory("Script:" + scenario.Name, commandLine);
            Save();
            _logger.Info($"Запущен сценарий «{scenario.Name}» для базы «{infobase.Name}»: {commandLine}");
            _dialogs.ShowInfo(
                string.Format(LocalizationManager.T("Script.Started"), commandLine),
                LocalizationManager.T("Script.RunTitle"));
        }
        catch (Exception ex)
        {
            _logger.Error($"Не удалось запустить сценарий «{scenario.Name}»: {ex.Message}", ex);
            _dialogs.ShowError(
                string.Format(LocalizationManager.T("Script.Failed"), scenario.Name, ex.Message),
                LocalizationManager.T("Script.RunTitle"));
        }

        // Системное уведомление (функция №4): приложение может быть свёрнуто в трей,
        // а диалог результата увиден не будет.
        try
        {
            AppServices.GetRequiredService<INotificationService>().Show(
                LocalizationManager.T("App.Title"),
                string.Format(LocalizationManager.T("Notify.ScriptStarted"), infobase.Name, scenario.Name));
        }
        catch (Exception ex)
        {
            _logger.Warn($"Системное уведомление не показано: {ex.Message}");
        }

        await Task.CompletedTask;
    }
}
#endif