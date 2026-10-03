#if WINDOWS
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management;

/// <summary>
/// Отдельное модальное окно создания/редактирования пользовательской типовой конфигурации 1С
/// (issue #321): код, наименование, сегмент URL, ник на releases.1c.ru и список редакций.
/// Работает на копии данных: результат возвращается через <see cref="Result"/> при
/// подтверждении и применяется вызывающим окном — правка одной строки не влияет на другие
/// конфигурации. Предопределённые конфигурации сюда не передаются (окно списка скрывает
/// для них кнопки «Изменить»/«Удалить»).
/// </summary>
public partial class ConfigTypeEditWindow : Window
{
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    // ObservableCollection: «Добавить» должно сразу показывать новую редакцию в списке
    // (обычный List не уведомляет UI — issue #321).
    private readonly ObservableCollection<OneCConfigEdition> _editions = new();
    private readonly string _originalCode;
    private readonly List<ViewModels.ItsAccountSelectionItem> _accountItems = new();

    /// <summary>Готовая конфигурация при подтверждении, иначе <c>null</c>.</summary>
    public OneCConfigType? Result { get; private set; }

    /// <param name="model">Редактируемая конфигурация или <c>null</c> для новой.</param>
    public ConfigTypeEditWindow(OneCConfigType? model = null)
    {
        InitializeComponent();

        _originalCode = model?.Code ?? string.Empty;
        var isNew = model is null;

        EditorTitle.Text = isNew
            ? LocalizationManager.T("Updates.AddConfig")
            : LocalizationManager.T("Updates.EditConfig");
        Title = EditorTitle.Text;

        CodeBox.Text = model?.Code ?? string.Empty;
        NameBox.Text = model?.Name ?? string.Empty;
        ConfigNameBox.Text = model?.ConfigName ?? string.Empty;
        // Поле «Сегмент адреса» (UrlCode) из UI удалено (issue #321): адрес обновлений
        // строится только из ника (releases.1c.ru/project/<nick>), UrlCode не нужен.
        NickBox.Text = model?.Nick ?? string.Empty;

        // Учётная запись ИТС (issue #333): записи справочника; виртуальный пункт «Основная»
        // добавляется только если в справочнике нет реальной записи с таким именем.
        _accountItems.AddRange(ViewModels.ItsAccountSelectionBuilder.Build(
            AppServices.GetRequiredService<IItsAccountsStore>()));
        AccountCombo.ItemsSource = _accountItems;
        AccountCombo.SelectedIndex = Math.Max(0, Math.Min(
            ViewModels.ItsAccountSelectionBuilder.IndexOf(_accountItems, model?.AccountId),
            _accountItems.Count - 1));

        if (model is not null)
        {
            foreach (var edition in model.Editions)
                _editions.Add(edition);
        }
        EditionsGrid.ItemsSource = _editions;

        // Фокус в поле «Наименование» (issue #299): отложенный вызов после показа окна —
        // иначе при ShowDialog() фокус «съедается» до активации окна.
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    NameBox.Focus();
                    if (isNew)
                        NameBox.SelectAll();
                }));
        };

        // Высота окна (issue #321): выше, чтобы влезали 3–4 строки списка редакций, но не
        // за рамки экрана — WindowSizeMath (ClampHeight/FitTop), как в остальных окнах.
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, FitHeightToContent);
        };
    }

    /// <summary>
    /// Подгоняет высоту окна под содержимое (паттерн ScriptScenarioEditWindow, issue #308):
    /// измеряет корневой контейнер при бесконечной высоте, клампит в [MinHeight, MaxHeight]
    /// через <see cref="WindowSizeMath.ClampHeight"/> и поднимает окно, если низ уходит
    /// за нижний край рабочей области (<see cref="WindowSizeMath.FitTop"/>).
    /// </summary>
    private void FitHeightToContent()
    {
        if (RootPanel is null || !IsLoaded || !IsVisible)
            return;

        var availableWidth = RootPanel.ActualWidth > 0 ? RootPanel.ActualWidth : Math.Max(400, Width);
        RootPanel.Measure(new System.Windows.Size(availableWidth, double.PositiveInfinity));
        var desired = RootPanel.DesiredSize.Height;
        if (desired <= 0)
            return;

        // Хром (заголовок окна + рамки) — разница между полной высотой и клиентской областью.
        var chrome = Math.Max(0, ActualHeight - (RootPanel.ActualHeight > 0 ? RootPanel.ActualHeight : desired));
        var target = WindowSizeMath.ClampHeight(desired + chrome, MinHeight, MaxHeight);
        if (Math.Abs(target - Height) > 1)
            Height = target;

        // Окно стояло у нижнего края экрана и выросло — поднимаем его, чтобы нижняя
        // часть не уходила за экран (issue #308).
        var wa = SystemParameters.WorkArea;
        var newTop = WindowSizeMath.FitTop(Top, Height, wa.Top, wa.Bottom);
        if (Math.Abs(newTop - Top) > 1)
            Top = newTop;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _dialogs.ShowWarning(LocalizationManager.T("Updates.NameRequired"),
                LocalizationManager.T("Updates.EditConfig"));
            NameBox.Focus();
            return;
        }

        var code = CodeBox.Text?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            // Для связи ИБ ↔ конфигурация нужен стабильный код: сохраняем прежний,
            // а для новой конфигурации генерируем из наименования.
            code = _originalCode.Length > 0 ? _originalCode : GenerateCode(name);
        }

        var selectedAccount = AccountCombo.SelectedItem as ViewModels.ItsAccountSelectionItem;

        Result = new OneCConfigType
        {
            Code = code,
            Name = name,
            ConfigName = ConfigNameBox.Text?.Trim() ?? string.Empty,
            Nick = NickBox.Text?.Trim() ?? string.Empty,
            // Учётная запись ИТС: пусто — «Основная» (либо выбранная в настройках).
            AccountId = selectedAccount?.Id ?? string.Empty,
            IsBuiltIn = false,
            Editions = new List<OneCConfigEdition>(_editions),
        };
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// «Добавить…» (issue #321): отдельный модальный диалог с полями новой редакции вместо
    /// инлайн-полей под таблицей. Диалог работает на копии; строка добавляется по ОК.
    /// </summary>
    private void OnAddEditionClick(object sender, RoutedEventArgs e)
    {
        var dialog = new EditionEditWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } edition)
            return;

        _editions.Add(edition);
        EditionsGrid.SelectedItem = edition;
        EditionsGrid.ScrollIntoView(edition);
    }

    /// <summary>«Изменить…»: модальный диалог правки выбранной редакции (копия → замена).</summary>
    private void OnEditEditionClick(object sender, RoutedEventArgs e)
    {
        if (EditionsGrid.SelectedItem is not OneCConfigEdition edition)
        {
            _dialogs.ShowInfo(LocalizationManager.T("Updates.SelectEditionFirst"),
                LocalizationManager.T("Updates.EditEdition"));
            return;
        }

        var dialog = new EditionEditWindow(new OneCConfigEdition
        {
            Name = edition.Name,
            Red = edition.Red,
            SubRed = edition.SubRed,
            UrlOverride = edition.UrlOverride,
        })
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.Result is not { } updated)
            return;

        var index = _editions.IndexOf(edition);
        _editions[index] = updated;
        EditionsGrid.SelectedItem = updated;
    }

    /// <summary>«Удалить»: удаляет выбранную редакцию из списка.</summary>
    private void OnRemoveEditionClick(object sender, RoutedEventArgs e)
    {
        if (EditionsGrid.SelectedItem is not OneCConfigEdition edition)
            return;
        var index = _editions.IndexOf(edition);
        _editions.Remove(edition);
        if (_editions.Count > 0)
            EditionsGrid.SelectedIndex = Math.Min(index, _editions.Count - 1);
    }

    /// <summary>Генерирует стабильный код из наименования (латиница/цифры/подчёркивания).</summary>
    private static string GenerateCode(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            if (ch < 128 && (char.IsLetterOrDigit(ch)))
                sb.Append(char.ToUpperInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
            if (sb.Length >= 12)
                break;
        }
        var result = sb.ToString().Trim('_');
        return result.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : result;
    }
}
#endif