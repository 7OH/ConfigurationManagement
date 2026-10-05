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
        /// Снимок клика, которым закрыли контекстное меню строки (issue #340, новая стратегия).
        /// Первичный клик запоминается (время, позиция) только для простого левого клика БЕЗ
        /// модификаторов; используется для распознавания ПОВТОРНОЙ доставки того же
        /// PointerPressed в дерево (попап меню освобождает перехват асинхронно) и для
        /// fallback, если повторной доставки не будет.
        /// </summary>
        private BatchSelectionHelper.MenuCloseClickSnapshot? _menuCloseClickSnapshot;

        /// <summary>
        /// Флаг «клик, закрывший меню, ещё не обработан» (issue #340, новая стратегия):
        /// взводится при первичном клике при открытом меню, снимается повторной доставкой
        /// или fallback-обработчиком <see cref="ApplyMenuCloseFallback"/>. Применение выбора
        /// однократно и идемпотентно.
        /// </summary>
        private bool _menuClosePendingApply;

        /// <summary>Целевая база клика, закрывшего меню (для fallback, issue #340).</summary>
        private Infobase? _menuCloseTarget;

        /// <summary>Секция целевой строки: true — «Закреплённые» (для fallback, issue #340).</summary>
        private bool _menuCloseTargetIsPinnedSection;

        /// <summary>
        /// Подписывает обработку клика, закрывшего контекстное меню строки (issue #340).
        /// Туннельная фаза ОКНА срабатывает раньше обработчиков контрола LeveledTreeView.
        /// Выбор применяет ШТАТНАЯ логика контрола (OnRowPointerPressed) — по живому
        /// контейнеру; здесь только фиксируется клик (снимок + флаг) для fallback и
        /// распознаётся повторная доставка, чтобы отменить fallback. Событие НЕ гасится.
        /// Снимок живёт до первого отпускания кнопки мыши (двойной клик не блокируется).
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

            // Снимок присутствовал в момент клика (F1): стабилизация выполняется не только
            // при совпавшей повторной доставке (путь A), но и когда снимок был сброшен до
            // доставки (путь C) — это тот же клик, закрывший меню.
            var menuCloseSnapshotPresent = _menuCloseClickSnapshot is not null;

            // Повторная доставка клика, которым закрыли контекстное меню (issue #340, новая
            // стратегия): гасить событие НЕЛЬЗЯ — выбор должна применить штатная логика
            // контрола (OnRowPointerPressed), работающая с живым контейнером. Здесь только
            // снимаем флаг fallback и даём событию дойти до контрола.
            var isMenuCloseRedelivery = false;
            if (_menuCloseClickSnapshot is { } snapshot)
            {
                isMenuCloseRedelivery = BatchSelectionHelper.IsSameClick(snapshot, "Left", Environment.TickCount, pos.X, pos.Y);
                if (isMenuCloseRedelivery)
                {
                    _menuClosePendingApply = false;
                    _menuCloseTarget = null;
                    _menuCloseTargetIsPinnedSection = false;
                }
                // Снимок устарел (прошло больше допуска) или клик в другом месте — это новое
                // действие пользователя: просто сбрасываем снимок, обработка штатная.
                _menuCloseClickSnapshot = null;
            }

            if (menuCloseSnapshotPresent)
            {
                MenuCloseTrace.Log($"PointerPressed: snapshotPresent=true, matched={isMenuCloseRedelivery}, " +
                                   $"path={(isMenuCloseRedelivery ? "A" : "C")}, pos=({pos.X:0.#},{pos.Y:0.#})");
            }

            // Первичный клик по строке базы при ОТКРЫТОМ контекстном меню: меню закрывается
            // этим кликом, его повторная доставка в дерево (после освобождения попапа)
            // обработается ШТАТНО контролом (OnRowPointerPressed) — он применит выбор к
            // живому контейнеру. Здесь запоминаем клик (снимок + флаг) ТОЛЬКО для fallback
            // на случай, если повторной доставки не будет. Снимок пишется только для
            // простого левого клика БЕЗ модификаторов (Ctrl/Shift — штатное мультивыделение).
            // Выбор НЕ применяем и событие НЕ гасим.
            if (_tree.ContextMenu is { IsOpen: true } &&
                source.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault()
                    is { DataContext: Infobase or PinnedInfobaseItem } rowItem &&
                e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed &&
                BatchSelectionHelper.ShouldRecordMenuCloseSnapshot(
                    "Left",
                    (e.KeyModifiers & KeyModifiers.Control) != 0,
                    (e.KeyModifiers & KeyModifiers.Shift) != 0) &&
                BatchSelectionHelper.Unwrap(rowItem.DataContext) is { } clickedBase)
            {
                _menuCloseClickSnapshot = new BatchSelectionHelper.MenuCloseClickSnapshot(
                    "Left", Environment.TickCount, pos.X, pos.Y);
                _menuClosePendingApply = true;
                _menuCloseTarget = clickedBase;
                _menuCloseTargetIsPinnedSection = BatchSelectionHelper.IsPinnedSection(rowItem.DataContext);

                // Диагностика (issue #340, F-поля): активность/видимость окна и число
                // открытых контекстных меню — для проверки гипотезы S4 (деактивация окна
                // закрытием попапа меню и сброс состояния до повторной доставки клика).
                // Аналог _openContextMenus.Count в Avalonia — состояние ContextMenu дерева.
                MenuCloseTrace.Log($"TryApply: snapshot=(Left,t={Environment.TickCount},x={pos.X:0.#},y={pos.Y:0.#}), " +
                                   $"target={clickedBase.Id}, pending=true, pinned={_menuCloseTargetIsPinnedSection}, " +
                                   $"IsVisible={IsVisible}, IsActive={IsActive}, " +
                                   $"openMenusCount={(_tree?.ContextMenu?.IsOpen == true ? 1 : 0)}");

                // Fallback: если повторная доставка клика не придёт (или контрол не применит
                // выбор), выбор ставится по данным; идемпотентен — сработает только пока
                // _menuClosePendingApply взведён и пользователь не перевыбрал строку.
                Avalonia.Threading.Dispatcher.UIThread.Post(ApplyMenuCloseFallback);

                // Контрольный дамп через 500 мс после клика (issue #340, диагностика):
                // итоговое состояние выделения — SelectedItem дерева, модель SelectedInfobase,
                // подсветка контейнера и размер набора мультивыделения.
                var row = _tree.FindRowForData(clickedBase, _menuCloseTargetIsPinnedSection);
                var dumpTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                dumpTimer.Tick += (_, _) =>
                {
                    dumpTimer.Stop();
                    var selectedItem = BatchSelectionHelper.Unwrap(_tree.SelectedItem);
                    var selectedModel = _vm?.SelectedInfobase;
                    MenuCloseTrace.Log(
                        $"Dump500ms: target={clickedBase.Id}, SelectedItem={(selectedItem?.Id ?? "null")}, " +
                        $"SelectedInfobase={(selectedModel?.Id ?? "null")}, " +
                        $"row.IsSelected={row?.IsSelected}, batch.Count={_vm?.BatchSelectedCount ?? 0}");
                };
                dumpTimer.Start();
            }
        }

        private void OnTreeMenuCloseClickDedup_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            // Снимок сбрасывается; флаг _menuClosePendingApply намеренно НЕ трогаем — если
            // повторная доставка не пришла, выбор применит fallback (ApplyMenuCloseFallback).
            if (_menuCloseClickSnapshot is not null)
            {
                _menuCloseClickSnapshot = null;
                MenuCloseTrace.Log("PointerReleased: snapshotCleared=true");
            }
        }

        /// <summary>
        /// Fallback-применение выбора клика, которым закрыли контекстное меню (issue #340,
        /// новая стратегия, Avalonia). Штатный путь — повторная доставка PointerPressed в
        /// контрол (OnRowPointerPressed) — применяет выбор к живому контейнеру и снимает
        /// флаг <see cref="_menuClosePendingApply"/>. Fallback нужен на случай, если повторной
        /// доставки не произошло: применяет выбор по ДАННЫМ (<see cref="SelectRowByData"/>)
        /// и запускает стабилизацию (<see cref="EnsureSelectionStable"/>). Идемпотентен.
        /// </summary>
        private void ApplyMenuCloseFallback()
        {
            if (!_menuClosePendingApply)
            {
                MenuCloseTrace.Log("Fallback: ran=false (флаг уже снят штатной доставкой)");
                return;
            }
            _menuClosePendingApply = false;

            var target = _menuCloseTarget;
            var isPinnedSection = _menuCloseTargetIsPinnedSection;
            _menuCloseTarget = null;
            _menuCloseTargetIsPinnedSection = false;
            if (target is null || _vm is null || _tree is null)
                return;

            // Пользователь успел перевыбрать другую строку — не вмешиваемся.
            if (_vm.SelectedInfobase is { } current && !ReferenceEquals(current, target))
            {
                MenuCloseTrace.Log($"Fallback: ran=true, target={target.Id}, userReselected=true");
                return;
            }

            // Клик был без модификаторов — семантика обычного клика: единственный выбор.
            var containerFound = _tree.FindRowForData(target, isPinnedSection) is not null;
            _vm.ClearBatchSelection();
            SelectRowByData(target, isPinnedSection);
            MenuCloseTrace.Log($"Fallback: ran=true, target={target.Id}, containerFound={containerFound}, " +
                               $"selectedByData=true, pinned={isPinnedSection}");
            EnsureSelectionStable(target, isPinnedSection);
        }

        /// <summary>
        /// Выбирает строку базы по ДАННЫМ (issue #340, Avalonia): подсветка ставится на
        /// контейнер нужной секции (закреплённая база дублируется в «Закреплённых» и в
        /// своей группе), модель синхронизируется. Строка вне видимой области (контейнер
        /// не реализован) — выбор остаётся на модели и будет подсвечен при появлении.
        /// </summary>
        private void SelectRowByData(Infobase target, bool isPinnedSection)
        {
            if (_vm is null)
                return;
            if (_tree.FindRowForData(target, isPinnedSection) is { } row)
                _tree.SelectRow(row);
            _vm.SelectedInfobase = target;
            _vm.SelectedGroupNode = null;
        }

        /// <summary>
        /// Кратковременная «конвергентная» стабилизация выделения после клика, которым закрыли
        /// контекстное меню (issue #340, седьмая попытка, Avalonia): одноразовая подписка на
        /// LayoutUpdated держится ДО СХОДИМОСТИ (до 10 срабатываний или ~1000 мс, F2) —
        /// отложенная переработка контейнеров после закрытия попапа может произойти позже
        /// прежних 3 проходов/~200 мс. Проверяет соответствие модели и контейнера и
        /// восстанавливает выбор по данным. Мультивыделение не затрагивается; защита от
        /// рекурсии — восстановление только при фактическом расхождении.
        /// </summary>
        private void EnsureSelectionStable(Infobase? target, bool isPinnedSection)
        {
            if (_tree is null || target is null || _vm is null)
                return;

            var passes = 0;
            const int maxPasses = 15;   // F2: расширено с 10 (план 0.3.9.306, 2.4)
            var startTick = Environment.TickCount;
            const int timeoutMs = 1500; // F2: расширено с 1000 (план 0.3.9.306, 2.4)
            const int chaseDelayMs = 800; // F1: одноразовый «догоняющий» таймер

            // Диагностика (issue #340): актуальный SelectedItem дерева и время с начала
            // стабилизации — чтобы по логу видеть, «уезжал» ли SelectedItem к моменту
            // завершения подписки.
            string SelectedItemId() => BatchSelectionHelper.Unwrap(_tree?.SelectedItem)?.Id ?? "null";

            EventHandler onLayoutUpdated = null!;
            onLayoutUpdated = (_, _) =>
            {
                passes++;
                var timeSinceStartMs = Environment.TickCount - startTick;
                if (passes > maxPasses || timeSinceStartMs >= timeoutMs)
                {
                    _tree.LayoutUpdated -= onLayoutUpdated;
                    MenuCloseTrace.Log($"EnsureStable: target={target.Id}, pass={passes}, done=true, " +
                                       $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
                    return;
                }

                // Пользователь перевыбрал другую строку — не вмешиваемся.
                if (!ReferenceEquals(_vm.SelectedInfobase, target))
                {
                    _tree.LayoutUpdated -= onLayoutUpdated;
                    MenuCloseTrace.Log($"EnsureStable: target={target.Id}, pass={passes}, userReselected=true, " +
                                       $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
                    return;
                }

                var matches = SelectionMatchesTarget(target, isPinnedSection);
                var containerRealized = _tree.FindRowForData(target, isPinnedSection) is not null;
                if (!matches)
                    SelectRowByData(target, isPinnedSection);
                MenuCloseTrace.Log($"EnsureStable: target={target.Id}, pass={passes}, matches={matches}, " +
                                   $"containerRealized={containerRealized}, action={(matches ? "skip" : "restored")}, " +
                                   $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
            };

            _tree.LayoutUpdated += onLayoutUpdated;

            // F1 (план 0.3.9.306, 2.4): «догоняющая» стабилизация для нереализованного
            // контейнера. Если в момент старта контейнер целевой строки ещё не реализован
            // (виртуализация Recycling после закрытия попапа), подписка на LayoutUpdated
            // может закончиться раньше, чем контейнер появится, а строка без контейнера
            // не подсвечивается (SelectRowByData при отсутствии контейнера только ставит
            // модель). Одноразовый DispatcherTimer (~800 мс) ПОСЛЕ завершения подписки
            // проверяет реализацию контейнера и применяет выбор (SelectRow), если
            // подсветка так и не встала. Идемпотентно; при перевыборе не вмешивается.
            var containerRealizedAtStart = _tree.FindRowForData(target, isPinnedSection) is not null;
            if (BatchSelectionHelper.ShouldRetryRestoreForUnrealizedContainer(
                    containerRealizedAtStart, userReselected: false, elapsedMs: 0, maxChaseMs: chaseDelayMs))
            {
                var chaseTimer = new Avalonia.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(chaseDelayMs)
                };
                chaseTimer.Tick += (_, _) =>
                {
                    chaseTimer.Stop();
                    var timeSinceStartMs = Environment.TickCount - startTick;
                    if (_tree is null || _vm is null || target is null)
                        return;
                    // Пользователь перевыбрал другую строку — не вмешиваемся.
                    if (!ReferenceEquals(_vm.SelectedInfobase, target))
                        return;

                    var row = _tree.FindRowForData(target, isPinnedSection);
                    if (row is null)
                    {
                        MenuCloseTrace.Log($"EnsureStable: target={target.Id}, chase=notRealized, " +
                                           $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
                        return;
                    }
                    if (!row.IsSelected)
                    {
                        _tree.SelectRow(row);
                        MenuCloseTrace.Log($"EnsureStable: target={target.Id}, chase=applied, " +
                                           $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
                    }
                    else
                    {
                        MenuCloseTrace.Log($"EnsureStable: target={target.Id}, chase=ok, " +
                                           $"selectedItemId={SelectedItemId()}, timeSinceStartMs={timeSinceStartMs}");
                    }
                };
                chaseTimer.Start();
            }
        }

        /// <summary>
        /// Соответствует ли фактическое выделение дерева целевой базе (issue #340, F2, Avalonia):
        /// SelectedItem дерева разворачивается до той же базы И контейнер строки РЕАЛИЗОВАН
        /// и подсвечен. Видимая-но-нереализованная строка (контейнер ещё перерабатывается)
        /// согласованной НЕ считается — стабилизация восстановит выбор по данным
        /// (SelectRowByData идемпотентен) и продолжит подписку до сходимости
        /// (см. <see cref="EnsureSelectionStable"/>).
        /// </summary>
        private bool SelectionMatchesTarget(Infobase target, bool isPinnedSection)
        {
            if (!ReferenceEquals(BatchSelectionHelper.Unwrap(_tree.SelectedItem), target))
                return false;

            var row = _tree.FindRowForData(target, isPinnedSection);
            return row is not null && row.IsSelected;
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