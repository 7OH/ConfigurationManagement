#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Utilities;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Обработчики событий главного окна (Avalonia/Linux): загрузка окна, сторожевая
    /// таймерная защита оверлея, подписки на вьюмодель и выделение строки дерева.
    /// </summary>
    public partial class MainWindow : Window
    {
        private void OnWindowLoaded(object? sender, RoutedEventArgs e)
        {
            // Инициализация выполняется синхронно при загрузке окна. Откладывать её на
            // следующий кадр нельзя: во время неё могут открываться модальные диалоги
            // (импорт/восстановление конфига), и внутри отложенного колбэка их вложенный
            // цикл сообщений приводил к зависанию приложения.
            _vm?.Initialize();
            // Декор главного окна строился в конструкторе по значению по умолчанию:
            // на этом этапе _settings во вьюмодели ещё не загружены (Initialize читает
            // их только сейчас), поэтому UseSystemTitleBar всегда возвращал false, и
            // сохранённая «Системная рамка окна» после перезапуска не применялась
            // (issue #222; у дополнительных окон настройки к моменту их создания уже
            // были загружены, поэтому там всё работало). После загрузки настроек
            // применяем сохранённое значение повторно: если оно отличается от того,
            // что выбрано при построении, обновляем декор и пересобираем содержимое
            // под нужный режим до привязки прокрутки/горячих клавиш. Прозрачность и
            // непрозрачность окна согласуются внутри ApplySystemDecorations через
            // _opaqueWindow, повторное применение их не ломает.
            var savedSystemTitleBar = _vm?.UseSystemTitleBar ?? false;
            if (savedSystemTitleBar != _useSystemTitleBar)
                ApplySystemTitleBar(savedSystemTitleBar);
            // Настройки читаются здесь, уже после построения содержимого, поэтому
            // переключатели верхней панели строились по значениям по умолчанию
            // и не показывали сохранённое состояние до первого щелчка.
            // Initialize присваивает поля напрямую, без уведомлений, так что
            // обработчик изменений вьюмодели их тоже не догонял.
            SyncTopBarToggles();
            RegisterHotkeys();
            // Шаблон дерева готов только после загрузки окна, раньше внутренней
            // прокрутки ещё нет.
            AttachVerticalScrollBar();
            // Масштаб строк списка (issue #303): применяем сохранённое значение и
            // включаем Ctrl+колесо над деревом — как в редакторах.
            _vm?.ApplyListZoom();
            _tree.AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, (_, e) =>
            {
                if (_vm is null)
                    return;
                // Ctrl+колесо — масштаб строк списка (issue #303), как в редакторах.
                if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
                {
                    _vm.ZoomListBy(e.Delta.Y > 0 ? 0.1 : -0.1);
                    e.Handled = true;
                    return;
                }
                // Shift+колесо — горизонтальная прокрутка списка (issue #309): её ведёт
                // внешний ScrollViewer (listArea), общий с заголовком колонок. Обработчик
                // идёт туннелем раньше штатного скроллера дерева, чтобы тот не «уводил»
                // Offset.X — содержимое дерева двигает внешний контейнер (AttachVerticalScrollBar).
                if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
                {
                    var delta = e.Delta;
                    if (MathUtilities.IsZero(delta.X))
                        delta = new Vector(delta.Y, delta.X);
                    if (_listScroll is { } horizontal)
                    {
                        var hidden = Math.Max(0, horizontal.Extent.Width - horizontal.Viewport.Width);
                        var next = horizontal.Offset.WithX(
                            Math.Clamp(horizontal.Offset.X - delta.X * WheelScrollStep, 0, hidden));
                        if (next != horizontal.Offset)
                        {
                            horizontal.Offset = next;
                            e.Handled = true;
                        }
                    }
                    return;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            if (_vm is not null)
            {
                // Переназначение клавиш меняет и привязки, и подписи в меню.
                _vm.HotkeysChanged += (_, _) =>
                {
                    RegisterHotkeys();
                    if (_tree is not null)
                        _tree.ContextMenu = BuildRowContextMenu();
                };

            }
            SetupTray();
            // Блокировка приложения (issue #294): если приложение было заблокировано
            // в прошлом сеансе (закрыто из трея, выход), при запуске окно открывается
            // с оверлеем и окном ввода пароля. Блокировка снимается только верным
            // паролем; закрытие запроса прячет окно обратно.
            try { _vm?.RestoreAppLockOnStartup(); } catch { /* не блокируем запуск */ }
            // Страховка от «зависшего» оверлея загрузки (issue #153): если фоновая
            // инициализация не завершилась за разумное время — например, на медленном
            // программном рендере/в виртуализации индетерминантный индикатор крутится
            // бесконечно, а блокирующий подложкой оверлей съедает ввод. Таймер сбрасывает
            // IsLoading, и окно возвращается к отзывчивости, даже если инициализация
            // где-то повисла.
            ArmLoadingOverlayWatchdog();
        }

        /// <summary>Максимальное время показа оверлея загрузки перед принудительным скрытием.</summary>
        private static readonly TimeSpan LoadingOverlayMaxDuration = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Запускает одноразовый таймер, который по истечении <see cref="LoadingOverlayMaxDuration"/>
        /// сбрасывает флаг <c>IsLoading</c>, если тот всё ещё взведён. Это последний рубеж:
        /// индикатор не должен оставаться на экране и жечь CPU/блокировать ввод бесконечно.
        /// </summary>
        private void ArmLoadingOverlayWatchdog()
        {
            var timer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = LoadingOverlayMaxDuration
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_vm is not null && _vm.IsLoading)
                {
                    _vm.IsLoading = false;
                    _vm.LogWarning("Оверлей загрузки скрыт по таймауту (фоновая инициализация не завершилась)");
                }
            };
            timer.Start();
        }

        /// <summary>Снимает обработчики вьюмодели, навешенные прошлой сборкой окна.</summary>
        private void DetachViewModelHandlers()
        {
            if (_vm is null)
                return;
            if (_groupNodesChanged is not null)
                _vm.GroupNodes.CollectionChanged -= _groupNodesChanged;
            if (_flatItemsChanged is not null)
                _vm.FlatItems.CollectionChanged -= _flatItemsChanged;
            if (_tagFiltersRebuilt is not null)
                _vm.TagFiltersRebuilt -= _tagFiltersRebuilt;
            if (_vmPropertyChanged is not null)
                _vm.PropertyChanged -= _vmPropertyChanged;
        }

        private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm is null)
                return;
            var selected = _tree.SelectedItem;
            switch (selected)
            {
                // Обёртка строки узла «Закреплённые» (issue #301): во вьюмодель отдаём
                // реальную базу, чтобы правая панель и команды работали как раньше.
                case PinnedInfobaseItem pinned:
                    _vm.SelectedInfobase = pinned.Base;
                    _vm.SelectedGroupNode = null;
                    break;
                case Infobase ib:
                    _vm.SelectedInfobase = ib;
                    _vm.SelectedGroupNode = null;
                    break;
                case GroupNodeViewModel g:
                    _vm.SelectedGroupNode = g;
                    _vm.SelectedInfobase = null;
                    break;
                default:
                    break;
            }
        }
    }
}
#endif