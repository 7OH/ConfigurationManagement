#if LINUX
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Utilities;
using Avalonia.VisualTree;
using Configuration_Management.Models;
using Configuration_Management.Services;
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
            // Дедупликация клика, которым закрыли контекстное меню строки (issue #340).
            AttachTreeMenuCloseClickDedup();
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

        // ================= Клик, закрывший контекстное меню (issue #340) =================

        /// <summary>
        /// Снимок клика, которым закрыли контекстное меню строки (issue #340, Avalonia).
        /// Тот же механизм, что в WPF: первичный клик запоминается (время, позиция),
        /// а ПОВТОРНАЯ доставка того же PointerPressed в дерево (попап меню освобождает
        /// перехват асинхронно) распознаётся по времени+позиции и гасится — строка не
        /// «перевыбирается», выделение не пропадает «через мгновение».
        /// </summary>
        private BatchSelectionHelper.MenuCloseClickSnapshot? _menuCloseClickSnapshot;

        /// <summary>
        /// Подписывает дедупликацию клика, закрывшего контекстное меню строки (issue #340).
        /// Туннельная фаза ОКНА срабатывает раньше обработчиков контрола LeveledTreeView,
        /// поэтому повторную доставку можно погасить ДО применения выбора. Снимок живёт
        /// до первого отпускания кнопки мыши (двойной клик для запуска базы не блокируется).
        /// </summary>
        private void AttachTreeMenuCloseClickDedup()
        {
            AddHandler(InputElement.PointerPressedEvent, OnTreeMenuCloseClickDedup_PointerPressed, RoutingStrategies.Tunnel);
            AddHandler(InputElement.PointerReleasedEvent, OnTreeMenuCloseClickDedup_PointerReleased, RoutingStrategies.Tunnel);
        }

        private void OnTreeMenuCloseClickDedup_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_tree is null || e.Source is not Visual source)
                return;

            var pos = e.GetPosition(_tree);

            // Повторная доставка клика, которым закрыли контекстное меню: выбор уже применён
            // штатным обработчиком контрола (OnRowPointerPressed) — гасим событие, чтобы
            // «перевыбор» не сбросил выделение.
            if (_menuCloseClickSnapshot is { } snapshot)
            {
                if (!BatchSelectionHelper.IsSameClick(snapshot, "Left", Environment.TickCount, pos.X, pos.Y))
                {
                    // Снимок устарел (прошло больше допуска) или клик в другом месте —
                    // это новое действие пользователя, обрабатываем штатно.
                    _menuCloseClickSnapshot = null;
                }
                else
                {
                    _menuCloseClickSnapshot = null;
                    e.Handled = true;
                    return;
                }
            }

            // Первичный клик по строке базы при ОТКРЫТОМ контекстном меню: меню закрывается
            // этим кликом, и его повторная доставка в дерево (после освобождения попапа)
            // должна быть погашена выше. Выбор применяется СИНХРОННО по данным строки
            // (issue #340): в четырёх прежних попытках на WPF выбор ставился отложенно,
            // и выделение пропадало «через мгновение». Контейнер под курсором сейчас
            // живой (клик только что пришёл) — применяем выбор сразу и запоминаем снимок;
            // повторная доставка «хвоста» гасится снимком и ничего не переприменяет.
            if (_tree.ContextMenu is { IsOpen: true } &&
                source.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault()
                    is { DataContext: Infobase or PinnedInfobaseItem } rowItem &&
                e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed)
            {
                _menuCloseClickSnapshot = new BatchSelectionHelper.MenuCloseClickSnapshot(
                    "Left", Environment.TickCount, pos.X, pos.Y);

                var clickedBase = rowItem.DataContext switch
                {
                    PinnedInfobaseItem pinned => pinned.Base,
                    Infobase ib => ib,
                    _ => null
                };
                if (clickedBase is not null && _vm is not null)
                {
                    _vm.ClearBatchSelection();
                    _vm.SelectedInfobase = clickedBase;
                    _vm.SelectedGroupNode = null;
                    rowItem.IsSelected = true;
                }
            }
        }

        private void OnTreeMenuCloseClickDedup_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _menuCloseClickSnapshot = null;
        }

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