#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно создания/редактирования пользовательского действия контекстного меню (Windows/WPF,
/// функция 7, цикл 0.3.9.193–199). Возвращает результат через свойство <see cref="Result"/>
/// при подтверждении. Двойной клик по токену подстановки вставляет его в позицию курсора
/// поля команды; живое превью командной строки строится по выбранной базе примера.
/// </summary>
public partial class CustomActionEditWindow : Window
{
    private readonly CustomActionEditViewModel _vm;
    private readonly IDialogService _dialogs;
    private readonly List<Infobase> _infobases = new();

    /// <summary>
    /// Редактируемое действие: при подтверждении поля применяются к нему же, чтобы
    /// Store.Save по тому же Id перезаписал файл, а не создавал копию. null — новое.
    /// </summary>
    private readonly CustomAction? _sourceAction;

    /// <summary>Готовое действие при подтверждении, иначе <c>null</c>.</summary>
    public CustomAction? Result { get; private set; }

    /// <param name="action">Редактируемое действие или <c>null</c> для нового.</param>
    public CustomActionEditWindow(CustomAction? action = null)
    {
        InitializeComponent();
        _dialogs = AppServices.GetRequiredService<IDialogService>();
        _sourceAction = action;
        _vm = new CustomActionEditViewModel(action);
        DataContext = _vm;

        Title = T(action is null ? "CustomAction.AddTitle" : "CustomAction.EditTitle");
        NameLabel.Text = T("CustomAction.Name");
        CommandLabel.Text = T("CustomAction.Command");
        CommandHint.Text = T("CustomAction.CommandHint");
        ScopeLabel.Text = T("CustomAction.Scope");
        // Область применения: значения enum отображаются локализованными подписями
        // через KeyValuePair (как интерпретатор у сценариев, issue #308).
        ScopeCombo.ItemsSource = Enum.GetValues(typeof(CustomActionScope))
            .Cast<CustomActionScope>()
            .Select(s => new KeyValuePair<CustomActionScope, string>(s, ScopeDisplayName(s)))
            .ToList();
        ScopeCombo.SelectedValue = _vm.SelectedScope;
        // Интерпретатор (shell): подписи как в окне сценариев (переиспользуются Script.Shell.*).
        ShellLabel.Text = T("Script.Shell");
        ShellCombo.ItemsSource = CustomActionEditViewModel.ShellOptions
            .Select(s => new KeyValuePair<ScriptShell, string>(s, ShellDisplayName(s)))
            .ToList();
        ShellCombo.SelectedValue = _vm.SelectedShell;
        SupportsBatchCheckBox.Content = T("CustomAction.SupportsBatch");
        RunWithoutConfirmCheckBox.Content = T("CustomAction.RunWithoutConfirm");
        EscapeValuesCheckBox.Content = T("CustomAction.EscapeValues");
        TimeoutLabel.Text = T("CustomAction.TimeoutMs");
        TimeoutBox.Text = _vm.TimeoutSeconds.ToString();
        HotkeyLabel.Text = T("CustomAction.Hotkey");
        WorkingDirectoryLabel.Text = T("CustomAction.WorkingDirectory");
        TokensLabel.Text = T("CustomAction.Tokens");
        TokensHint.Text = T("CustomAction.TokensHint");
        ExampleBaseLabel.Text = T("CustomAction.ExampleBase");
        PreviewLabel.Text = T("CustomAction.Preview");
        OkButton.Content = T("Common.Save");
        CancelButton.Content = T("Common.Cancel");

        // Токены подстановок: объекты ScriptTokenHint — в шаблоне списка отображается
        // «{token} — описание», а двойной клик вставляет ТОЛЬКО токен.
        TokensList.ItemsSource = CustomActionEditViewModel.AvailableTokens
            .Select(t => new ScriptTokenHint(t.Token, t.LocalizationKey, T(t.LocalizationKey)))
            .ToList();

        // Базы для живого примера командной строки (механизм SelectedExampleBase из
        // ScriptScenarioEditWindow): окно работает и без списка баз — пример не покажется.
        try
        {
            _infobases.AddRange(AppServices.GetRequiredService<IInfobaseRepository>().Load());
        }
        catch
        {
            // Окно редактирования работает и без списка баз — пример просто не покажется.
        }
        ExampleBaseCombo.ItemsSource = _infobases;

        Loaded += (_, _) =>
        {
            // Фокус в поле «Наименование» (issue #299): отложенный вызов после показа окна —
            // иначе при ShowDialog() фокус «съедается» до активации окна.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                NameBox.Focus();
                if (action is null)
                    NameBox.SelectAll();
            }));

            // Переустановка выбора первой базы ПОСЛЕ показа окна (issue #308, п.14:37):
            // повторный SelectedIndex пересоздаёт контейнер с применённым ItemTemplate.
            if (_infobases.Count > 0)
                ExampleBaseCombo.SelectedIndex = 0;

            UpdatePreview();
        };
    }

    private static string T(string key) => LocalizationManager.T(key);

    /// <summary>Локализованная подпись области применения для выпадающего списка.</summary>
    private static string ScopeDisplayName(CustomActionScope scope) => scope switch
    {
        CustomActionScope.Base => T("CustomAction.Scope.Base"),
        CustomActionScope.Group => T("CustomAction.Scope.Group"),
        _ => T("CustomAction.Scope.Both")
    };

    /// <summary>Локализованная подпись интерпретатора для выпадающего списка
    /// (как у сценариев, issue #308, п.9): «Авто» локализуется, имена инструментов фиксированы.</summary>
    private static string ShellDisplayName(ScriptShell shell) => shell switch
    {
        ScriptShell.Cmd => "cmd",
        ScriptShell.PowerShell => "PowerShell",
        ScriptShell.Sh => "sh",
        _ => T("Script.Shell.Auto")
    };

    private void TokensList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Двойной клик вставляет ТОЛЬКО токен (как у сценариев, issue #308), а не строку
        // «%token% — описание»; guard по ScriptTokenHint обязателен — двойной клик возможен
        // и по пустому месту списка.
        if (TokensList.SelectedItem is not ScriptTokenHint hint || string.IsNullOrEmpty(hint.Token))
            return;
        var token = hint.Token;
        // Вставка токена в позицию курсора поля команды.
        var text = CommandBox.Text ?? "";
        var caret = CommandBox.CaretIndex;
        if (caret > text.Length)
            caret = text.Length;
        CommandBox.Text = text.Insert(caret, token);
        CommandBox.CaretIndex = caret + token.Length;
        CommandBox.Focus();
        UpdatePreview();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private Infobase? SelectedExampleBase =>
        ExampleBaseCombo.SelectedItem is Infobase ib ? ib
        : ExampleBaseCombo.Items.Count > 0 ? _infobases.FirstOrDefault()
        : null;

    /// <summary>
    /// Живое превью полной командной строки действия: поля формы переносятся в черновик
    /// <see cref="CustomAction"/>, превью строится через
    /// <see cref="CustomActionEditViewModel.BuildExampleCommandLine"/> для выбранной базы.
    /// </summary>
    private void UpdatePreview()
    {
        _vm.SelectedScope = ScopeCombo.SelectedValue is CustomActionScope scope ? scope : _vm.SelectedScope;
        _vm.SelectedShell = ShellCombo.SelectedValue is ScriptShell shell ? shell : _vm.SelectedShell;
        _vm.TimeoutSeconds = int.TryParse(TimeoutBox.Text, out var timeout) ? timeout : _vm.TimeoutSeconds;
        var draft = new CustomAction
        {
            Name = _vm.Name,
            Command = CommandBox.Text ?? "",
            Scope = _vm.SelectedScope,
            Shell = _vm.SelectedShell,
            SupportsBatch = _vm.SupportsBatch,
            RunWithoutConfirm = _vm.RunWithoutConfirm,
            EscapeValues = _vm.EscapeValues,
            TimeoutMs = _vm.TimeoutSeconds * 1000,
            Hotkey = HotkeyBox.Text ?? "",
            WorkingDirectory = WorkingDirectoryBox.Text ?? ""
        };
        var preview = CustomActionEditViewModel.BuildExampleCommandLine(draft, SelectedExampleBase);
        PreviewBox.Text = string.IsNullOrWhiteSpace(preview) ? "—" : preview;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _vm.Name = NameBox.Text ?? "";
        _vm.Command = CommandBox.Text ?? "";
        _vm.SelectedScope = ScopeCombo.SelectedValue is CustomActionScope scope ? scope : CustomActionScope.Both;
        _vm.SelectedShell = ShellCombo.SelectedValue is ScriptShell shell ? shell : ScriptShell.Auto;
        _vm.SupportsBatch = SupportsBatchCheckBox.IsChecked ?? false;
        _vm.RunWithoutConfirm = RunWithoutConfirmCheckBox.IsChecked ?? false;
        _vm.EscapeValues = EscapeValuesCheckBox.IsChecked ?? true;
        _vm.TimeoutSeconds = int.TryParse(TimeoutBox.Text, out var timeout) ? timeout : 0;
        _vm.Hotkey = HotkeyBox.Text ?? "";
        _vm.WorkingDirectory = WorkingDirectoryBox.Text ?? "";

        var errorKey = _vm.Validate();
        if (errorKey is not null)
        {
            _dialogs.ShowWarning(T(errorKey), Title);
            return;
        }

        // При редактировании поля применяются к переданному действию (Id сохраняется),
        // иначе создаётся новое — Store.Save по тому же Id перезапишет файл без дублей.
        if (_sourceAction is not null)
        {
            _vm.ApplyTo(_sourceAction);
            Result = _sourceAction;
        }
        else
        {
            var created = new CustomAction();
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