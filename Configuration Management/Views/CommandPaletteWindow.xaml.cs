using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Командная палитра (Ctrl+K): быстрый поиск по базам и командам интерфейса.
    /// Окно показывает список, отфильтрованный по тексту; Enter выполняет выбранный
    /// элемент («1С:Предприятие» для базы), Shift+Enter запускает базу в Конфигураторе,
    /// Esc закрывает. Результат доступен через <see cref="Result"/> после ShowDialog.
    /// </summary>
    public partial class CommandPaletteWindow : Window
    {
        private readonly CommandPaletteViewModel _palette = new();

        /// <summary>Выбранный элемент (null — окно закрыто без выбора).</summary>
        public CommandPaletteItem? Result { get; private set; }

        /// <summary>True — выбор сделан с Shift (Конфигуратор для базы).</summary>
        public bool UseConfigurator { get; private set; }

        public CommandPaletteWindow(IEnumerable<CommandPaletteItem> bases, IEnumerable<CommandPaletteItem> commands)
        {
            InitializeComponent();

            _palette.SetSource(bases, commands);
            ItemList.ItemsSource = _palette.VisibleItems;

            Loaded += (_, _) =>
            {
                // Фокус отложенно: синхронный Focus() в конструкторе перехватывается
                // показом модального окна (тот же паттерн, что в остальных диалогах).
                SearchBox.Focus();
            };
        }

        private void OnSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var caretAtEnd = SearchBox.CaretIndex >= SearchBox.Text.Length;
            _palette.ApplyQuery(SearchBox.Text ?? "");
            ItemList.ItemsSource = _palette.VisibleItems;
            if (_palette.VisibleItems.Count > 0)
                ItemList.SelectedIndex = _palette.SelectedIndex;
            if (caretAtEnd)
                SearchBox.CaretIndex = SearchBox.Text.Length;
        }

        private void OnSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    if (_palette.MoveDown())
                    {
                        ItemList.SelectedIndex = _palette.SelectedIndex;
                        ItemList.ScrollIntoView(ItemList.SelectedItem);
                    }
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (_palette.MoveUp())
                    {
                        ItemList.SelectedIndex = _palette.SelectedIndex;
                        ItemList.ScrollIntoView(ItemList.SelectedItem);
                    }
                    e.Handled = true;
                    break;

                case Key.Enter:
                    Confirm(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                    e.Handled = true;
                    break;

                case Key.Escape:
                    Result = null;
                    Close();
                    e.Handled = true;
                    break;
            }
        }

        private void OnItemList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ItemList.SelectedIndex >= 0)
                _palette.SelectedIndex = ItemList.SelectedIndex;
        }

        private void OnItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Confirm(useConfigurator: false);
        }

        private void Confirm(bool useConfigurator)
        {
            var current = _palette.Current;
            if (current is null)
            {
                Result = null;
            }
            else
            {
                Result = current;
                UseConfigurator = useConfigurator;
            }
            Close();
        }

        /// <summary>
        /// Открывает палитру модально с владельцем и возвращает выбранный элемент
        /// (null — отмена). Используется из вью-моделей обеих платформ не должен:
        /// у каждой платформы своё окно, этот метод — только для WPF.
        /// </summary>
        public static (CommandPaletteItem? Item, bool UseConfigurator) ShowDialogFor(
            IEnumerable<CommandPaletteItem> bases, IEnumerable<CommandPaletteItem> commands)
        {
            var win = new CommandPaletteWindow(bases, commands);
            win.Owner = System.Windows.Application.Current.MainWindow;
            win.ShowDialog();
            return (win.Result, win.UseConfigurator);
        }
    }
}
