#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Configuration_Management.Localization;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог назначения тегов для мультивыделения (issue #315): список
    /// существующих тегов с флажками (мультивыбор) + поле ввода нового тега
    /// с кнопкой «Добавить». Результат — отмеченные теги (включая новые).
    /// Windows/WPF-версия; Avalonia-аналог — <c>TagPickWindow.Avalonia.cs</c>.
    /// </summary>
    public partial class TagPickWindow : Window
    {
        private readonly List<CheckBox> _checks = new();
        private readonly TextBlock _selectedCount;
        private readonly string _selectedFormat;

        /// <summary>
        /// Создаёт диалог выбора тегов.
        /// </summary>
        /// <param name="availableTags">Существующие теги всех баз (источник списка).</param>
        public TagPickWindow(IEnumerable<string>? availableTags)
        {
            InitializeComponent();
            _selectedCount = SelectedCountText;
            _selectedFormat = LocalizationManager.T("TagPick.SelectedCount");

            var tags = (availableTags ?? Enumerable.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tags.Count == 0)
            {
                TagsPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationManager.T("TagPick.NoTags"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 8, 6, 8),
                    Foreground = (Brush)FindResource("TextSecondaryBrush")
                });
            }

            foreach (var tag in tags)
                AddCheckbox(tag, isChecked: false);

            UpdateSelectedCount();

            Loaded += (_, _) =>
            {
                // Фокус ставим отложенно, после полного показа окна: синхронная
                // установка в Loaded слетает до отрисовки модального диалога.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_checks.Count > 0)
                        _checks[0].Focus();
                    else
                        NewTagBox.Focus();
                }));
            };
        }

        /// <summary>
        /// Отмеченные теги (в порядке списка); пусто, если пользователь ничего
        /// не выбрал и не добавил. Вызывающий код трактует пустой результат
        /// как отмену (ничего не меняет).
        /// </summary>
        public IReadOnlyList<string> Result { get; private set; } = Array.Empty<string>();

        private CheckBox AddCheckbox(string tag, bool isChecked)
        {
            var check = new CheckBox
            {
                Content = tag,
                IsChecked = isChecked,
                Margin = new Thickness(2, 3, 2, 3),
                Cursor = Cursors.Hand,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            check.Checked += OnSelectionChanged;
            check.Unchecked += OnSelectionChanged;
            _checks.Add(check);
            TagsPanel.Children.Add(check);
            return check;
        }

        private void OnSelectionChanged(object sender, RoutedEventArgs e)
            => UpdateSelectedCount();

        private void UpdateSelectedCount()
        {
            var count = _checks.Count(c => c.IsChecked == true);
            _selectedCount.Text = string.Format(_selectedFormat, count);
        }

        private void OnAddTag_Click(object sender, RoutedEventArgs e)
            => AddNewTag();

        private void OnNewTagBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddNewTag();
                e.Handled = true;
            }
        }

        /// <summary>Добавляет введённый тег в список и сразу отмечает его.</summary>
        private void AddNewTag()
        {
            var text = NewTagBox.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return;

            // Тег уже есть в списке (регистронезависимо) — просто отмечаем его.
            var existing = _checks.FirstOrDefault(c =>
                string.Equals((string?)c.Content, text, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.IsChecked = true;
                existing.BringIntoView();
                NewTagBox.Clear();
                return;
            }

            var check = AddCheckbox(text, isChecked: true);
            NewTagBox.Clear();
            UpdateSelectedCount();
            check.BringIntoView();
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = _checks
                .Where(c => c.IsChecked == true)
                .Select(c => (string)c.Content!)
                .ToList();
            DialogResult = true;
        }
    }
}
#endif