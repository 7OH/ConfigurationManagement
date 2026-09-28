#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;

namespace Configuration_Management
{
    /// <summary>
    /// Модальный диалог прогресса клонирования клиент-серверной ИБ (0.3.9.100,
    /// функция №10). Операция может занимать десятки минут (выгрузка .dt,
    /// создание ИБ, восстановление); окно показывает текущий этап, а клонирование
    /// выполняется в фоновом потоке и не «замораживает» интерфейс. Образец —
    /// <see cref="ConfigDiffProgressWindow"/>. Без кнопки «Отмена», как все
    /// прогресс-окна серии: прерывание = закрытие приложения.
    /// </summary>
    internal sealed class CloneServerProgressWindow : Window
    {
        private readonly TextBlock _stageText;

        public CloneServerProgressWindow()
        {
            Title = LocalizationManager.T("CloneServer.Title");
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