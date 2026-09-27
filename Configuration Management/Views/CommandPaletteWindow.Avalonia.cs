#if LINUX
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Configuration_Management.Localization;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Командная палитра (Ctrl+K), Avalonia/Linux-версия WPF-окна
    /// <see cref="CommandPaletteWindow"/>. Раскладка лежит в CommandPaletteWindow.axaml,
    /// здесь только поведение: фильтрация по тексту, навигация стрелками,
    /// Enter — выполнить («1С:Предприятие» для базы), Shift+Enter — Конфигуратор, Esc — закрыть.
    /// </summary>
    public partial class CommandPaletteWindow : ModalWindowBase
    {
        private readonly CommandPaletteViewModel _palette = new();
        private TextBox _searchBox = null!;
        private ListBox _itemList = null!;

        /// <summary>Выбранный элемент (null — окно закрыто без выбора).</summary>
        public CommandPaletteItem? Result { get; private set; }

        /// <summary>True — выбор сделан с Shift (Конфигуратор для базы).</summary>
        public bool UseConfigurator { get; private set; }

        public CommandPaletteWindow(IEnumerable<CommandPaletteItem> bases, IEnumerable<CommandPaletteItem> commands)
        {
            AvaloniaXamlLoader.Load(this);

            Title = LocalizationManager.T("Palette.Title");
            this.FindControl<TextBlock>("HintText")!.Text = LocalizationManager.T("Palette.Hint");

            _searchBox = this.FindControl<TextBox>("SearchBox")!;
            _itemList = this.FindControl<ListBox>("ItemList")!;

            _palette.SetSource(bases, commands);
            _itemList.ItemsSource = _palette.VisibleItems;

            _searchBox.TextChanged += (_, _) =>
            {
                _palette.ApplyQuery(_searchBox.Text ?? "");
                _itemList.ItemsSource = _palette.VisibleItems;
                if (_palette.VisibleItems.Count > 0)
                    _itemList.SelectedIndex = _palette.SelectedIndex;
            };
            _searchBox.KeyDown += OnSearchBox_KeyDown;
            _itemList.SelectionChanged += (_, _) =>
            {
                if (_itemList.SelectedIndex >= 0)
                    _palette.SelectedIndex = _itemList.SelectedIndex;
            };
            _itemList.DoubleTapped += (_, _) => Confirm(useConfigurator: false);

            Opened += (_, _) =>
            {
                // Фокус ставим отложенно (после полного показа и активации окна):
                // синхронная установка в Opened слетает до отрисовки модального диалога.
                Dispatcher.UIThread.Post(() => _searchBox.Focus());
            };
        }

        private void OnSearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    if (_palette.MoveDown())
                        _itemList.SelectedIndex = _palette.SelectedIndex;
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (_palette.MoveUp())
                        _itemList.SelectedIndex = _palette.SelectedIndex;
                    e.Handled = true;
                    break;

                case Key.Enter:
                    Confirm(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    e.Handled = true;
                    break;

                case Key.Escape:
                    Result = null;
                    Close();
                    e.Handled = true;
                    break;
            }
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
            DialogResult = true;
            Close();
        }
    }
}
#endif
