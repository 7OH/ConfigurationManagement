#if LINUX
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Configuration_Management
{
    /// <summary>
    /// Общий механизм закрытия всплывающих подсказок (ToolTip), пользовательских Popup
    /// и контекстных меню для Avalonia/Linux (issue #270). Аналог WPF-класса
    /// <see cref="ToolTipCloser"/> (файл <c>ToolTipCloser.cs</c>), но собранный под Avalonia.
    /// </summary>
    /// <remarks>
    /// Зачем общий механизм, а не инстансный обход визуального дерева окна:
    /// <list type="bullet">
    /// <item>главное окно отслеживало владельцев открытых подсказок глобально
    /// (класс-обработчик изменения <c>ToolTip.IsOpenProperty</c>), а диалоги
    /// (<see cref="ModalWindowBase"/>) полагались только на обход своего визуального
    /// дерева и цепочку фокуса — если владелец подсказки не находился, первый ESC
    /// закрывал диалог целиком (issue #270);</item>
    /// <item>пользовательские Popup (HelpLink и т.п.) и ContextMenu в диалогах по ESC
    /// не закрывались вовсе;</item>
    /// <item>диагностический трейс <c>CM_TOOLTIP_TRACE=1</c> существовал только в
    /// WPF-части, поэтому на Linux лог не создавался.</item>
    /// </list>
    /// Реестры открытых элементов заполняются класс-обработчиками изменения свойств
    /// <c>IsOpen</c> и не зависят от того, где физически отрисован попап (в визуальном
    /// дереве окна или в оверлейном слое TopLevel). Первый ESC в ЛЮБОМ окне закрывает
    /// подсказки через <see cref="CloseAll"/> и помечает событие обработанным, повторный
    /// ESC работает штатно (закрывает окно). Вызов <see cref="Register"/> идемпотентен и
    /// безопасен из конструкторов <see cref="ModalWindowBase"/> и главного окна.
    /// </remarks>
    internal static class ToolTipCloserAvalonia
    {
        // ---- Отслеживаемые элементы (глобально, для всех окон) ----

        // Владельцы открытых ToolTip (элементы, к которым привязан тултип). Основной путь
        // закрытия: ToolTip.SetIsOpen(owner, false). Класс-обработчик изменения
        // ToolTip.IsOpenProperty срабатывает независимо от того, лежит ли владелец в
        // визуальном дереве окна или в оверлейном слое TopLevel (issue #261/#270).
        private static readonly HashSet<Control> _openToolTipOwners = new();

        // Открытые контекстные меню приложения (глобально). Первый ESC должен закрыть и их,
        // иначе окно закроется, а меню останется «висеть» (issue #261/#270).
        private static readonly HashSet<ContextMenu> _openContextMenus = new();

        // Открытые пользовательские Popup-подсказки (HelpLink и т.п., issue #275/#270).
        // Штатные дропдауны селекторов/календарей фильтруются в OnPopupIsOpenChanged через
        // ShouldClosePopup и в этот набор не попадают.
        private static readonly HashSet<Popup> _openPopups = new();

        // Владельцы подсказок, подавленных после закрытия по ESC (issue #270): пока владелец
        // числится здесь, повторное автоматическое открытие его ToolTip гасится в
        // OnToolTipIsOpenChanged — подсказка не «возвращается» при наведённом курсоре.
        private static readonly HashSet<Control> _suppressedToolTipOwners = new();

        /// <summary>Момент последнего закрытия подсказки по ESC (Environment.TickCount64).</summary>
        private static long _lastToolTipEscTick;

        /// <summary>Окно подавления повторного открытия подсказки после ESC (мс), как в MainWindow.Avalonia.cs.</summary>
        private const long ToolTipSuppressWindowMs = 800;

        /// <summary>Признак того, что класс-обработчики уже зарегистрированы (Register идемпотентен).</summary>
        private static bool _registered;

        // ---- Диагностический трейс (CM_TOOLTIP_TRACE=1) ----
        // Тот же путь и формат, что в WPF-версии ToolTipCloser.cs: на Linux Path.GetTempPath()
        // даёт /tmp (уважает TMPDIR). Строка: «yyyy-MM-dd HH:mm:ss.fff [tid] message».
        private static readonly bool _traceEnabled =
            string.Equals(Environment.GetEnvironmentVariable("CM_TOOLTIP_TRACE"), "1", StringComparison.OrdinalIgnoreCase);
        private static readonly string _tracePath = Path.Combine(Path.GetTempPath(), "cm_tooltip_trace.log");
        private static readonly object _traceLock = new();

        /// <summary>
        /// Регистрирует класс-обработчики отслеживания открытых подсказок, пользовательских
        /// Popup и контекстных меню (issue #270). Вызывается из конструктора каждого окна,
        /// участвующего в механизме (<see cref="ModalWindowBase"/>, главное окно); повторные
        /// вызовы безопасны (регистрация выполняется один раз).
        /// </summary>
        public static void Register()
        {
            if (_registered)
                return;
            _registered = true;

            // Класс-обработчик открытия/закрытия ToolTip: запоминает владельца подсказки.
            ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>(OnToolTipIsOpenChanged);

            // Открытые контекстные меню (issue #261/#270): первый ESC должен закрыть меню
            // в любом окне, а не само окно.
            ContextMenu.IsOpenProperty.Changed.AddClassHandler<ContextMenu>(OnContextMenuIsOpenChanged);

            // Пользовательские Popup-подсказки (HelpLink и т.п., issue #275/#270): штатные
            // дропдауны селекторов/календарей фильтруются в OnPopupIsOpenChanged.
            Popup.IsOpenProperty.Changed.AddClassHandler<Popup>(OnPopupIsOpenChanged);

            TraceLog($"Register: класс-обработчики установлены (путь лога: {_tracePath})");
        }

        /// <summary>
        /// Закрывает все открытые всплывающие элементы приложения (issues #270, #275): тултипы,
        /// контекстные меню и пользовательские Popup-подсказки (HelpLink и т.п.).
        /// Возвращает true, если был закрыт хотя бы один элемент. Используется по ESC (первый
        /// ESC закрывает подсказки и помечает событие обработанным — окно не трогается, второй
        /// ESC закрывает окно) и при потере фокуса окном (Deactivated).
        /// </summary>
        public static bool CloseAll()
        {
            var closed = false;
            closed |= CloseOpenToolTips();
            closed |= CloseOpenContextMenus();
            closed |= CloseOpenPopups();
            TraceLog($"CloseAll: итог closed={closed}, тултипов={_openToolTipOwners.Count}, " +
                     $"меню={_openContextMenus.Count}, попапов={_openPopups.Count}");
            return closed;
        }

        /// <summary>
        /// Закрывает все открытые ToolTip приложения. Основной путь — по владельцам, записанным
        /// класс-обработчиком изменения <see cref="ToolTip.IsOpenProperty"/>: это надёжнее обхода
        /// визуального дерева, потому что открытый ToolTip рендерится попапом в оверлейном слое
        /// TopLevel. Резервный путь — обход визуального дерева всех окон и цепочки визуальных
        /// родителей сфокусированного элемента (владелец мог оказаться вне оверлея).
        /// </summary>
        private static bool CloseOpenToolTips()
        {
            var closed = false;

            foreach (var owner in _openToolTipOwners.ToArray())
            {
                if (ToolTip.GetIsOpen(owner))
                {
                    ToolTip.SetIsOpen(owner, false);
                    closed = true;
                    // Подавляем повторное автоматическое открытие (issue #270): иначе при
                    // наведённом курсоре тултип тут же откроется снова.
                    SuppressToolTipOwner(owner);
                }
            }
            if (closed)
            {
                _openToolTipOwners.Clear();
                TraceLog("CloseOpenToolTips: закрыты по владельцам");
            }

            // Резервный путь: обход визуального дерева открытых окон и цепочки родителей
            // сфокусированного элемента (подсказки, не попавшие в отслеживание).
            if (!closed)
            {
                foreach (var window in GetWindows())
                {
                    if (CloseToolTipsIn(window))
                        closed = true;
                }
                if (GetFocusedVisual() is { } focused)
                {
                    for (var node = focused; node is not null; node = node.GetVisualParent())
                    {
                        if (node is Control control && ToolTip.GetIsOpen(control))
                        {
                            ToolTip.SetIsOpen(control, false);
                            closed = true;
                            SuppressToolTipOwner(control);
                        }
                    }
                }
            }

            if (closed)
                TraceLog("CloseOpenToolTips: закрыты тултипы");
            return closed;
        }

        /// <summary>Закрывает все открытые ToolTip в поддереве (резервный путь).</summary>
        private static bool CloseToolTipsIn(Visual root)
        {
            var closed = false;
            foreach (var node in root.GetVisualDescendants())
            {
                if (node is Control control && ToolTip.GetIsOpen(control))
                {
                    ToolTip.SetIsOpen(control, false);
                    closed = true;
                    SuppressToolTipOwner(control);
                }
            }
            return closed;
        }

        /// <summary>
        /// Закрывает все открытые контекстные меню приложения (issue #261/#270). Используется
        /// из <see cref="CloseAll"/>: первый ESC закрывает открытое меню в любом окне (не только
        /// главном), а не само окно.
        /// </summary>
        private static bool CloseOpenContextMenus()
        {
            var closed = false;
            foreach (var menu in _openContextMenus.ToArray())
            {
                if (menu.IsOpen)
                {
                    try { menu.Close(); } catch { /* не роняем по одному меню */ }
                    closed = true;
                }
            }
            if (closed)
            {
                _openContextMenus.Clear();
                TraceLog("CloseOpenContextMenus: закрыты меню");
            }
            return closed;
        }

        /// <summary>
        /// Закрывает открытые пользовательские Popup (всплывающие «пузыри» HelpLink, кастомные
        /// оверлеи), которые не являются ни стандартным ToolTip, ни ContextMenu (issue #261/#270).
        /// Основной путь — по отслеживаемым инстансам <see cref="_openPopups"/>; резервный —
        /// обход визуального дерева всех окон для попапов, открытых до регистрации механизма.
        /// Штатные дропдауны селекторов (ComboBox) и календари DatePicker НЕ трогаем — ими
        /// управляют сами контролы, и грубое гашение IsOpen разорвало бы их внутреннее
        /// состояние (см. <see cref="ShouldClosePopup"/>).
        /// </summary>
        private static bool CloseOpenPopups()
        {
            var closed = false;

            foreach (var popup in _openPopups.ToArray())
            {
                if (popup.IsOpen)
                {
                    try { popup.IsOpen = false; } catch { /* не роняем по одному попапу */ }
                    closed = true;
                }
            }
            if (closed)
            {
                _openPopups.Clear();
                TraceLog("CloseOpenPopups: закрыты попапы по инстансам");
            }

            // Резервный путь: обход визуального дерева окон (попапы, открытые до регистрации).
            if (!closed)
            {
                foreach (var window in GetWindows())
                {
                    if (ClosePopupsIn(window))
                        closed = true;
                }
            }

            if (closed)
                TraceLog("CloseOpenPopups: закрыты пользовательские попапы");
            return closed;
        }

        /// <summary>Закрывает все «пользовательские» открытые <see cref="Popup"/> в поддереве.</summary>
        private static bool ClosePopupsIn(Visual root)
        {
            var any = false;
            foreach (var node in root.GetVisualDescendants())
            {
                if (node is Popup { IsOpen: true } popup && ShouldClosePopup(popup))
                {
                    try { popup.IsOpen = false; } catch { /* не роняем по одному попапу */ }
                    any = true;
                }
            }
            return any;
        }

        /// <summary>
        /// Является ли попап «пользовательским» (закрывается по ESC/потере фокуса), а не штатным
        /// дропдауном контрола: попап, привязанный к селектору (ComboBox), или попап календаря
        /// DatePicker закрываются самими контролами по ESC, поэтому их не гасим.
        /// </summary>
        private static bool ShouldClosePopup(Popup popup)
        {
            if (popup.PlacementTarget is ComboBox or DatePicker)
                return false;
            return !ContainsCalendar(popup.Child);
        }

        /// <summary>Содержит ли поддерево элемент календаря DatePicker (в таком случае попап — штатный).</summary>
        private static bool ContainsCalendar(Control? root)
        {
            if (root is null)
                return false;
            foreach (var node in root.GetVisualDescendants())
            {
                if (node is Calendar or DatePicker)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Класс-обработчик изменения <see cref="ToolTip.IsOpenProperty"/>. Запоминает владельцев
        /// открытых подсказок в <see cref="_openToolTipOwners"/> (issue #261/#270), чтобы их можно
        /// было закрыть по ESC детерминированно, не полагаясь на обход визуального дерева окна.
        /// </summary>
        private static void OnToolTipIsOpenChanged(Control owner, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                // Вето на повторное открытие после ESC (issue #261): если владелец подавлен,
                // сразу гасим показанную подсказку и не запоминаем её в открытых.
                if (IsToolTipSuppressed(owner))
                {
                    ToolTip.SetIsOpen(owner, false);
                    return;
                }
                _openToolTipOwners.Add(owner);
                TraceLog($"OnToolTipIsOpenChanged: owner={FormatObject(owner)}, всего={_openToolTipOwners.Count}");
            }
            else
            {
                _openToolTipOwners.Remove(owner);
            }
        }

        /// <summary>
        /// Заблокировано ли повторное открытие подсказки владельца после ESC (issue #261).
        /// Пока указатель над владельцем (<see cref="Control.IsPointerOver"/>) или не вышел
        /// короткий интервал — подавлено; как только курсор ушёл и окно истекло — подавление
        /// снимается, и тултип снова работает как обычно.
        /// </summary>
        private static bool IsToolTipSuppressed(Control owner)
        {
            if (!_suppressedToolTipOwners.Contains(owner))
                return false;

            if (Environment.TickCount64 - _lastToolTipEscTick < ToolTipSuppressWindowMs || owner.IsPointerOver)
                return true;

            _suppressedToolTipOwners.Remove(owner);
            return false;
        }

        /// <summary>Подавляет повторное открытие подсказки владельца после закрытия по ESC (issue #261).</summary>
        private static void SuppressToolTipOwner(Control owner)
        {
            if (_suppressedToolTipOwners.Add(owner))
                _lastToolTipEscTick = Environment.TickCount64;
        }

        /// <summary>Класс-обработчик открытия/закрытия <see cref="ContextMenu"/> (issue #261/#270).</summary>
        private static void OnContextMenuIsOpenChanged(ContextMenu menu, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                _openContextMenus.Add(menu);
                TraceLog($"OnContextMenuIsOpenChanged: menu={FormatObject(menu)}, всего={_openContextMenus.Count}");
            }
            else
            {
                _openContextMenus.Remove(menu);
            }
        }

        /// <summary>Класс-обработчик открытия/закрытия пользовательского <see cref="Popup"/> (issue #275/#270).</summary>
        private static void OnPopupIsOpenChanged(Popup popup, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                // Штатные дропдауны селекторов/календарей не трогаем — ими управляют контролы.
                if (ShouldClosePopup(popup))
                {
                    _openPopups.Add(popup);
                    TraceLog($"OnPopupIsOpenChanged: popup={FormatObject(popup)}, всего={_openPopups.Count}");
                }
            }
            else
            {
                _openPopups.Remove(popup);
            }
        }

        /// <summary>Открытые окна приложения (классическая десктопная модель жизни).</summary>
        private static IEnumerable<Window> GetWindows()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.Windows;
            return Array.Empty<Window>();
        }

        /// <summary>
        /// Сфокусированный визуальный элемент для резервного пути закрытия тултипов по фокусу
        /// (issue #270): сначала у активного окна, затем у любого окна с фокусом.
        /// </summary>
        private static Visual? GetFocusedVisual()
        {
            foreach (var window in GetWindows())
            {
                if (window.IsActive && window.FocusManager?.GetFocusedElement() is Visual visual)
                    return visual;
            }
            foreach (var window in GetWindows())
            {
                if (window.FocusManager?.GetFocusedElement() is Visual visual)
                    return visual;
            }
            return null;
        }

        // ---- Диагностический трейс (CM_TOOLTIP_TRACE=1) ----

        /// <summary>Короткое представление объекта для трейс-лога.</summary>
        private static string FormatObject(object? o)
        {
            if (o is null)
                return "<null>";
            var type = o.GetType().Name;
            return o is Control c ? $"{type}[{c.Name}]" : type;
        }

        /// <summary>
        /// Пишет строку в файл %TEMP%/tmp/cm_tooltip_trace.log (на Linux — /tmp/cm_tooltip_trace.log,
        /// Path.GetTempPath() уважает TMPDIR), только если задана переменная окружения
        /// CM_TOOLTIP_TRACE=1. В проде вызов — практически бесплатный no-op.
        /// Формат строки совпадает с WPF-версией: «yyyy-MM-dd HH:mm:ss.fff [tid] message».
        /// </summary>
        public static void TraceLog(string message)
        {
            if (!_traceEnabled)
                return;
            try
            {
                lock (_traceLock)
                {
                    File.AppendAllText(_tracePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}\r\n");
                }
            }
            catch
            {
                // Логирование не должно ронять приложение.
            }
        }
    }
}
#endif