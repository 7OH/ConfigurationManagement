#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using MaterialDesignThemes.Wpf;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Configuration_Management
{
    public partial class MainWindow
    {

        /// <summary>
        /// Регистрирует настраиваемые горячие клавиши действий (запуск, правка, удаление и т.д.).
        /// </summary>
        private void RegisterLaunchHotkeys()
        {
            // Удаляем ранее зарегистрированные «пользовательские» биндинги (кроме Alt+1…9).
            var toRemove = InputBindings
                .OfType<KeyBinding>()
                .Where(kb => kb.Command is not null &&
                             kb.Modifiers != ModifierKeys.Alt)
                .ToList();
            foreach (var kb in toRemove)
                InputBindings.Remove(kb);

            void Add(string? gesture, ICommand? command)
            {
                if (command is null) return;
                if (!TryParseKeyGesture(gesture, out var key, out var mods)) return;
                try
                {
                    InputBindings.Add(new KeyBinding(command, key, mods));
                }
                catch
                {
                    // Одно неверное значение (например, записанное до правки issue #204
                    // сочетание без модификатора) не должно обрывать регистрацию остальных.
                }
            }

            Add(_viewModel.HotkeyEnterprise, _viewModel.LaunchEnterpriseCommand);
            Add(_viewModel.HotkeyConfigurator, _viewModel.LaunchConfiguratorCommand);
            Add(_viewModel.HotkeyFavorite, _viewModel.ToggleFavoriteCommand);
            Add(_viewModel.HotkeyEdit, _viewModel.EditInfobaseCommand);
            Add(_viewModel.HotkeyDelete, _viewModel.DeleteInfobaseCommand);
            Add(_viewModel.HotkeyClearCache, _viewModel.ClearCacheCommand);
            Add(_viewModel.HotkeyAdd, _viewModel.AddInfobaseCommand);
            Add(_viewModel.HotkeyPin, _viewModel.TogglePinCommand);
            // Переключение вкладок списка баз: Все / Избранное / Недавние.
            Add(_viewModel.HotkeyShowAll, _viewModel.ShowAllCommand);
            Add(_viewModel.HotkeyShowFavorites, _viewModel.ShowFavoritesCommand);
            Add(_viewModel.HotkeyShowRecent, _viewModel.ShowRecentCommand);

            // Очистка строки поиска и сброс фильтра тегов — настраиваемые хоткеи (issue #160),
            // значения по умолчанию Ctrl+Shift+C / Ctrl+Shift+T задаются в настройках.
            Add(_viewModel.HotkeyClearSearch, _viewModel.ClearSearchCommand);
            Add(_viewModel.HotkeyClearTags, _viewModel.ClearTagFiltersCommand);

            // Переключение подробностей правой панели информации — настраиваемый
            // хоткей (issue #172); значение по умолчанию Ctrl+D задаётся в настройках.
            Add(_viewModel.HotkeyRightPanelDetails, _viewModel.ToggleRightPanelDetailsCommand);

            // «Найти в списке» — переход к базе в общем списке (issue #285);
            // значение по умолчанию Ctrl+T задаётся в настройках.
            Add(_viewModel.HotkeyFindInList, _viewModel.FindInListCommand);

            // Командная палитра (Ctrl+K): быстрый поиск баз и команд интерфейса.
            Add(_viewModel.HotkeyCommandPalette, _viewModel.CommandPaletteCommand);

            // Смена пользователя — настраиваемый хоткей (issue #200);
            // значение по умолчанию не задано.
            Add(_viewModel.HotkeySwitchUser, _viewModel.SwitchUserCommand);

            // Проверка обновлений конфигураций 1С (функции №21/№22): F9 — для выбранной
            // ИБ, ALT+F9 — окно «Актуальные релизы». Сочетания настраиваются в настройках.
            Add(_viewModel.HotkeyCheckUpdate, _viewModel.CheckUpdateCommand);
            Add(_viewModel.HotkeyActualReleases, _viewModel.ShowActualReleasesCommand);
            // Автообновление платформы 1С (функция 9, этап 0.3.9.214): Ctrl+F9.
            Add(_viewModel.HotkeyPlatformUpdate, _viewModel.ShowPlatformUpdateCommand);

            // Сценарии резервирования и «Список выгрузок» (функции №16/№18):
            // Ctrl+Shift+F5 — выполнить сценарий, Ctrl+Shift+F7 — список выгрузок.
            Add(_viewModel.HotkeyRunBackup, _viewModel.RunBackupScenarioCommand);
            Add(_viewModel.HotkeyExportsList, _viewModel.ShowExportsListCommand);
            // «Выполнить скрипт» для выбранной базы (issue #308): F5.
            Add(_viewModel.HotkeyRunScript, _viewModel.RunScriptForSelectedCommand);

            // Блокировка сеансов ИБ (функция №20, Ctrl+Alt+L) и временная блокировка
            // приложения паролем (функция №19). Сочетания настраиваются в настройках.
            Add(_viewModel.HotkeySessionLock, _viewModel.ShowSessionLockCommand);
            Add(_viewModel.HotkeyLockApp, _viewModel.LockAppCommand);

            // Масштаб строк списка (issue #303): Ctrl++ / Ctrl+- / Ctrl+0, как в
            // редакторах. Сочетания настраиваются в «Настройки → Клавиши».
            Add(_viewModel.HotkeyZoomIn, _viewModel.ZoomInCommand);
            Add(_viewModel.HotkeyZoomOut, _viewModel.ZoomOutCommand);
            Add(_viewModel.HotkeyZoomReset, _viewModel.ZoomResetCommand);

            // Администрирование ИБ (Этап 6, функция №29 + консоль серверов):
            // проверка целостности файловой ИБ (chdbfl) и консоль администрирования серверов 1С.
            Add(_viewModel.HotkeyCheckIntegrity, _viewModel.CheckIntegrityCommand);
            Add(_viewModel.HotkeyServerConsole, _viewModel.OpenServerConsoleCommand);
            // Сохранение копии экрана (функция №30 StartManager): сочетание настраивается в настройках.
            Add(_viewModel.ScreenshotHotkey, _viewModel.TakeScreenshotCommand);

            // Ctrl+Shift+Plus / Ctrl+Shift+Minus — развернуть/свернуть все узлы дерева.
            // Регистрируются обе раскладки (основная Oem* и цифровой блок Add/Subtract).
            InputBindings.Add(new KeyBinding(_viewModel.ExpandAllGroupsCommand, Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift));
            InputBindings.Add(new KeyBinding(_viewModel.ExpandAllGroupsCommand, Key.Add, ModifierKeys.Control | ModifierKeys.Shift));
            InputBindings.Add(new KeyBinding(_viewModel.CollapseAllGroupsCommand, Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift));
            InputBindings.Add(new KeyBinding(_viewModel.CollapseAllGroupsCommand, Key.Subtract, ModifierKeys.Control | ModifierKeys.Shift));
        }

        /// <summary>
        /// Разбирает жест вида «F3», «Delete», «Ctrl+F2», «Shift+Insert».
        /// </summary>
        internal static bool TryParseKeyGesture(string? text, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;
            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(text.Trim(), "—", StringComparison.Ordinal) ||
                string.Equals(text.Trim(), "-", StringComparison.Ordinal) ||
                string.Equals(text.Trim(), "Нет", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text.Trim(), "None", StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = text.Trim().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                var p = parts[i];
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Control;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Shift;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Alt;
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                         p.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Windows;
                else
                    return false;
            }

            var keyPart = parts[^1];
            // Синонимы
            if (keyPart.Equals("Del", StringComparison.OrdinalIgnoreCase))
                keyPart = "Delete";
            if (keyPart.Equals("Ins", StringComparison.OrdinalIgnoreCase))
                keyPart = "Insert";
            if (keyPart.Equals("Esc", StringComparison.OrdinalIgnoreCase))
                keyPart = "Escape";
            // «Отображаемые» имена клавиш из HotkeyBox (KeyToDisplay): без обратных
            // синонимов сохранённые сочетания вида Ctrl+0 / Ctrl++ / Ctrl+- не читались
            // бы из настроек после перезапуска (issue #303 — хоткеи масштаба строк).
            if (keyPart.Length == 1 && keyPart[0] >= '0' && keyPart[0] <= '9')
                keyPart = "D" + keyPart;
            else if (keyPart is "+" or "=")
                keyPart = "OemPlus";
            else if (keyPart == "-")
                keyPart = "OemMinus";
            else if (keyPart == "NumPad+")
                keyPart = "Add";
            else if (keyPart == "NumPad-")
                keyPart = "Subtract";

            if (!Enum.TryParse<Key>(keyPart, true, out var parsed) || parsed == Key.None)
                return false;

            // Сочетание без модификатора WPF не принимает в KeyBinding для букв/цифр
            // (NotSupportedException). Такие значения могли сохраниться в settings.json
            // до правки поля ввода (issue #204) — отбраковываем их при чтении, чтобы
            // они не ломали регистрацию остальных горячих клавиш. Без модификатора
            // допустимы только функциональные клавиши и Delete/Insert — как в HotkeyBox.
            if (modifiers == ModifierKeys.None && !IsAllowedWithoutModifier(parsed))
                return false;

            key = parsed;
            return true;
        }

        /// <summary>
        /// Допустима ли клавиша в сочетании без модификатора: функциональные
        /// клавиши F1…F24, а также Delete и Insert. Буквы и цифры без модификатора
        /// WPF не принимает в KeyBinding, поэтому требуют хотя бы одного модификатора.
        /// Набор совпадает с реализацией в Controls/HotkeyBox.cs.
        /// </summary>
        private static bool IsAllowedWithoutModifier(Key key) =>
            (key >= Key.F1 && key <= Key.F24)
            || key == Key.Delete
            || key == Key.Insert;

        /// <summary>
        /// Регистрирует системные биндинги закладок: Alt+1…Alt+9 (запуск Предприятия),
        /// а также сочетания установки/навигации/очистки/запуска закладок.
        /// Перед добавлением удаляются ВСЕ прежние биндинги этих же жестов (включая
        /// пользовательские), чтобы системное сочетание гарантированно выигрывало.
        /// </summary>
        private void RegisterFavoriteHotkeys()
        {
            // Удаляем предыдущие системные биндинги закладок (и пользовательские,
            // занявшие эти же жесты): см. IsBookmarkSystemBinding.
            var toRemove = InputBindings
                .OfType<KeyBinding>()
                .Where(kb => IsBookmarkSystemBinding(kb.Modifiers, kb.Key))
                .ToList();
            foreach (var kb in toRemove)
                InputBindings.Remove(kb);

            // Alt+1…Alt+9 — запуск Предприятия избранных баз.
            for (int i = 1; i <= 9; i++)
            {
                int index = i;
                InputBindings.Add(new KeyBinding(
                    new ViewModels.RelayCommand(_ => _viewModel.LaunchFavoriteByHotkey(index)),
                    (Key)((int)Key.D0 + i),
                    ModifierKeys.Alt));
            }

            // Ctrl+Shift+P — поставить/снять закладку выбранной базы.
            InputBindings.Add(new KeyBinding(
                new ViewModels.RelayCommand(_ => _viewModel.ToggleBookmarkForCurrent()),
                Key.P, ModifierKeys.Control | ModifierKeys.Shift));

            // Ctrl+Shift+D1..D9 — назначить явный номер закладки.
            for (int i = 1; i <= 9; i++)
            {
                int number = i;
                InputBindings.Add(new KeyBinding(
                    new ViewModels.RelayCommand(_ => _viewModel.AssignBookmarkSlot(_viewModel.SelectedInfobase, number)),
                    (Key)((int)Key.D0 + i),
                    ModifierKeys.Control | ModifierKeys.Shift));
            }

            // Ctrl+D1..D9 — перейти к закладке (раскрыть свёрнутую группу).
            for (int i = 1; i <= 9; i++)
            {
                int number = i;
                InputBindings.Add(new KeyBinding(
                    new ViewModels.RelayCommand(_ => _viewModel.NavigateToBookmark(number)),
                    (Key)((int)Key.D0 + i),
                    ModifierKeys.Control));
            }

            // Ctrl+Alt+X — очистить все закладки.
            InputBindings.Add(new KeyBinding(
                new ViewModels.RelayCommand(_ => _viewModel.ClearAllBookmarks()),
                Key.X, ModifierKeys.Control | ModifierKeys.Alt));

            // Ctrl+Alt+D1..D9 — запустить Конфигуратор по закладке.
            for (int i = 1; i <= 9; i++)
            {
                int number = i;
                InputBindings.Add(new KeyBinding(
                    new ViewModels.RelayCommand(_ => _viewModel.LaunchBookmark(number, true)),
                    (Key)((int)Key.D0 + i),
                    ModifierKeys.Control | ModifierKeys.Alt));
            }

            // Alt+E — запустить все закладки.
            InputBindings.Add(new KeyBinding(
                new ViewModels.RelayCommand(_ => _viewModel.LaunchAllBookmarks()),
                Key.E, ModifierKeys.Alt));

            // Ctrl+B — меню закладок.
            InputBindings.Add(new KeyBinding(
                new ViewModels.RelayCommand(_ => ShowBookmarksMenu()),
                Key.B, ModifierKeys.Control));
        }

        /// <summary>
        /// Признак того, что жесты (модификаторы + клавиша) относятся к системным
        /// сочетаниям закладок. Такие привязки удаляются перед повторной регистрацией,
        /// чтобы пользовательские хоткеи не перебивали их.
        /// </summary>
        private static bool IsBookmarkSystemBinding(ModifierKeys mods, Key key)
        {
            if (key >= Key.D1 && key <= Key.D9)
            {
                return mods == ModifierKeys.Alt
                    || mods == (ModifierKeys.Control | ModifierKeys.Shift)
                    || mods == ModifierKeys.Control
                    || mods == (ModifierKeys.Control | ModifierKeys.Alt);
            }
            if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.P)
                return true;
            if (mods == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.X)
                return true;
            if (mods == ModifierKeys.Alt && key == Key.E)
                return true;
            if (mods == ModifierKeys.Control && key == Key.B)
                return true;
            return false;
        }

        /// <summary>
        /// Показывает контекстное меню закладок (Ctrl+B) относительно позиции курсора.
        /// Для каждой закладки — запуск Предприятия/Конфигуратора, переход и снятие;
        /// внизу — «Очистить все».
        /// </summary>
        private void ShowBookmarksMenu()
        {
            var menu = new ContextMenu();
            var bookmarks = _viewModel.GetBookmarks();

            if (bookmarks.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = LocalizationManager.T("Main.BookmarksNone"), IsEnabled = false });
            }
            else
            {
                foreach (var (number, ib) in bookmarks)
                {
                    var sub = new MenuItem { Header = string.Format(LocalizationManager.T("Main.BookmarksItem"), number, ib.Name) };
                    sub.Items.Add(new MenuItem
                    {
                        Header = string.Format(LocalizationManager.T("Main.BookmarksEnterprise"), number),
                        Command = new ViewModels.RelayCommand(_ => _viewModel.LaunchBookmark(number, false))
                    });
                    sub.Items.Add(new MenuItem
                    {
                        Header = string.Format(LocalizationManager.T("Main.BookmarksConfigurator"), number),
                        Command = new ViewModels.RelayCommand(_ => _viewModel.LaunchBookmark(number, true))
                    });
                    sub.Items.Add(new MenuItem
                    {
                        Header = string.Format(LocalizationManager.T("Main.BookmarksNavigate"), number),
                        Command = new ViewModels.RelayCommand(_ => _viewModel.NavigateToBookmark(number))
                    });
                    sub.Items.Add(new Separator());
                    sub.Items.Add(new MenuItem
                    {
                        Header = LocalizationManager.T("Main.BookmarksRemove"),
                        Command = new ViewModels.RelayCommand(_ => _viewModel.RemoveBookmark(ib))
                    });
                    menu.Items.Add(sub);
                }
                menu.Items.Add(new Separator());
            }

            menu.Items.Add(new MenuItem
            {
                Header = LocalizationManager.T("Main.BookmarksClearAll"),
                Command = new ViewModels.RelayCommand(_ => _viewModel.ClearAllBookmarks())
            });

            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        /// <summary>
        /// Надёжный обработчик Alt+1…9 (KeyBinding с Alt иногда перехватывается системой).
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Блокировка приложения (issue #294): пока активна, клавиатура главного окна
            // не работает — любое нажатие лишь открывает окно ввода пароля. Окно блокировки
            // ввода — отдельное окно, его клавиатура сюда не попадает.
            if (_viewModel.IsAppLocked)
            {
                _viewModel.ShowAppUnlockDialog();
                e.Handled = true;
                return;
            }

            // Ctrl+Shift++ / Ctrl+Shift+- — «развернуть все» / «свернуть все» (issue #160).
            // Обрабатываем на этапе Preview (туннелирование): событие доходит сюда раньше,
            // чем до вложенных элементов и чем оцениваются InputBindings (фаза всплытия),
            // и не зависит от фокуса/времени регистрации привязок. Поэтому хоткей
            // гарантированно срабатывает с первого нажатия. Вызываются те же команды,
            // что и у кнопок верхней панели (ExpandAllGroupsCommand/CollapseAllGroupsCommand),
            // которые, по отзывам, работают сразу. Установка e.Handled = true отменяет
            // всплытие KeyDown, так что дублирующие InputBindings не сработают повторно.
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                if (key is Key.OemPlus or Key.Add)
                {
                    _viewModel.ExpandAllGroupsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }

                if (key is Key.OemMinus or Key.Subtract)
                {
                    _viewModel.CollapseAllGroupsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            // Стрелки ↑/↓/←/→ управляют выделением в списке баз, только если
            // фокус находится в пределах дерева и не в поле ввода текста.
            // Это гарантирует, что стрелки всегда перемещают выделение по дереву,
            // а не «прыгают» по кнопкам внутри строки (избранное, закрепление, теги).
            if (key is Key.Up or Key.Down or Key.Left or Key.Right &&
                Keyboard.Modifiers == ModifierKeys.None &&
                Keyboard.FocusedElement is not TextBox &&
                !IsFocusInsideTagEditor() &&
                IsFocusInsideMainTree())
            {
                if (HandleArrowNavigation(key))
                {
                    e.Handled = true;
                    return;
                }
            }

            // Enter на строке списка = двойной клик (issue #328): база → запуск
            // «1С:Предприятие»/«Конфигуратор» (ResolveDoubleClickAction), группа →
            // свернуть/развернуть. Не срабатывает, когда фокус вне дерева, в
            // текстовом вводе (инлайн-редактор тега, поиск, палитра команд) или
            // открыт модальный диалог — там Enter работает как обычно. В HotkeyBox
            // Enter намеренно не назначается (клавиша ввода/навигации), поэтому
            // конфликта с пользовательскими горячими нет.
            if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None
                && IsFocusInsideMainTree()
                && Keyboard.FocusedElement is not Button and not ToggleButton
                && Services.EnterActivationHelper.CanHandleEnter(
                    Keyboard.FocusedElement is TextBox or PasswordBox || IsFocusInsideTagEditor(),
                    HasOpenModalDialog())
                && HandleRowEnterActivation())
            {
                e.Handled = true;
                return;
            }

            // Esc → в трей (если включено в настройках)
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                // Не перехватываем, если фокус в поле ввода тега — там свой обработчик
                if (IsFocusInsideTagEditor())
                    return;

                // Сначала закрываем открытую подсказку, открытые контекстные меню и пользовательские
                // Popup/оверлеи (issue #261): первый ESC прячет элемент, а не сворачивает/закрывает
                // окно. Иначе главное окно уходит в трей, а элемент остаётся «висеть». После закрытия
                // меню события сюда не доходят (меню обрабатывает ESC само класс-обработчиком
                // OnContextMenuPreviewKeyDown), поэтому повторный ESC уже уводит окно в трей.
                if (ToolTipCloser.CloseAll() || CloseOpenContextMenus() || CloseOpenPopups())
                {
                    e.Handled = true;
                    return;
                }

                if (_viewModel.EscapeToTray && _viewModel.ShowTrayIcon)
                {
                    MinimizeToTray();
                    e.Handled = true;
                    return;
                }
            }

            // Ctrl+F → фокус в поле поиска (в том числе когда фокус в другом поле ввода)
            if (key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (SearchTextBox is not null)
                {
                    SearchTextBox.Focus();
                    SearchTextBox.SelectAll();
                    e.Handled = true;
                    return;
                }
            }

            // Закладки: установка, навигация, очистка и запуск Конфигуратора.
            // Надёжный fallback для наборов цифр с Ctrl/Ctrl+Alt, которые могут
            // перехватываться фокусом или системой.
            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var shift = (mods & ModifierKeys.Shift) == ModifierKeys.Shift;
                var alt = (mods & ModifierKeys.Alt) == ModifierKeys.Alt;

                // Ctrl+Shift+P — поставить/снять закладку выбранной базы.
                if (key == Key.P && shift && !alt)
                {
                    _viewModel.ToggleBookmarkForCurrent();
                    e.Handled = true;
                    return;
                }

                // Ctrl+B — меню закладок.
                if (key == Key.B && !shift && !alt)
                {
                    ShowBookmarksMenu();
                    e.Handled = true;
                    return;
                }

                // Ctrl+Alt+X — очистить все закладки.
                if (key == Key.X && alt && !shift)
                {
                    _viewModel.ClearAllBookmarks();
                    e.Handled = true;
                    return;
                }

                bool isDigit = (key >= Key.D1 && key <= Key.D9)
                    || (key >= Key.NumPad1 && key <= Key.NumPad9);
                if (isDigit)
                {
                    int num = key >= Key.NumPad1 && key <= Key.NumPad9
                        ? key - Key.NumPad0
                        : key - Key.D0;
                    if (shift && !alt)
                    {
                        _viewModel.AssignBookmarkSlot(_viewModel.SelectedInfobase, num);
                        e.Handled = true;
                        return;
                    }
                    if (alt && !shift)
                    {
                        _viewModel.LaunchBookmark(num, true);
                        e.Handled = true;
                        return;
                    }
                    if (!shift && !alt)
                    {
                        _viewModel.NavigateToBookmark(num);
                        e.Handled = true;
                        return;
                    }
                }
            }

            // Alt+E — запустить все закладки.
            if (key == Key.E && mods == ModifierKeys.Alt)
            {
                _viewModel.LaunchAllBookmarks();
                e.Handled = true;
                return;
            }

            // Alt+1…Alt+9 — запуск Предприятия избранных баз (надёжный fallback
            // KeyBinding, см. RegisterFavoriteHotkeys). Обрабатываются РАНЬШЕ хоткеев
            // действий: системные сочетания закладок имеют приоритет (0.3.9.198).
            if (mods == ModifierKeys.Alt && key >= Key.D1 && key <= Key.D9)
            {
                _viewModel.LaunchFavoriteByHotkey(key - Key.D0);
                e.Handled = true;
                return;
            }
            if (mods == ModifierKeys.Alt && key >= Key.NumPad1 && key <= Key.NumPad9)
            {
                _viewModel.LaunchFavoriteByHotkey(key - Key.NumPad0);
                e.Handled = true;
                return;
            }

            // Пользовательские действия по горячей клавише (0.3.9.198): в самом конце
            // цепочки — системные хоткеи (InputBindings) и закладки имеют приоритет.
            // Не срабатываем, пока вводится текст (поле поиска, инлайн-правка тега).
            if (HandleCustomActionHotkey(key))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Выполняет пользовательское действие по совпавшей горячей клавише (0.3.9.198):
        /// ключ <see cref="MainViewModel.HotkeyCustomActions"/> разбирается TryParseKeyGesture
        /// и сравнивается с текущим нажатием; контекст определяется как в подменю
        /// (мультивыделение → база → группа, <see cref="CustomActionExecutionPlan.DetermineMenuContext"/>).
        /// Не срабатывает во время выполнения другого действия и при вводе текста.
        /// Возвращает true, если сочетание распознано как хоткей действия.
        /// </summary>
        private bool HandleCustomActionHotkey(Key key)
        {
            if (_viewModel is null || _viewModel.IsCustomActionRunning || _viewModel.HotkeyCustomActions.Count == 0)
                return false;
            // Не срабатываем, пока вводится текст: поле поиска, пароль/другие TextBox-поля,
            // инлайн-правка тега строки базы (issue #283).
            if (Keyboard.FocusedElement is TextBox or PasswordBox || IsFocusInsideTagEditor())
                return false;

            var mods = Keyboard.Modifiers;
            foreach (var pair in _viewModel.HotkeyCustomActions)
            {
                if (!TryParseKeyGesture(pair.Key, out var parsedKey, out var parsedMods))
                    continue;
                if (parsedKey != key || parsedMods != mods)
                    continue;

                var context = CustomActionExecutionPlan.DetermineMenuContext(
                    _viewModel.BatchSelectedCount,
                    _viewModel.SelectedInfobase is not null,
                    _viewModel.SelectedGroupNode is not null);
                if (context is null)
                    return false;
                _ = ExecuteCustomActionHotkeyAsync(pair.Value, context.Value);
                return true;
            }
            return false;
        }

        /// <summary>Выполняет действие по горячей клавише через общий мост (с тем же подтверждением и индикацией).</summary>
        private async Task ExecuteCustomActionHotkeyAsync(CustomAction action, CustomActionContext context)
        {
            if (_viewModel is null || _viewModel.IsCustomActionRunning)
                return;
            await _viewModel.ExecuteCustomActionAsync(action, context);
        }

        /// <summary>
        /// True, если фокус ввода находится внутри inline-поля правки тега строки базы
        /// (редактируемый ComboBox InlineTagBox или его внутреннее поле ввода). В этом
        /// случае клавиши остаются полю: там свой обработчик Enter/Esc (issue #283).
        /// </summary>
        private bool IsFocusInsideTagEditor()
        {
            if (Keyboard.FocusedElement is not DependencyObject focused)
                return false;

            return focused is ComboBox { Name: "InlineTagBox" }
                || FindAncestor<ComboBox>(focused) is { Name: "InlineTagBox" };
        }

        /// <summary>Открытые контекстные меню главного окна (issue #261).</summary>
        private readonly HashSet<ContextMenu> _openContextMenus = new();

        private void OnContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu)
                _openContextMenus.Add(menu);
        }

        private void OnContextMenuClosed(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu)
                _openContextMenus.Remove(menu);
        }

        /// <summary>
        /// Закрывает контекстное меню по ESC (issue #261). Класс-обработчик Preview на тип
        /// ContextMenu: срабатывает, когда фокус ввода находится внутри открытого меню (попап меню
        /// живёт во внешнем HWND/Popup, куда Window_PreviewKeyDown главного окна не доходит). Прячем
        /// меню и помечаем событие обработанным, чтобы тот же ESC не увёл окно в трей.
        /// </summary>
        private void OnContextMenuPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None
                && sender is ContextMenu { IsOpen: true } menu)
            {
                menu.IsOpen = false;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Закрывает все открытые контекстные меню главного окна (issue #261). Используется при
        /// нажатии ESC: первый ESC должен закрыть открытое меню (меню запуска/выбора клиента, меню
        /// «Утилиты», контекстные меню строк и заголовков), а не сворачивать окно в трей. Возвращает
        /// true, если было закрыто хотя бы одно меню. Закрытие через владельца детерминированно —
        /// меню записываются класс-обработчиками Opened/Closed (см. MainWindow.xaml.cs) независимо
        /// от того, как они были показаны.
        /// </summary>
        private bool CloseOpenContextMenus()
        {
            var closed = false;
            foreach (var menu in _openContextMenus.ToArray())
            {
                if (menu.IsOpen)
                {
                    menu.IsOpen = false;
                    closed = true;
                }
            }
            if (closed)
                _openContextMenus.Clear();
            return closed;
        }

        /// <summary>
        /// Закрывает пользовательские всплывающие элементы (Popup) главного окна, которые не являются
        /// ни стандартными <see cref="System.Windows.Controls.ToolTip"/>, ни <see cref="ContextMenu"/>
        /// (issue #261). Именно такие пользовательские Popup-контейнеры/оверлеи (на скриншотах 7OH —
        /// два всплывающих элемента) оставались открытыми по ESC после фикса контекстных меню в
        /// 0.3.9.17: окно сворачивалось в трей, а попап «висел». Возвращает true, если был закрыт
        /// хотя бы один открытый Popup. Обход ведём по всем окнам приложения, чтобы не зависеть от
        /// того, где физически размещён Popup (в визуальном дереве окна или во внешнем HWND/Popup).
        /// </summary>
        private bool CloseOpenPopups()
        {
            var closed = false;
            foreach (Window window in Application.Current.Windows)
            {
                if (ClosePopupsIn(window))
                    closed = true;
            }
            return closed;
        }

        /// <summary>Закрывает все открытые <see cref="System.Windows.Controls.Primitives.Popup"/> в поддереве.</summary>
        private static bool ClosePopupsIn(DependencyObject root)
        {
            var any = false;
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node is System.Windows.Controls.Primitives.Popup { IsOpen: true } popup)
                {
                    popup.IsOpen = false;
                    any = true;
                }
                for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--)
                    queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
            return any;
        }


        /// <summary>
        /// Определяет, находится ли клавиатурный фокус внутри дерева баз.
        /// Возвращает false, если фокус вне дерева (поле поиска, кнопка верхней панели и т.п.).
        /// </summary>
        private bool IsFocusInsideMainTree()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            return focused is not null && MainTree is not null &&
                   IsDescendantOf(focused, MainTree);
        }

        /// <summary>
        /// Проверяет, является ли <paramref name="candidate"/> потомком <paramref name="root"/> в визуальном дереве.
        /// </summary>
        private static bool IsDescendantOf(DependencyObject candidate, DependencyObject root)
        {
            for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, root))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Обрабатывает нажатие стрелки для навигации по дереву баз (issue #331).
        /// Навигация идёт по фактической иерархии произвольной глубины, а не по
        /// двухуровневой схеме:
        /// ↑/↓ — предыдущий/следующий ВИДИМЫЙ узел в порядке обхода (без перескоков
        /// между секциями «Закреплённые» и обычным списком вопреки порядку);
        /// → — раскрыть свёрнутую папку или перейти к первому потомку;
        /// ← — свернуть развёрнутую папку (ТОЛЬКО её — корневые секции не
        /// затрагиваются) или перейти к родительской строке.
        /// Возвращает true, если событие обработано.
        /// </summary>
        private bool HandleArrowNavigation(Key key)
        {
            if (MainTree is null || _viewModel.GroupNodes.Count == 0)
                return false;

            var rows = GetVisibleTreeViewItems();
            if (rows.Count == 0)
                return false;

            var currentIndex = FindCurrentRowIndex(rows);

            // ↑/↓ — следующий/предыдущий видимый узел. Навигация идёт по контейнерам
            // строк, а не по объектам данных: закреплённая база присутствует в дереве
            // дважды (узел «Закреплённые» и собственная группа), и работа с данными
            // всякий раз находила бы первое (верхнее) вхождение, «перепрыгивая»
            // выделение в начало списка.
            if (key is Key.Up or Key.Down)
            {
                var targetIndex = key == Key.Down
                    ? Services.TreeNavigationHelper.NextVisible(rows.Count, currentIndex)
                    : Services.TreeNavigationHelper.PreviousVisible(currentIndex);
                if (targetIndex < 0 || targetIndex == currentIndex)
                    return false;
                SelectRowItem(rows[targetIndex]);
                return true;
            }

            // ←/→ — по фактической вложенности (произвольная глубина, issue #331).
            // Решение принимает чистый хелпер на основе описания строки; Expand и
            // Collapse оставляют выделение на строке, GoToParent/GoToFirstChild
            // переносят его на строку-цель по контейнерам.
            if (key is Key.Left or Key.Right)
            {
                if (currentIndex < 0)
                    return false;

                var row = rows[currentIndex];
                var info = BuildRowInfoForNavigation(rows, currentIndex);
                var action = key == Key.Right
                    ? Services.TreeNavigationHelper.DecideRight(info)
                    : Services.TreeNavigationHelper.DecideLeft(info);

                switch (action)
                {
                    case Services.TreeNavigationHelper.LateralAction.Expand:
                    case Services.TreeNavigationHelper.LateralAction.Collapse:
                        ToggleGroupExpanded(row);
                        return true;

                    case Services.TreeNavigationHelper.LateralAction.GoToFirstChild:
                    case Services.TreeNavigationHelper.LateralAction.GoToParent:
                    {
                        var target = Services.TreeNavigationHelper.TargetIndex(action, info, currentIndex);
                        if (target >= 0 && target < rows.Count && target != currentIndex)
                            SelectRowItem(rows[target]);
                        return true;
                    }

                    default:
                        // База без детей (или корень без родителя): стрелка ничего не
                        // делает. Помечаем событие обработанным, чтобы штатная логика
                        // TreeView не «сворачивала» соседние секции (кейс 1 issue #331).
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Описание строки для навигации влево/вправо: принадлежность группе,
        /// развёрнутость, наличие потомков и индексы родителя/первого потомка в
        /// видимом порядке. Индексы считаются по контейнерам, чтобы закреплённая
        /// копия базы не «перепрыгивала» выделение в начало списка.
        /// </summary>
        private Services.TreeNavigationHelper.RowInfo BuildRowInfoForNavigation(List<TreeViewItem> rows, int index)
        {
            var row = rows[index];
            var isGroup = row.DataContext is GroupNodeViewModel;
            var hasChildren = row.DataContext is GroupNodeViewModel groupNode && groupNode.Items.Count > 0;
            var isExpanded = row.IsExpanded;

            // Родительская строка — ItemsControl, которому принадлежит контейнер;
            // для вложенной строки это TreeViewItem-предок из того же обхода.
            int? parentIndex = null;
            if (ItemsControl.ItemsControlFromItemContainer(row) is TreeViewItem parentTvi)
            {
                var pi = rows.IndexOf(parentTvi);
                if (pi >= 0)
                    parentIndex = pi;
            }

            int? firstChildIndex = null;
            if (isGroup && isExpanded && hasChildren)
                firstChildIndex = index + 1; // первый потомок идёт сразу после группы

            return new Services.TreeNavigationHelper.RowInfo(
                isGroup, isExpanded, hasChildren, parentIndex, firstChildIndex);
        }

        /// <summary>Раскрывает/сворачивает группу через модель (сохраняя состояние).</summary>
        private void ToggleGroupExpanded(TreeViewItem row)
        {
            if (row.DataContext is GroupNodeViewModel groupNode)
                _viewModel.ToggleGroupExpandedCommand.Execute(groupNode);
        }

        /// <summary>
        /// Выполняет «Enter = двойной клик» по текущей строке списка (issue #328):
        /// группа (включая служебные узлы) сворачивается/разворачивается, база
        /// запускается действием по настройке (как при двойном клике). Возвращает
        /// true, если строка под курсором есть и действие выполнено.
        /// </summary>
        private bool HandleRowEnterActivation()
        {
            if (_viewModel is null)
                return false;

            if (_viewModel.SelectedGroupNode is { } groupNode)
            {
                _viewModel.ToggleGroupExpandedCommand.Execute(groupNode);
                return true;
            }

            if (_viewModel.SelectedInfobase is { } infobase)
            {
                ActivateInfobaseByDoubleClickAction(infobase);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Открыт ли какой-либо другой видимый модальный диалог (свойства базы,
        /// настройки и т.п.). При открытом диалоге Enter не перехватывается —
        /// он работает в самом диалоге как обычно (issue #328).
        /// </summary>
        private bool HasOpenModalDialog()
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (!ReferenceEquals(window, this) && window.IsVisible)
                    return true;
            }
            return false;
        }

    }
}
#endif
