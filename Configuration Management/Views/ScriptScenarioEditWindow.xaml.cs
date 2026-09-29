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

    /// <summary>
    /// Редактируемый сценарий (issue #308): при подтверждении поля применяются к нему же,
    /// чтобы Store.Save по тому же Id перезаписал файл, а не создавал копию. null — новый.
    /// </summary>
    private readonly ScriptScenario? _sourceScenario;

    /// <summary>Готовый сценарий при подтверждении, иначе <c>null</c>.</summary>
    public ScriptScenario? Result { get; private set; }

    /// <param name="scenario">Редактируемый сценарий или <c>null</c> для нового.</param>
    public ScriptScenarioEditWindow(ScriptScenario? scenario = null)
    {
        InitializeComponent();
        _dialogs = AppServices.GetRequiredService<IDialogService>();
        _sourceScenario = scenario;
        _vm = new ScriptScenarioEditViewModel(scenario);
        DataContext = _vm;

        Title = T(scenario is null ? "Script.AddTitle" : "Script.EditTitle");
        NameLabel.Text = T("Script.Name");
        FilePathLabel.Text = T("Script.FilePath");
        BrowseButton.Content = T("Script.Browse");
        WorkingDirectoryLabel.Text = T("Script.WorkingDirectory");
        WorkingDirectoryBrowseButton.Content = T("Script.Browse");
        ParametersLabel.Text = T("Script.Parameters");
        ParametersHint.Text = T("Script.ParametersHint");
        TokensLabel.Text = T("Script.Tokens");
        TokensHint.Text = T("Script.TokensHint");
        ExampleBaseLabel.Text = T("Script.ExampleBase");
        PreviewLabel.Text = T("Script.CommandLinePreview");
        OkButton.Content = T("Common.Save");

        // Токены подстановок: объекты ScriptTokenHint (issue #308) — в шаблоне списка
        // отображается «%token% — описание», а двойной клик вставляет ТОЛЬКО токен.
        TokensList.ItemsSource = ScriptScenarioEditViewModel.AvailableTokens
            .Select(t => new ScriptTokenHint(t.Token, t.LocalizationKey, T(t.LocalizationKey)))
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
        // Согласованность комбобокса и превью (issue #308): превью строится по первой базе,
        // поэтому комбобокс должен показывать её же.
        if (_infobases.Count > 0)
            ExampleBaseCombo.SelectedIndex = 0;

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

    /// <summary>
    /// Выбор «Папки запуска» сценария (issue #308, п.7): открывает диалог выбора
    /// каталога; пустое значение — наследовать рабочий каталог приложения.
    /// </summary>
    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var path = _dialogs.OpenFolderDialog(T("Script.WorkingDirectory"));
        if (!string.IsNullOrWhiteSpace(path))
        {
            WorkingDirectoryBox.Text = path;
            UpdatePreview();
        }
    }

    private void TokensList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Двойной клик вставляет ТОЛЬКО токен (issue #308), а не строку «%token% — описание»;
        // guard по ScriptTokenHint обязателен — двойной клик возможен и по пустому месту списка.
        if (TokensList.SelectedItem is not ScriptTokenHint hint || string.IsNullOrEmpty(hint.Token))
            return;
        var token = hint.Token;
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
            WorkingDirectory = WorkingDirectoryBox.Text ?? "",
            Parameters = _vm.NonEmptyParameters
        };
        var preview = ScriptScenarioEditViewModel.BuildExampleCommandLine(draft, SelectedExampleBase);
        PreviewBox.Text = string.IsNullOrWhiteSpace(preview) ? "—" : preview;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _vm.Name = NameBox.Text ?? "";
        _vm.FilePath = FilePathBox.Text ?? "";
        _vm.WorkingDirectory = WorkingDirectoryBox.Text ?? "";
        _vm.ParametersText = ParametersBox.Text ?? "";
        _vm.HideWindow = HideWindowCheckBox.IsChecked ?? true;

        var errorKey = _vm.Validate();
        if (errorKey is not null)
        {
            _dialogs.ShowWarning(T(errorKey), Title);
            return;
        }

        // Issue #308: при редактировании поля применяются к переданному сценарию (Id сохраняется),
        // иначе создаётся новый — Store.Save по тому же Id перезапишет файл без дублей.
        if (_sourceScenario is not null)
        {
            _vm.ApplyTo(_sourceScenario);
            Result = _sourceScenario;
        }
        else
        {
            var created = new ScriptScenario();
            _vm.ApplyTo(created);
            Result = created;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
#endif