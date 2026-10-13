#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог создания/правки группы меню «Утилиты» (issue #374): имя группы и иконка
    /// из каталога (с превью). Avalonia/Linux-версия WPF-окна <see cref="UtilitiesGroupEditWindow"/>.
    /// Результат — <see cref="UtilitiesGroupEditParams"/> через <see cref="Result"/> (null — отмена).
    /// </summary>
    public sealed class UtilitiesGroupEditWindow : ModalWindowBase
    {
        private readonly TextBox _nameBox = new();
        private readonly ComboBox _iconCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
        private readonly Button _okButton = new();

        /// <param name="icons">Доступные иконки каталога (из ViewModel редактора).</param>
        /// <param name="initialTitle">Текущее имя группы (для правки) или пусто.</param>
        /// <param name="initialIconKey">Текущий ключ иконки (для правки) или пусто.</param>
        public UtilitiesGroupEditWindow(
            IReadOnlyList<UtilitiesIconOption> icons,
            string? initialTitle = null,
            string? initialIconKey = null)
        {
            Title = LocalizationManager.T("Utilities.Editor.GroupDialogTitle");
            Width = 440;
            Height = 240;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _nameBox.Text = initialTitle ?? string.Empty;
            _nameBox.TextChanged += (_, _) => UpdateOkEnabled();

            _iconCombo.ItemsSource = icons;
            _iconCombo.ItemTemplate = new FuncDataTemplate<UtilitiesIconOption>(
                (o, _) => BuildIconRow(o));
            if (!string.IsNullOrWhiteSpace(initialIconKey))
            {
                var match = icons.FirstOrDefault(o => string.Equals(o.Key, initialIconKey, StringComparison.Ordinal));
                if (match is not null)
                    _iconCombo.SelectedItem = match;
            }
            if (_iconCombo.SelectedItem is null && icons.Count > 0)
                _iconCombo.SelectedItem = icons[0];

            Content = BuildLayout();
            UpdateOkEnabled();
            Opened += (_, _) => _nameBox.Focus();
        }

        /// <summary>Результат диалога: имя и ключ иконки (null — отмена).</summary>
        public UtilitiesGroupEditParams? Result { get; private set; }

        /// <summary>Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>.</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private Control BuildLayout()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Имя группы.
            var nameLabel = new TextBlock
            {
                Text = LocalizationManager.T("Utilities.Editor.GroupNameLabel"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(nameLabel, 0);
            grid.Children.Add(nameLabel);

            _nameBox.Padding = new Thickness(8, 6);
            Grid.SetRow(_nameBox, 1);
            grid.Children.Add(_nameBox);

            // Иконка.
            var iconLabel = new TextBlock
            {
                Text = LocalizationManager.T("Utilities.Editor.IconHeader"),
                Margin = new Thickness(0, 12, 0, 6)
            };
            Grid.SetRow(iconLabel, 2);
            grid.Children.Add(iconLabel);

            _iconCombo.Width = 220;
            Grid.SetRow(_iconCombo, 3);
            Grid.SetColumn(_iconCombo, 0);
            grid.Children.Add(_iconCombo);

            // Кнопки.
            var buttons = BuildButtons(
                okText: LocalizationManager.T("Common.Ok"), onOk: OnOkClick);
            Grid.SetRow(buttons, 4);
            buttons.Margin = new Thickness(0, 16, 0, 0);
            grid.Children.Add(buttons);

            return grid;
        }

        /// <summary>Строка иконки: превью слева + подпись.</summary>
        private Control BuildIconRow(UtilitiesIconOption option)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var icon = IconHelper.MakeIcon(option.Key, 16, "TextPrimaryBrush");
            if (icon is not null)
                row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = option.Label, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private void UpdateOkEnabled()
        {
            _okButton.IsEnabled = !string.IsNullOrWhiteSpace(_nameBox.Text);
        }

        private void OnOkClick()
        {
            var icon = _iconCombo.SelectedItem as UtilitiesIconOption;
            Result = new UtilitiesGroupEditParams(
                _nameBox.Text?.Trim() ?? string.Empty,
                icon is null ? null : icon.Key);
            DialogResult = true;
            Close();
        }
    }
}
#endif