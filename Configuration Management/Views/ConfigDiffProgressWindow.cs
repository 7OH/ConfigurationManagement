#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;

namespace Configuration_Management
{
    /// <summary>
    /// Модальный диалог прогресса сравнения конфигураций (0.3.9.99, функция №9).
    /// Операция может занимать минуты (создание временных ИБ из .cf, выгрузка
    /// конфигураций); окно показывает текущий этап, а сравнение выполняется
    /// в фоновом потоке и не «замораживает» интерфейс. Образец —
    /// <see cref="DetectConfigProgressWindow"/>. Без кнопки «Отмена», как все
    /// прогресс-окна серии: прерывание = закрытие приложения.
    /// </summary>
    internal sealed class ConfigDiffProgressWindow : Window
    {
        private readonly TextBlock _stageText;

        public ConfigDiffProgressWindow()
        {
            Title = LocalizationManager.T("ConfigDiff.Title");
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
            // Цвет текста — из темы (TextPrimaryBrush), как у остальных окон ConfigDiff:
            // у программного окна нет XAML-стиля, а DynamicResource-ссылка обновляет
            // цвет при смене светлой/тёмной темы (issue #316).
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