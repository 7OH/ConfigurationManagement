#if LINUX
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно создания/редактирования сценария запуска скрипта (Avalonia/Linux, issue #308).
/// Параметры задаются строками (каждая строка — отдельный параметр); двойной клик
/// по токену подстановки вставляет его в позицию курсора поля параметров.
/// </summary>
public sealed class ScriptScenarioEditWindow : ModalWindowBase
{
    private readonly ScriptScenarioEditViewModel _vm;
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly System.Collections.Generic.List<Infobase> _infobases = new();

    private readonly TextBox _nameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _filePathBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _parametersBox = new TextBox
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 90,
        MaxHeight = 150
    }.Styled(ControlThemes.ModernTextBox);
    private readonly ListBox _tokensList = new();
    private readonly ComboBox _exampleBaseCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly TextBox _previewBox = new TextBox { IsReadOnly = true }.Styled(ControlThemes.ModernTextBox);

    /// <summary>Готовый сценарий при подтверждении, иначе <c>null</c>.</summary>
    public ScriptScenario? Result { get; private set; }

    /// <param name="scenario">Редактируемый сценарий или <c>null</c> для нового.</param>
    public ScriptScenarioEditWindow(ScriptScenario? scenario = null)
    {
        _vm = new ScriptScenarioEditViewModel(scenario);
        Title = T(scenario is null ? "Script.AddTitle" : "Script.EditTitle");
        Width = 640;
        Height = 680;
        MinWidth = 560;
        MinHeight = 580;
        FontSize = 13;
        Content = BuildRoot();

        // Фокус в поле «Наименование» (issue #299): отложенно после показа окна —
        // синхронная установка в Opened слетает до активации модального диалога.
        Opened += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _nameBox.Focus();
                if (scenario is null)
                    _nameBox.SelectAll();
            }, Avalonia.Threading.DispatcherPriority.Background);
        };
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    private static Control Label(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        // Цвет подписи из темы (issue #291), как в остальных окнах Avalonia.
        ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");
        return label;
    }

    private static Control HintLabel(string text)
    {
        var hint = new TextBlock { Text = text, FontSize = 11 };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        return hint;
    }

    private Control BuildRoot()
    {
        var panel = new StackPanel { Margin = new Avalonia.Thickness(14), Spacing = 8 };

        panel.Children.Add(Label(T("Script.Name")));
        _nameBox.Text = _vm.Name;
        panel.Children.Add(_nameBox);

        panel.Children.Add(Label(T("Script.FilePath")));
        var pathRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(8) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        _filePathBox.Text = _vm.FilePath;
        _filePathBox.TextChanged += (_, _) => UpdatePreview();
        Grid.SetColumn(_filePathBox, 0);
        pathRow.Children.Add(_filePathBox);
        var browse = new Button { Content = T("Script.Browse"), Width = 96 }.Styled(ControlThemes.SecondaryButton);
        browse.Click += (_, _) => BrowseFile();
        Grid.SetColumn(browse, 2);
        pathRow.Children.Add(browse);
        panel.Children.Add(pathRow);

        panel.Children.Add(Label(T("Script.Parameters")));
        _parametersBox.Text = _vm.ParametersText;
        _parametersBox.TextChanged += (_, _) => UpdatePreview();
        panel.Children.Add(_parametersBox);
        panel.Children.Add(HintLabel(T("Script.ParametersHint")));

        panel.Children.Add(Label(T("Script.Tokens")));
        // Токены подстановок: «%name% — имя базы» и т.д. (описание — из словаря локализации).
        _tokensList.ItemsSource = ScriptScenarioEditViewModel.AvailableTokens
            .Select(t => t.Token + " — " + T(t.LocalizationKey))
            .ToList();
        _tokensList.Height = 130;
        _tokensList.DoubleTapped += (_, _) => InsertTokenAtCaret();
        panel.Children.Add(_tokensList);
        panel.Children.Add(HintLabel(T("Script.TokensHint")));

        panel.Children.Add(Label(T("Script.ExampleBase")));
        try
        {
            _infobases.AddRange(AppServices.GetRequiredService<IInfobaseRepository>().Load());
        }
        catch
        {
            // Окно редактирования работает и без списка баз — пример просто не покажется.
        }
        _exampleBaseCombo.ItemsSource = _infobases;
        _exampleBaseCombo.SelectedItem = _infobases.FirstOrDefault();
        _exampleBaseCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _exampleBaseCombo.MinWidth = 260;
        _exampleBaseCombo.SelectionChanged += (_, _) => UpdatePreview();
        panel.Children.Add(_exampleBaseCombo);

        panel.Children.Add(HintLabel(T("Script.CommandLinePreview")));
        _previewBox.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_previewBox);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        };
        var ok = new Button { Content = T("Common.Save"), Width = 90 }.Styled(ControlThemes.DialogConfirmButton);
        ok.Click += (_, _) => OkClicked();
        var cancel = new Button { Content = T("Common.Cancel"), Width = 90 }.Styled(ControlThemes.DialogCancelButton);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);
        panel.Children.Add(bottom);

        UpdatePreview();
        return new ScrollViewer { Content = panel };
    }

    private void BrowseFile()
    {
        var path = _dialogs.OpenFileDialog(T("Script.FilePath"));
        if (!string.IsNullOrWhiteSpace(path))
        {
            _filePathBox.Text = path;
            UpdatePreview();
        }
    }

    private void InsertTokenAtCaret()
    {
        if (_tokensList.SelectedItem is not string token || string.IsNullOrEmpty(token))
            return;
        // Вставка токена в позицию курсора поля параметров (план #308: «двойной клик
        // по параметру вставляет его в позицию курсора поля параметров»).
        var text = _parametersBox.Text ?? "";
        var caret = _parametersBox.CaretIndex;
        if (caret > text.Length)
            caret = text.Length;
        _parametersBox.Text = text.Insert(caret, token);
        _parametersBox.CaretIndex = caret + token.Length;
        _parametersBox.Focus();
        UpdatePreview();
    }

    private Infobase? SelectedExampleBase =>
        _exampleBaseCombo.SelectedItem is Infobase ib ? ib : _infobases.FirstOrDefault();

    private void UpdatePreview()
    {
        _vm.Name = _nameBox.Text ?? "";
        _vm.FilePath = _filePathBox.Text ?? "";
        _vm.ParametersText = _parametersBox.Text ?? "";
        var draft = new ScriptScenario
        {
            Name = _vm.Name,
            FilePath = _vm.FilePath,
            Parameters = _vm.NonEmptyParameters
        };
        var preview = ScriptScenarioEditViewModel.BuildExampleCommandLine(draft, SelectedExampleBase);
        _previewBox.Text = string.IsNullOrWhiteSpace(preview) ? "—" : preview;
    }

    private void OkClicked()
    {
        _vm.Name = _nameBox.Text ?? "";
        _vm.FilePath = _filePathBox.Text ?? "";
        _vm.ParametersText = _parametersBox.Text ?? "";

        var errorKey = _vm.Validate();
        if (errorKey is not null)
        {
            _dialogs.ShowWarning(T(errorKey), Title ?? "");
            return;
        }

        var scenario = new ScriptScenario();
        _vm.ApplyTo(scenario);
        Result = scenario;
        DialogResult = true;
        Close();
    }
}
#endif