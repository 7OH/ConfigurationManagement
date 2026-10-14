#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог создания/правки группы меню «Утилиты» (issue #374): имя группы и иконка
    /// из каталога (с превью). Результат — <see cref="UtilitiesGroupEditParams"/> через
    /// <see cref="Result"/> (null — отмена).
    /// </summary>
    public partial class UtilitiesGroupEditWindow : Window
    {
        /// <param name="icons">Доступные иконки каталога (из ViewModel редактора).</param>
        /// <param name="initialTitle">Текущее имя группы (для правки) или пусто.</param>
        /// <param name="initialIconKey">Текущий ключ иконки (для правки) или пусто.</param>
        public UtilitiesGroupEditWindow(
            IReadOnlyList<UtilitiesIconOption> icons,
            string? initialTitle = null,
            string? initialIconKey = null)
        {
            InitializeComponent();
            Owner = Application.Current?.MainWindow;

            IconCombo.ItemsSource = icons;
            NameBox.Text = initialTitle ?? string.Empty;
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);

            // Выбираем текущую иконку (для правки) или первую из каталога.
            if (!string.IsNullOrWhiteSpace(initialIconKey))
            {
                var match = icons.FirstOrDefault(o => string.Equals(o.Key, initialIconKey, StringComparison.Ordinal));
                if (match is not null)
                    IconCombo.SelectedItem = match;
            }
            if (IconCombo.SelectedItem is null && icons.Count > 0)
                IconCombo.SelectedItem = icons[0];

            Loaded += (_, _) =>
            {
                // Активное поле «Имя» с выделением текста (issue #374). Фокус откладываем:
                // в Loaded окно ещё не активировано, и Focus() не встаёт (текст выделяется,
                // но поле не активно для ввода). После активации окна фокус гарантирован.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Activate();
                    NameBox.Focus();
                    NameBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
                // Диалог чуть выше центра владельца (issue #374).
                Top = Math.Max(SystemParameters.WorkArea.Top, Top - 120);
            };
        }

        /// <summary>Результат диалога: имя и ключ иконки (null — отмена).</summary>
        public UtilitiesGroupEditParams? Result { get; private set; }

        private void OnNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            var icon = IconCombo.SelectedItem as UtilitiesIconOption;
            Result = new UtilitiesGroupEditParams(
                NameBox.Text?.Trim() ?? string.Empty,
                icon is null ? null : icon.Key);
            DialogResult = true;
        }
    }
}
#endif