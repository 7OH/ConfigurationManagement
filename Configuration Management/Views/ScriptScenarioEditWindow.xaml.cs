#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно создания/редактирования сценария запуска скрипта (Windows/WPF).
/// Возвращает результат через свойство <see cref="Result"/> при подтверждении.
/// Параметры задаются строками (каждая строка — отдельный параметр); двойной клик
/// по токену подстановки вставляет его в позицию курсора поля параметров.
/// </summary>
public partial class ScriptScenarioEditWindow : Window
{
    private readonly ScriptScenarioEditViewModel _vm;
    private readonly IDialogService _dialogs;
    private readonly List<Infobase> _infobases = new();

    /// <summary>Готовый сценарий при подтверждении, иначе <c>null</c>.</summary>
    public ScriptScenario? Result { get; private set; }

    /// <param name="scenario">Редактируемый сценарий или <c>null</c> для нового.</param>
    public ScriptScenarioEditWindow(ScriptScenario? scenario = null)
    {
        InitializeComponent();
        _dialogs = AppServices.GetRequiredService<IDialogService>();
        _vm = new ScriptScenarioEditViewModel(scenario);
        DataContext = _vm;

        Title = T(scenario is null ? "Script.AddTitle" : "Script.EditTitle");
        NameLabel.Text = T("Script.Name");
        FilePathLabel.Text = T("Script.FilePath");
        BrowseButton.Content = T("Script.Browse");
        ParametersLabel.Text = T("Script.Parameters");
        ParametersHint.Text = T("Script.ParametersHint");
        TokensLabel.Text = T("Script.Tokens");
        TokensHint.Text = T("Script.TokensHint");
        ExampleBaseLabel.Text = T("Script.ExampleBase");
        PreviewLabel.Text = T("Script.CommandLinePreview");
        OkButton.Content = T("Common.Save");

        // Токены подстановок: «%name% — имя базы» и т.д. (описание — из словаря локализации).
        TokensList.ItemsSource = ScriptScenarioEditViewModel.AvailableTokens
            .Select(t => t.Token + " — " + T(t.LocalizationKey))
            .ToList();

        // Базы для живого примера командной строки.
        try
        {
            _infobases.AddRange(AppServices.GetRequiredService<IInfobaseRepository>().Load());
        }
        catch
        {
            // Окно редактирования работает и без списка баз — пример просто не покажется.
        }
        ExampleBaseCombo.ItemsSource = _infobases;
        ExampleBaseCombo.DisplayMemberPath = nameof(Infobase.Name);

        Loaded += (_, _) =>
        {
            // Фокус в поле «Наименование» (issue #299): отложенный вызов после показа окна —
            // иначе при ShowDialog() фокус «съедается» до активации окна.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                NameBox.Focus();
                if (scenario is null)
                    NameBox.SelectAll();
            }));
            UpdatePreview();
        };
    }

    private static string T(string key) => LocalizationManager.T(key);

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var path = _dialogs.OpenFileDialog(T("Script.FilePath"));
        if (!string.IsNullOrWhiteSpace(path))
        {
            FilePathBox.Text = path;
            UpdatePreview();
        }
    }

    private void TokensList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TokensList.SelectedItem is not string token || string.IsNullOrEmpty(token))
            return;
        // Вставка токена в позицию курсора поля параметров (план #308: «двойной клик
        // по параметру вставляет его в позицию курсора поля параметров»).
        var text = ParametersBox.Text ?? "";
        var caret = ParametersBox.CaretIndex;
        if (caret > text.Length)
            caret = text.Length;
        ParametersBox.Text = text.Insert(caret, token);
        ParametersBox.CaretIndex = caret + token.Length;
        ParametersBox.Focus();
        UpdatePreview();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private Infobase? SelectedExampleBase =>
        ExampleBaseCombo.SelectedItem is Infobase ib ? ib
        : ExampleBaseCombo.Items.Count > 0 ? _infobases.FirstOrDefault()
        : null;

    private void UpdatePreview()
    {
        var draft = new ScriptScenario
        {
            Name = _vm.Name,
            FilePath = FilePathBox.Text ?? "",
            Parameters = _vm.NonEmptyParameters
        };
        var preview = ScriptScenarioEditViewModel.BuildExampleCommandLine(draft, SelectedExampleBase);
        PreviewBox.Text = string.IsNullOrWhiteSpace(preview) ? "—" : preview;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _vm.Name = NameBox.Text ?? "";
        _vm.FilePath = FilePathBox.Text ?? "";
        _vm.ParametersText = ParametersBox.Text ?? "";

        var errorKey = _vm.Validate();
        if (errorKey is not null)
        {
            _dialogs.ShowWarning(T(errorKey), Title);
            return;
        }

        var scenario = new ScriptScenario();
        _vm.ApplyTo(scenario);
        Result = scenario;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
#endif