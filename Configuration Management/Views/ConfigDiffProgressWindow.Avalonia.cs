#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    /// <summary>
    /// Модальный диалог прогресса сравнения конфигураций (0.3.9.99, функция №9,
    /// Avalonia/Linux). Операция может занимать минуты (создание временных ИБ из .cf,
    /// выгрузка конфигураций); окно показывает текущий этап, а сравнение выполняется
    /// в фоновом потоке. Образец — <see cref="DetectConfigProgressWindow.Avalonia.cs"/>.
    /// Без кнопки «Отмена», как все прогресс-окна серии.
    /// </summary>
    internal sealed class ConfigDiffProgressWindow : ModalWindowBase
    {
        private readonly TextBlock _stageText;

        public ConfigDiffProgressWindow()
        {
            Title = LocalizationManager.T("ConfigDiff.Title");
            Width = 420;
            Height = 140;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            // Индетерминантный индикатор держит рендер-цикл занятым и на программном
            // рендере/в виртуализации даёт постоянную перерисовку (issue #153). Там
            // рисуем статичную заполненную полосу, как в главном окне.
            var disableAnimations = LinuxRendering.DisableAnimations;
            var bar = new ProgressBar
            {
                IsIndeterminate = !disableAnimations,
                Value = disableAnimations ? 100 : 0,
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
            ThemeBrushes.Bind(_stageText, TextBlock.ForegroundProperty, "TextPrimaryBrush");

            var panel = new StackPanel();
            panel.Children.Add(bar);
            panel.Children.Add(_stageText);
            Content = panel;
        }

        /// <summary>Обновляет текст текущего этапа. Можно вызывать из фонового потока.</summary>
        public void SetStage(string text)
        {
            Dispatcher.UIThread.Post(() => _stageText.Text = text);
        }
    }
}
#endif