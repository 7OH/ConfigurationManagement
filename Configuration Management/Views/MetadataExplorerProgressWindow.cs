#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;

namespace Configuration_Management
{
    /// <summary>
    /// Модальный диалог прогресса выгрузки «Обозревателя метаданных» (0.3.9.133, Windows/WPF).
    /// Выгрузка /DumpConfigToFiles может занимать минуты; окно показывает текущий этап
    /// (<see cref="SetStage"/>), а выгрузка выполняется в фоновом потоке. Образец —
    /// <see cref="ConfigDiffProgressWindow"/>. Без кнопки «Отмена», как все прогресс-окна серии.
    /// </summary>
    internal sealed class MetadataExplorerProgressWindow : Window
    {
        private readonly TextBlock _stageText;

        public MetadataExplorerProgressWindow()
        {
            Title = LocalizationManager.T("MetadataExplorer.Title");
            Width = 420;
            Height = 140;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var bar = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 6,
                Margin = new Thickness(20, 16, 20, 10)
            };
            _stageText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(20, 0, 20, 12)
            };
            // Цвет текста — из темы (TextPrimaryBrush): DynamicResource обновляет
            // цвет при смене светлой/тёмной темы (образец ConfigDiffProgressWindow).
            _stageText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            var panel = new StackPanel();
            panel.Children.Add(bar);
            panel.Children.Add(_stageText);
            Content = panel;
        }

        /// <summary>Обновляет текст текущего этапа. Можно вызывать из фонового потока.</summary>
        public void SetStage(string text)
        {
            if (Dispatcher.CheckAccess())
                _stageText.Text = text;
            else
                Dispatcher.Invoke(() => _stageText.Text = text);
        }
    }
}
#endif