#if WINDOWS
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Configuration_Management
{
    /// <summary>
    /// Общий механизм закрытия всплывающих подсказок (ToolTip) по ESC для WPF-окон
    /// приложения (issue #270): первый ESC закрывает открытые тултипы и помечает событие
    /// обработанным, повторный ESC закрывает само окно (в т.ч. через кнопку IsCancel).
    /// Логика скопирована из окна настроек (<see cref="SettingsWindow"/>) и главного окна
    /// (<see cref="MainWindow"/>), где она работает классами-обработчиками на типы
    /// ToolTip/FrameworkElement: владельцы открытых подсказок записываются независимо от
    /// того, где физически отрисован попап (в визуальном дереве окна или во внешнем
    /// HWND/Popup), поэтому закрытие по ESC детерминированно.
    /// <para>
    /// Использование: вызвать <see cref="Register"/> (идемпотентно, из конструктора окна),
    /// а ESC и потерю фокуса обрабатывать так: <c>PreviewKeyDown</c> — если
    /// <see cref="CloseAll"/> вернул true, пометить <c>e.Handled = true</c>;
    /// <c>Deactivated</c> — просто <see cref="CloseAll"/> (issue #275).
    /// </para>
    /// </summary>
    internal static class ToolTipCloser
    {
        // ---- Закрытие всплывающих подсказок (issue #270) ----
        // Владельцы открытых ToolTip (элементы, к которым привязан тултип). Записываются
        // класс-обработчиками ToolTip.OpenedEvent/ClosedEvent, поэтому не зависят от того,
        // где физически отрисован попап, и надёжно закрываются по ESC даже тогда, когда
        // обход визуального дерева окна владельца подсказки не находит. Храним коллекцию,
        // чтобы закрывались ВСЕ открытые подсказки, а не только последняя.
        private static readonly HashSet<DependencyObject> _openToolTips = new();

        /// <summary>
        /// Владельцы подсказок, подавленных после закрытия по ESC (issue #270): пока владелец
        /// числится здесь, повторное автоматическое открытие его ToolTip вето-обработчиком
        /// ToolTipService.ToolTipOpeningEvent блокируется — подсказка не «возвращается» при
        /// наведённом курсоре. Подавление снимается, когда курсор ушёл с владельца и истёк
        /// короткий интервал после ESC.
        /// </summary>
        private static readonly HashSet<DependencyObject> _suppressedToolTipOwners = new();

        /// <summary>Момент последнего закрытия подсказки по ESC (Environment.TickCount64).</summary>
        private static long _lastToolTipEscTick;

        /// <summary>Окно подавления повторного открытия подсказки после ESC (мс).</summary>
        private const long ToolTipSuppressWindowMs = 800;

        /// <summary>Признак того, что класс-обработчики уже зарегистрированы (Register идемпотентен).</summary>
        private static bool _registered;

        /// <summary>
        /// Регистрирует класс-обработчики отслеживания открытых подсказок (issue #270).
        /// Вызывается из конструктора каждого WPF-окна, участвующего в механизме; повторные
        /// вызовы безопасны (регистрация выполняется один раз). Обработчики классовые —
        /// срабатывают для любой открытой подсказки независимо от того, лежит ли её владелец
        /// в визуальном дереве окна или во внешнем попапе/HWND.
        /// </summary>
        public static void Register()
        {
            if (_registered)
                return;
            _registered = true;

            // Класс-обработчик открытия ToolTip: запоминает владельца.
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.ToolTip),
                System.Windows.Controls.ToolTip.OpenedEvent,
                new RoutedEventHandler(OnToolTipOpened));

            // Класс-обработчик закрытия ToolTip: убирает владельца.
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.ToolTip),
                System.Windows.Controls.ToolTip.ClosedEvent,
                new RoutedEventHandler(OnToolTipClosed));

            // Класс-обработчик Preview (туннелирование) на тип ToolTip: ESC, приходящийся на
            // открытую подсказку (фокус во внешнем попапе/HWND), закрывает именно подсказку,
            // а не всё окно.
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.ToolTip),
                UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(OnToolTipPreviewKeyDown));

            // Класс-обработчик вето на открытие: после закрытия по ESC подавленные владельцы
            // не должны автоматически показывать тултип снова, пока указатель над ними.
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                System.Windows.Controls.ToolTipService.ToolTipOpeningEvent,
                new ToolTipEventHandler(OnToolTipOpening));
        }

        /// <summary>
        /// Закрывает все открытые всплывающие подсказки (ToolTip) приложения (issue #270).
        /// Возвращает true, если была закрыта хотя бы одна подсказка. Основной путь — закрытие
        /// через владельцев, записанных класс-обработчиками <see cref="ToolTip.OpenedEvent"/>/<see cref="ToolTip.ClosedEvent"/>,
        /// поэтому не зависит от того, где физически отрисован попап. Резервные пути — обход
        /// визуального дерева открытых окон и цепочка визуальных родителей элемента под
        /// курсором/в фокусе. Используется и по ESC, и при потере фокуса окна (issue #275).
        /// </summary>
        public static bool CloseAll()
        {
            var closed = false;

            // Основной путь: закрываем ВСЕ открытые подсказки через их владельцев.
            foreach (var owner in _openToolTips.ToArray())
            {
                if (TryCloseToolTip(owner))
                {
                    closed = true;
                    // Подавляем повторное автоматическое открытие (issue #270): иначе при
                    // наведённом курсоре ToolTipService тут же снова покажет подсказку.
                    SuppressToolTipOwner(owner);
                }
            }
            if (closed)
            {
                _openToolTips.Clear();
                return true;
            }

            // Резервный путь: обход визуального дерева открытых окон (для подсказок, не
            // попавших в _openToolTips).
            if (Application.Current is { } app)
            {
                foreach (Window window in app.Windows)
                {
                    if (CloseToolTipsIn(window))
                        closed = true;
                }
            }

            // Попап открытой подсказки размещается вне визуального дерева окна (отдельный
            // HWND/Popup), поэтому до «хозяина» добираемся и по курсору/фокусу.
            closed |= CloseToolTipByMouseOrFocus();

            return closed;
        }

        /// <summary>Закрывает все открытые <see cref="System.Windows.Controls.ToolTip"/> в поддереве.</summary>
        private static bool CloseToolTipsIn(DependencyObject root)
        {
            var closed = false;
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();

                if (node is UIElement ui && TryCloseToolTip(ui))
                {
                    closed = true;
                    SuppressToolTipOwner(ui);
                }

                for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--)
                    queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
            return closed;
        }

        /// <summary>
        /// Закрывает открытую подсказку на элементе под курсором или в фокусе (issue #270).
        /// Всплывающий попап ToolTip живёт вне визуального дерева окна и мог не попасть в обход
        /// <see cref="CloseToolTipsIn"/>, поэтому дотягиваемся до «хозяина» подсказки по цепочке
        /// визуальных родителей от элемента под мышью/в фокусе.
        /// </summary>
        private static bool CloseToolTipByMouseOrFocus()
        {
            var closed = false;
            var candidates = new DependencyObject?[]
            {
                Mouse.DirectlyOver as DependencyObject,
                Keyboard.FocusedElement as DependencyObject
            };

            foreach (var candidate in candidates)
            {
                for (var node = candidate; node is not null; node = VisualTreeHelper.GetParent(node))
                {
                    if (TryCloseToolTip(node))
                    {
                        closed = true;
                        SuppressToolTipOwner(node);
                    }
                }
            }

            return closed;
        }

        /// <summary>
        /// Закрывает открытую подсказку элемента, если таковая есть. Возвращает true,
        /// если элемент держал открытый ToolTip и тот был закрыт (issue #261/#270).
        /// </summary>
        private static bool TryCloseToolTip(DependencyObject element)
        {
            // GetToolTip возвращает объект ToolTip и для строковых подсказок (ToolTip="..."),
            // у него свойство IsOpen доступно на чтение и запись — это надёжный способ погасить
            // уже показанный попап.
            if (System.Windows.Controls.ToolTipService.GetToolTip(element) is System.Windows.Controls.ToolTip tip && tip.IsOpen)
            {
                tip.IsOpen = false;
                return true;
            }

            // Страховка для случая, когда строка ещё не обёрнута в ToolTip, но подсказка уже
            // показана сервисом: снимаем её через отключение/включение тултипа.
            if (System.Windows.Controls.ToolTipService.GetIsOpen(element))
            {
                System.Windows.Controls.ToolTipService.SetIsEnabled(element, false);
                System.Windows.Controls.ToolTipService.SetIsEnabled(element, true);
                return true;
            }

            return false;
        }

        /// <summary>Класс-обработчик открытия <see cref="System.Windows.Controls.ToolTip"/> (issue #270): запоминает владельца.</summary>
        private static void OnToolTipOpened(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ToolTip tip)
            {
                var owner = tip.PlacementTarget ?? tip;
                _openToolTips.Add(owner);
            }
        }

        /// <summary>Класс-обработчик закрытия <see cref="System.Windows.Controls.ToolTip"/> (issue #270): убирает владельца.</summary>
        private static void OnToolTipClosed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ToolTip tip)
            {
                var owner = tip.PlacementTarget ?? tip;
                _openToolTips.Remove(owner);
            }
        }

        /// <summary>
        /// Класс-обработчик ESC на типе <see cref="System.Windows.Controls.ToolTip"/> (issue #270):
        /// когда клавиша приходится на открытую подсказку (фокус во внешнем попапе/HWND),
        /// закрывает подсказку и помечает событие обработанным, чтобы первый ESC не закрыл окно.
        /// </summary>
        private static void OnToolTipPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (sender is System.Windows.Controls.ToolTip tip)
            {
                tip.IsOpen = false;
                var owner = tip.PlacementTarget ?? tip;
                _openToolTips.Remove(owner);
                SuppressToolTipOwner(owner);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Класс-обработчик вето на повторное открытие подсказки (issue #270). Вызывается до
        /// показа тултипа владельца: если владелец подавлен после ESC и указатель всё ещё над
        /// ним (или не истёк короткий интервал) — показ отменяется. Как только курсор ушёл и
        /// интервал истёк — подавление снимается.
        /// </summary>
        private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (_suppressedToolTipOwners.Count == 0 || sender is not DependencyObject owner)
                return;
            if (!_suppressedToolTipOwners.Contains(owner))
                return;

            if (Environment.TickCount64 - _lastToolTipEscTick < ToolTipSuppressWindowMs || IsPointerOver(owner))
            {
                e.Handled = true;
                return;
            }

            _suppressedToolTipOwners.Remove(owner);
        }

        /// <summary>Подавляет повторное открытие подсказки владельца после закрытия по ESC (issue #270).</summary>
        private static void SuppressToolTipOwner(DependencyObject owner)
        {
            _suppressedToolTipOwners.Add(owner);
            _lastToolTipEscTick = Environment.TickCount64;
        }

        /// <summary>Находится ли указатель мыши над элементом или его потомком (issue #270).</summary>
        private static bool IsPointerOver(DependencyObject owner)
        {
            for (var node = Mouse.DirectlyOver as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (ReferenceEquals(node, owner))
                    return true;
            }
            return false;
        }
    }
}
#endif