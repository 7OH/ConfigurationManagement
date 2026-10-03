#if WINDOWS
using System;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management;

/// <summary>
/// Модальный диалог создания/редактирования редакции типовой конфигурации 1С
/// (issue #321): имя редакции, сегменты «Ред»/«Подред» и переопределённая ссылка.
/// Работает на копии данных; результат возвращается через <see cref="Result"/>.
/// Заменяет инлайн-поля под таблицей редакций в <see cref="ConfigTypeEditWindow"/>.
/// </summary>
public partial class EditionEditWindow : Window
{
    /// <summary>Готовая редакция при подтверждении, иначе <c>null</c>.</summary>
    public OneCConfigEdition? Result { get; private set; }

    /// <param name="model">Редактируемая редакция или <c>null</c> для новой.</param>
    public EditionEditWindow(OneCConfigEdition? model = null)
    {
        InitializeComponent();

        EditorTitle.Text = model is null
            ? LocalizationManager.T("Updates.AddEdition")
            : LocalizationManager.T("Updates.EditEdition");
        Title = EditorTitle.Text;

        NameBox.Text = model?.Name ?? string.Empty;
        RedBox.Text = model?.Red ?? string.Empty;
        SubRedBox.Text = model?.SubRed ?? string.Empty;
        UrlOverrideBox.Text = model?.UrlOverride ?? string.Empty;

        // Фокус на первом поле после показа окна (как в остальных редакторах).
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => NameBox.Focus()));
        };
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        var red = RedBox.Text?.Trim() ?? string.Empty;

        // Редакция должна иметь имя или хотя бы сегмент «Ред» — иначе строка бессмысленна
        // (пустая строка «терялась» при показе списка — issue #321).
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(red))
        {
            MessageBox.Show(
                LocalizationManager.T("Updates.EditionNameOrRedRequired"),
                LocalizationManager.T("Updates.EditEdition"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        Result = new OneCConfigEdition
        {
            Name = name,
            Red = red,
            SubRed = SubRedBox.Text?.Trim() ?? string.Empty,
            UrlOverride = UrlOverrideBox.Text?.Trim() ?? string.Empty,
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