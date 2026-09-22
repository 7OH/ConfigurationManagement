#if WINDOWS
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Configuration_Management
{
    /// <summary>
    /// Общий механизм закрытия всплывающих подсказок (ToolTip) по ESC и при потере фокуса
    /// для WPF-окон приложения (issues #270, #275): первый ESC закрывает открытые тултипы
    /// (в т.ч. строковые подсказки <c>ToolTip="..."</c>) и помечает событие обработанным,
    /// повторный ESC закрывает само окно (в т.ч. через кнопку IsCancel). При потере фокуса
    /// окном (<see cref="Window.Deactivated"/>) и приложением целиком
    /// (<see cref="Application.Deactivated"/>) все открытые подсказки прячутся.
    /// <para>
    /// Владельцы и сами ToolTip-инстансы записываются классами-обработчиками
    /// <see cref="System.Windows.Controls.ToolTip.OpenedEvent"/>/<see cref="System.Windows.Controls.ToolTip.ClosedEvent"/>
    /// независимо от того, где физически отрисован попап (в визуальном дереве окна или во внешнем
    /// HWND/Popup), поэтому закрытие по ESC детерминированно. Основной путь закрытия — по самим
    /// инстансам ToolTip: для строковых подсказок WPF показывает внутренний общий ToolTip, и
    /// присвоение <c>IsOpen = false</c> именно ему гарантированно гасит попап. Трюки с
    /// <c>ToolTipService.GetIsOpen(владелец)</c> и <c>GetToolTip(владелец) is ToolTip</c> для
    /// строковых подсказок НЕ работают: GetToolTip возвращает строку, а сервис выставляет
    /// IsOpenProperty на самом тултипе, а не на владельце — из-за этого в 0.3.9.25 первый ESC
    /// закрывал окно (#270), а потеря фокуса оставляла подсказку висеть (#275).
    /// </para>
    /// <para>
    /// Использование: вызвать <see cref="Register"/> (идемпотентно, из конструктора окна),
    /// а ESC и потерю фокуса обрабатывать так: <c>PreviewKeyDown</c> — если
    /// <see cref="CloseAll"/> вернул true, пометить <c>e.Handled = true</c>;
    /// <c>Deactivated</c> — просто <see cref="CloseAll"/> (issue #275). Дополнительно
    /// <see cref="Register"/> вешает глобальные обработчики: класс-обработчик
    /// <c>UIElement.PreviewKeyDownEvent</c> (handledEventsToo) перехватывает первый ESC в любом
    /// окне и в любом попапе, а <see cref="Application.Deactivated"/> закрывает подсказки при
    /// уходе фокуса с приложения целиком.
    /// </para>
    /// </summary>
    internal static class ToolTipCloser
    {
        // ---- Закрытие всплывающих подсказок (issues #270, #275) ----
        // Открытые ToolTip-инстансы. Основной путь закрытия: IsOpen=false на самом тултипе
        // работает и для объектовых подсказок (XAML <ToolTip>), и для строковых (ToolTip="..."),
        // которые WPF показывает через внутренний общий ToolTip сервиса.
        private static readonly HashSet<System.Windows.Controls.ToolTip> _openToolTips = new();

        // Владельцы открытых ToolTip (элементы, к которым привязан тултип). Используются для
        // подавления повторного открытия после ESC и в резервных путях закрытия.
        private static readonly HashSet<DependencyObject> _openToolTipOwners = new();

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
        /// Регистрирует класс-обработчики отслеживания открытых подсказок, глобальный перехват
        /// ESC и подписку на потерю фокуса приложением (issues #270, #275). Вызывается из
        /// конструктора каждого WPF-окна, участвующего в механизме; повторные вызовы безопасны
        /// (регистрация выполняется один раз). Обработчики классовые — срабатывают для любой
        /// открытой подсказки независимо от того, лежит ли её владелец в визуальном дереве окна
        /// или во внешнем попапе/HWND.
        /// </summary>
        public static void Register()
        {
            if (_registered)
                return;
            _registered = true;

            // Класс-обработчик открытия ToolTip: запоминает сам тултип и его владельца.
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.ToolTip),
                System.Windows.Controls.ToolTip.OpenedEvent,
                new RoutedEventHandler(OnToolTipOpened));

            // Класс-обработчик закрытия ToolTip: убирает тултип и владельца.
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

            // Глобальный перехват ESC на любом UIElement (issue #270): первый ESC гарантированно
            // закрывает открытые подсказки в ЛЮБОМ окне приложения (включая окна без собственного
            // PreviewKeyDown) и помечает событие обработанным ДО срабатывания кнопки IsCancel или
            // хоткея сворачивания в трей. handledEventsToo=true — перехватываем клавишу даже там,
            // где ниже по маршруту её уже начали обрабатывать.
            EventManager.RegisterClassHandler(
                typeof(UIElement),
                UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(OnGlobalPreviewKeyDown),
                handledEventsToo: true);

            // Класс-обработчик вето на открытие: после закрытия по ESC подавленные владельцы
            // не должны автоматически показывать тултип снова, пока указатель над ними.
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                System.Windows.Controls.ToolTipService.ToolTipOpeningEvent,
                new ToolTipEventHandler(OnToolTipOpening));

            // Потеря фокуса приложением целиком (issue #275): открытый тултип в любом окне
            // должен исчезнуть при Alt+Tab / клике в другое приложение. Дублирует
            // Window.Deactivated отдельных окон — второй вызов CloseAll просто вернёт false.
            if (Application.Current is { } app)
                app.Deactivated += OnApplicationDeactivated;
        }

        /// <summary>
        /// Потеря фокуса приложением (issue #275): прячем все открытые подсказки, чтобы они
        /// не «висели» поверх другого окна/браузера.
        /// </summary>
        private static void OnApplicationDeactivated(object? sender, EventArgs e)
        {
            try { CloseAll(); }
            catch { /* потеря фокуса не должна ронять приложение */ }
        }

        /// <summary>
        /// Закрывает все открытые всплывающие подсказки (ToolTip) приложения (issues #270, #275).
        /// Возвращает true, если была закрыта хотя бы одна подсказка. Основной путь — закрытие
        /// через сами инстансы ToolTip, записанные класс-обработчиками
        /// <see cref="System.Windows.Controls.ToolTip.OpenedEvent"/>/<see cref="System.Windows.Controls.ToolTip.ClosedEvent"/>:
        /// это работает и для объектовых, и для строковых подсказок (внутренний общий ToolTip
        /// сервиса) и не зависит от того, где физически отрисован попап. Резервные пути —
        /// закрытие по владельцам (улучшенный <see cref="TryCloseToolTip"/>), обход визуального
        /// дерева открытых окон и цепочка визуальных родителей элемента под курсором/в фокусе.
        /// Используется и по ESC, и при потере фокуса (issue #275).
        /// </summary>
        public static bool CloseAll()
        {
            var closed = false;

            // Основной путь: закрываем ВСЕ открытые подсказки через сами ToolTip-инстансы.
            // Для строковых подсказок это внутренний ToolTip, созданный сервисом, — IsOpen=false
            // на нём гарантированно гасит попап (GetToolTip у строковых возвращает строку, а
            // GetIsOpen на владельце — false, потому что сервис выставляет IsOpen на тултипе).
            foreach (var tip in _openToolTips.ToArray())
            {
                if (tip.IsOpen)
                {
                    try { tip.IsOpen = false; } catch { /* не роняем по одной подсказке */ }
                    closed = true;
                    // Подавляем повторное автоматическое открытие (issue #270): иначе при
                    // наведённом курсоре ToolTipService тут же снова покажет подсказку.
                    SuppressToolTipOwner(tip.PlacementTarget ?? tip);
                }
            }
            if (closed)
            {
                _openToolTips.Clear();
                _openToolTipOwners.Clear();
                return true;
            }

            // Путь по владельцам: подсказки, у которых Opened/Closed не отразились в
            // _openToolTips (например, показанные до регистрации класс-обработчика).
            foreach (var owner in _openToolTipOwners.ToArray())
            {
                if (TryCloseToolTip(owner))
                {
                    closed = true;
                    SuppressToolTipOwner(owner);
                }
            }
            if (closed)
            {
                _openToolTips.Clear();
                _openToolTipOwners.Clear();
                return true;
            }

            // Резервный путь: обход визуального дерева открытых окон (для подсказок, не
            // попавших в отслеживание).
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

        /// <summary>
        /// Глобальный класс-обработчик ESC на типе UIElement (issue #270). Первый ESC в любом
        /// окне/попапе закрывает открытые подсказки и помечает событие обработанным — до того,
        /// как его обработает кнопка IsCancel окна или хоткей сворачивания в трей. Если открытых
        /// подсказок нет — событие не трогаем, и ESC работает как обычно (закрытие окна, меню,
        /// ввод в TextBox/ComboBox/HotkeyBox и т.п.).
        /// </summary>
        private static void OnGlobalPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None)
                return;

            try
            {
                if (CloseAll())
                    e.Handled = true;
            }
            catch
            {
                // Перехват ESC никогда не должен ронять приложение.
            }
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
        /// если элемент держал открытый ToolTip и тот был закрыт (issues #261/#270).
        /// Три пути: (1) у элемента присоединён объектовый ToolTip — гасим его IsOpen;
        /// (2) среди отслеживаемых открытых тултипов есть такой, чей PlacementTarget — этот
        /// элемент (строковые подсказки: GetToolTip возвращает строку, а попап держит
        /// внутренний ToolTip сервиса); (3) сервис числит подсказку открытой через
        /// GetIsOpen — снимаем через SetIsEnabled(false) с отложенным восстановлением true
        /// через Dispatcher (синхронное включение мгновенно переоткрыло бы подсказку, пока
        /// курсор над владельцем).
        /// </summary>
        private static bool TryCloseToolTip(DependencyObject element)
        {
            // Путь 1: объектовый ToolTip (XAML <ToolTip>), присоединённый к элементу.
            if (System.Windows.Controls.ToolTipService.GetToolTip(element) is System.Windows.Controls.ToolTip tip && tip.IsOpen)
            {
                try { tip.IsOpen = false; } catch { }
                return true;
            }

            // Путь 2: строковая подсказка (ToolTip="..."). GetToolTip возвращает строку, поэтому
            // ищем открытый ToolTip, чей владелец (PlacementTarget) — этот элемент.
            foreach (var open in _openToolTips)
            {
                if (ReferenceEquals(open.PlacementTarget, element) && open.IsOpen)
                {
                    try { open.IsOpen = false; } catch { }
                    return true;
                }
            }

            // Путь 3: страховка через состояние сервиса. Снимаем подсказку отключением тултипа,
            // а восстановление true откладываем: синхронное включение тут же переоткрыло бы её
            // (вето ToolTipOpeningEvent при этом подавляет повторный показ — см. OnToolTipOpening).
            if (System.Windows.Controls.ToolTipService.GetIsOpen(element))
            {
                System.Windows.Controls.ToolTipService.SetIsEnabled(element, false);
                element.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { System.Windows.Controls.ToolTipService.SetIsEnabled(element, true); }
                    catch { /* элемент мог быть уничтожен до восстановления */ }
                }), DispatcherPriority.Input);
                return true;
            }

            return false;
        }

        /// <summary>Класс-обработчик открытия <see cref="System.Windows.Controls.ToolTip"/> (issue #270): запоминает тултип и владельца.</summary>
        private static void OnToolTipOpened(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ToolTip tip)
            {
                _openToolTips.Add(tip);
                var owner = tip.PlacementTarget ?? tip;
                _openToolTipOwners.Add(owner);
            }
        }

        /// <summary>Класс-обработчик закрытия <see cref="System.Windows.Controls.ToolTip"/> (issue #270): убирает тултип и владельца.</summary>
        private static void OnToolTipClosed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ToolTip tip)
            {
                _openToolTips.Remove(tip);
                var owner = tip.PlacementTarget ?? tip;
                _openToolTipOwners.Remove(owner);
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
                try { tip.IsOpen = false; } catch { }
                var owner = tip.PlacementTarget ?? tip;
                _openToolTips.Remove(tip);
                _openToolTipOwners.Remove(owner);
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