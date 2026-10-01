#if WINDOWS
using System;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management;

/// <summary>
/// Модальный редактор учётной записи ИТС (issue #333): наименование, логин и пароль.
/// Скининг — по теме приложения (DynamicResource), фокус по умолчанию на первом поле
/// («Наименование»). Работает на копии данных: результат возвращается через
/// <see cref="Result"/> при подтверждении; флаг «Основная» из редактора не меняется
/// (смена основной — только через кнопку «Задать основным» окна списка).
/// </summary>
public partial class ItsAccountEditWindow : Window
{
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly string _originalId;

    /// <summary>Готовая учётная запись при подтверждении, иначе <c>null</c>.</summary>
    public ItsAccount? Result { get; private set; }

    /// <param name="model">Редактируемая запись или <c>null</c> для новой.</param>
    public ItsAccountEditWindow(ItsAccount? model = null)
    {
        InitializeComponent();

        _originalId = model?.Id ?? string.Empty;
        var isNew = model is null;
        EditorTitle.Text = isNew
            ? LocalizationManager.T("ItsAccounts.AddTitle")
            : LocalizationManager.T("ItsAccounts.EditTitle");
        Title = EditorTitle.Text;

        NameBox.Text = model?.Name ?? string.Empty;
        LoginBox.Text = model?.Login ?? string.Empty;
        if (model is not null)
            PasswordBox.Password = model.Password ?? string.Empty;

        // Фокус в первом поле (наименование) — отложенный вызов после показа окна,
        // иначе при ShowDialog() фокус «съедается» до активации окна (паттерн #299).
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
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _dialogs.ShowWarning(LocalizationManager.T("ItsAccounts.NameRequired"),
                LocalizationManager.T("ItsAccounts.EditTitle"));
            NameBox.Focus();
            return;
        }

        Result = new ItsAccount
        {
            Id = _originalId,
            Name = name,
            Login = LoginBox.Text?.Trim() ?? string.Empty,
            Password = PasswordBox.Password ?? string.Empty,
            // Флаг «Основная» не меняется редактором: хранилище сохраняет его при правке.
        };
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
#endif