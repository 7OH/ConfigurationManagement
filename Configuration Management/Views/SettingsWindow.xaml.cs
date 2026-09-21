#if WINDOWS
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Microsoft.Win32;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог настроек приложения с горизонтальными вкладками:
    /// «Платформы», «Отображение», «Клавиши», «Настройки», «ibases.v8i», «Базы» и «О программе».
    /// Управление группами — в основном окне (добавление/редактирование через список баз).
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly SettingsViewModel _settings;
        private readonly IDialogService _dialogs =
            AppServices.GetRequiredService<IDialogService>();
        private List<string> _installedPlatformVersions;
        private readonly ObservableCollection<string> _additionalPlatformPaths = new();
        private bool _showFavoritesButton = true;
        private bool _showPinnedButton = true;
        private bool _showTags = true;
        private readonly ObservableCollection<FavoriteHotkeyItem> _favoriteHotkeyItems = new();
        private readonly ObservableCollection<ColumnOrderItem> _columnOrderItems = new();

        // ---- Закрытие всплывающих подсказок (issue #270) ----
        // Владельцы открытых ToolTip (элементы, к которым привязан тултип). Записываются
        // класс-обработчиками ToolTip.OpenedEvent/ClosedEvent, поэтому не зависят от того, где
        // физически отрисован попап, и надёжно закрываются по ESC даже тогда, когда обход
        // визуального дерева окна владельца подсказки не находит. Храним коллекцию, чтобы
        // закрывались ВСЕ открытые подсказки, а не только последняя (паритет с главным окном).
        private readonly HashSet<DependencyObject> _openToolTips = new();

        /// <summary>
        /// Владельцы подсказок, подавленных после закрытия по ESC (issue #270): пока владелец
        /// числится здесь, повторное автоматическое открытие его ToolTip вето-обработчиком
        /// ToolTipService.ToolTipOpeningEvent блокируется — подсказка не «возвращается» при
        /// наведённом курсоре. Подавление снимается, когда курсор ушёл с владельца и истёк
        /// короткий интервал после ESC.
        /// </summary>
        private readonly HashSet<DependencyObject> _suppressedToolTipOwners = new();

        /// <summary>Момент последнего закрытия подсказки по ESC (Environment.TickCount64).</summary>
        private long _lastToolTipEscTick;

        /// <summary>Окно подавления повторного открытия подсказки после ESC (мс).</summary>
        private const long ToolTipSuppressWindowMs = 800;

        // ---- Шрифт интерфейса ----
        // Рабочие копии настроек шрифтов областей хранятся в SettingsViewModel.ElementFonts.
        private string _currentElement = Themes.ThemeManager.FontDefault;

        // ---- Резервное копирование профиля ----
        private System.Windows.Controls.TextBox _profileDirBox = null!;
        private System.Windows.Controls.CheckBox _profileRestoreCheck = null!;

        // ---- Цветовое оформление ----
        private readonly ObservableCollection<ColorItem> _colorItems = new();
        private bool _suppressSchemeEvent;

        // ---- Язык интерфейса ----
        // Выбранный в окне язык. Применяется в обработчике «Сохранить», чтобы
        // «Отмена» не меняла текущий язык и не перезаписывала settings.json (issue #206).
        private string? _pendingLanguageCode;

        // ---- Компактный режим ----
        // Признак «идёт начальная установка значения переключателя»: пока он стоит,
        // событие Checked/Unchecked не должно вызывать ApplyCompactMode, иначе простое
        // открытие окна настроек повторно масштабирует главное окно («прыжок отступов»,
        // issue #199) и компактный режим не возвращается к прежнему виду.
        private bool _suppressCompactEvent;

        // ---- Интеграция с проводником (функция №12) ----
        // Признак «идёт начальная установка значения переключателя»: пока он стоит,
        // событие Checked/Unchecked не должно вызывать повторную регистрацию в реестре.
        private bool _suppressExplorerEvent;

        // ---- Автозапуск при старте ОС (функция №31, Этап 8) ----
        // Признак «идёт начальная установка значения переключателя»: пока он стоит,
        // событие Checked/Unchecked не должно вызывать повторную запись в реестр.
        private bool _suppressAutoStartEvent;

        /// <summary>
        /// Создаёт диалог настроек приложения.
        /// </summary>
        /// <param name="viewModel">Главная модель представления приложения.</param>
        public SettingsWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            // Шаблон имени COM-коннектора 1С (issue #175): показываем текущее значение.
            if (ComConnectorNameTemplateBox != null)
                ComConnectorNameTemplateBox.Text = viewModel.ComConnectorNameTemplate;
            // Режим функциональности (Этап 10 StartManager) и подсказки о 1CLaunch.cfg/портативном режиме.
            InitializeFunctionalModeUi();
            // Интерактивный предпросмотр имени COM-коннектора (issue #175): реагирует
            // на изменение и шаблона, и версии. Поле версии в настройки не сохраняется.
            if (ComConnectorNameTemplateBox != null && ComConnectorPreviewVersionBox != null)
            {
                ComConnectorNameTemplateBox.TextChanged += (_, _) => UpdateComConnectorPreview();
                ComConnectorPreviewVersionBox.TextChanged += (_, _) => UpdateComConnectorPreview();
                UpdateComConnectorPreview();
            }
            // Таймаут определения свойств конфигурации через COM (issue #174).
            if (ComDetectTimeoutMsBox != null)
                ComDetectTimeoutMsBox.Text = viewModel.ComDetectTimeoutMs.ToString();
            // Глубина истории запусков одной базы (issue #246).
            if (MaxLaunchHistoryDepthBox != null)
                MaxLaunchHistoryDepthBox.Text = viewModel.MaxLaunchHistoryPerBase.ToString();
            // Каталоги шаблонов конфигураций: показываем сохранённые значения при открытии,
            // иначе список выглядел бы пустым и «ОК» затирал их пустым списком (как в Avalonia).
            if (TemplatePathsList != null)
            {
                TemplatePathsList.Items.Clear();
                foreach (var p in viewModel.TemplateCatalogPaths ?? new System.Collections.Generic.List<string>())
                {
                    if (!string.IsNullOrWhiteSpace(p))
                        TemplatePathsList.Items.Add(p);
                }
            }
            // Авторизация на сайте 1С при проверке обновлений конфигураций.
            if (UpdatesLoginBox != null)
                UpdatesLoginBox.Text = viewModel.UpdatesLogin;
            if (UpdatesPasswordBox != null)
                UpdatesPasswordBox.Password = viewModel.UpdatesPassword;
            // Глобальное действие по двойному щелчку на базе (функция №28 StartManager).
            InitGlobalDoubleClickCombo();
            // Блокировка сеансов ИБ (функция №20, Ctrl+Alt+L) и временная блокировка
            // приложения паролем (функция №19): показываем текущие значения сочетаний.
            if (HotkeySessionLockBox != null)
                HotkeySessionLockBox.Value = viewModel.HotkeySessionLock;
            if (HotkeyLockAppBox != null)
                HotkeyLockAppBox.Value = viewModel.HotkeyLockApp;
            // Администрирование ИБ (Этап 6, функция №29 + консоль серверов).
            if (HotkeyCheckIntegrityBox != null)
                HotkeyCheckIntegrityBox.Value = viewModel.HotkeyCheckIntegrity;
            if (HotkeyServerConsoleBox != null)
                HotkeyServerConsoleBox.Value = viewModel.HotkeyServerConsole;
            _settings = new SettingsViewModel(viewModel);
            _installedPlatformVersions = new List<string>(viewModel.InstalledPlatformVersions);
            foreach (var path in viewModel.AdditionalPlatformSearchPaths)
                _additionalPlatformPaths.Add(path);
            if (AdditionalPathsList != null)
                AdditionalPathsList.ItemsSource = _additionalPlatformPaths;
            UpdatePlatformsDisplay();
            InitializeDefaultArchitecture();
            InitializeSyncSettings();
            InitializeDisplaySettings();
            InitializeFontSettings();
            InitializeFavoriteHotkeys();
            InitializeExportTimestampSettings();
            InitializeColorSchemes();
            InitializeLanguage();
            InitializeProfileBackupTab();
            InitializeAccountsTab();

            // Интеграция с проводником (функция №12): показываем текущее значение и
            // блокируем событие на время начальной установки (повторная регистрация не нужна).
            if (ExplorerIntegrationCheck != null)
            {
                _suppressExplorerEvent = true;
                ExplorerIntegrationCheck.IsChecked = viewModel.ExplorerIntegrationEnabled;
                _suppressExplorerEvent = false;
            }
            // Автозапуск при старте ОС (функция №31) и копия экрана (функция №30, Этап 8):
            // показываем текущие значения.
            if (AutoStartCheck != null)
            {
                _suppressAutoStartEvent = true;
                AutoStartCheck.IsChecked = viewModel.AutoStartEnabled;
                _suppressAutoStartEvent = false;
            }
            if (ScreenshotDirectoryBox != null)
                ScreenshotDirectoryBox.Text = viewModel.ScreenshotSaveDirectory;
            if (HotkeyScreenshotBox != null)
                HotkeyScreenshotBox.Value = viewModel.ScreenshotHotkey;

            // ESC сначала закрывает открытые всплывающие подсказки, а только потом окно (issue #270).
            // Раньше первый ESC в настройках закрывал всё окно целиком, хотя подсказок там могло
            // быть открыто больше двух. Preview перехватывает клавишу до того, как её обработает
            // кнопка «Отмена» (IsCancel); если подсказки были закрыты — окно на этом ESC не закроется.
            PreviewKeyDown += OnSettingsPreviewKeyDown;

            // Отслеживаем открытый ToolTip глобально (issue #270): класс-обработчик на тип ToolTip
            // срабатывает для любой открытой подсказки независимо от того, лежит ли её владелец в
            // визуальном дереве окна или во внешнем попапе/HWND. Тип квалифицируем полностью:
            // внутри Window идентификатор ToolTip затенён унаследованным свойством
            // FrameworkElement.ToolTip (object), и в выражении ToolTip.OpenedEvent он разрешался
            // бы в это свойство, а не в тип (CS1061).
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.ToolTip),
                System.Windows.Controls.ToolTip.OpenedEvent,
                new RoutedEventHandler(OnToolTipOpened));
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.ToolTip),
                System.Windows.Controls.ToolTip.ClosedEvent,
                new RoutedEventHandler(OnToolTipClosed));

            // Надёжное отслеживание владельцев открытых подсказок через изменение
            // присоединённого свойства ToolTip.IsOpenProperty (issue #270): срабатывает
            // независимо от того, как задана подсказка (строка/объект) и где физически
            // отрисован попап (в визуальном дереве окна или во внешнем HWND/Popup), как и в
            // Avalonia (MainWindow.Avalonia.cs: ToolTip.IsOpenProperty.Changed.AddClassHandler).
            System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(System.Windows.Controls.ToolTip.IsOpenProperty,
                              typeof(System.Windows.Controls.ToolTip))
                ?.AddValueChanged(typeof(System.Windows.Controls.ToolTip), OnToolTipIsOpenGlobalChanged);

            // Класс-обработчик Preview (туннелирование) на тип ToolTip (issue #270): ESC,
            // приходящийся на открытую подсказку (фокус во внешнем попапе/HWND), закрывает
            // именно подсказку, а не всё окно настроек.
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.ToolTip),
                UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(OnToolTipPreviewKeyDown));

            // Класс-обработчик вето на открытие (issue #270): после закрытия по ESC подавленные
            // владельцы не должны автоматически показывать тултип снова, пока указатель над ними.
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                System.Windows.Controls.ToolTipService.ToolTipOpeningEvent,
                new ToolTipEventHandler(OnToolTipOpening));

            // Подсказки скрываются при потере фокуса окна (issue #275), как контекстное меню:
            // при клике в другое окно/приложение открытый тултип исчезает, а не «висит» поверх.
            Deactivated += (_, _) => CloseOpenToolTips();
        }

        /// <summary>
        /// Обновляет интерактивный предпросмотр имени COM-коннектора (issue #175):
        /// разворачивает шаблон по введённой версии. Имя без плейсхолдеров показывается
        /// как есть, даже если поле версии пусто (issue #175). При null (пустой шаблон
        /// либо шаблон с плейсхолдерами, который по этой версии развернуть нельзя)
        /// выводится placeholder.
        /// </summary>
        private void UpdateComConnectorPreview()
        {
            if (ComConnectorPreviewResultBox == null || ComConnectorNameTemplateBox == null || ComConnectorPreviewVersionBox == null)
                return;

            var result = ComConnectorTemplate.Expand(ComConnectorNameTemplateBox.Text, ComConnectorPreviewVersionBox.Text);
            ComConnectorPreviewResultBox.Text = result ?? LocalizationManager.T("Settings.General.ComConnectorPreviewEmpty");
        }

        /// <summary>Переключатель компактного режима: применяет изменение сразу и сохраняет.</summary>
        private void OnCompactMode_Toggled(object sender, RoutedEventArgs e)
        {
            // Начальная установка значения переключателя событием не считается:
            // повторное масштабирование при открытии окна настроек — «прыжок
            // отступов» (issue #199), компактный режим не возвращается к прежнему.
            if (_suppressCompactEvent)
                return;
            if (CompactModeCheck is null)
                return;
            _viewModel.ApplyCompactMode(CompactModeCheck.IsChecked == true);
        }

        /// <summary>Переключатель интеграции с проводником: применяет изменение сразу и сохраняет.</summary>
        private void OnExplorerIntegration_Toggled(object sender, RoutedEventArgs e)
        {
            // Начальная установка значения переключателя событием не считается,
            // чтобы не выполнять лишнюю регистрацию/удаление из реестра при открытии окна.
            if (_suppressExplorerEvent)
                return;
            if (ExplorerIntegrationCheck is null)
                return;
            _viewModel.ApplyExplorerIntegration(ExplorerIntegrationCheck.IsChecked == true);
        }

        /// <summary>Переключатель автозапуска при старте ОС (функция №31): применяет изменение сразу и сохраняет.</summary>
        private void OnAutoStart_Toggled(object sender, RoutedEventArgs e)
        {
            // Начальная установка значения переключателя событием не считается,
            // чтобы не выполнять лишнюю запись/удаление из реестра при открытии окна.
            if (_suppressAutoStartEvent)
                return;
            if (AutoStartCheck is null)
                return;
            _viewModel.ApplyAutoStart(AutoStartCheck.IsChecked == true);
        }

        /// <summary>Выбор каталога сохранения копий экрана (функция №30).</summary>
        private void OnScreenshotBrowse_Click(object sender, RoutedEventArgs e)
        {
            var selected = _dialogs.OpenFolderDialog(
                LocalizationManager.T("Settings.Screenshot.ChooseDir"), ScreenshotDirectoryBox?.Text);
            if (!string.IsNullOrWhiteSpace(selected) && ScreenshotDirectoryBox != null)
                ScreenshotDirectoryBox.Text = selected;
        }

        /// <summary>
        /// Список установленных версий платформы 1С.
        /// </summary>
        public List<string> Result => _installedPlatformVersions;

        /// <summary>
        /// Живая строка версии для вкладки «О программе»: номер из InformationalVersion
        /// без суффикса «+<sha>», как в Avalonia-версии (SettingsWindow.Avalonia.cs).
        /// </summary>
        public string AboutVersion =>
            string.Format(LocalizationManager.T("Settings.About.Version"), VersionInfo.Display());

        /// <summary>
        /// Строгий разбор времени суток расписания синхронизации (ЧЧ:ММ или Ч:ММ).
        /// Обычный TryParse принимает «9» как девять суток и «25:00» как длительность,
        /// поэтому проверяем явно и не принимаем значения от 24 часов и больше.
        /// </summary>
        private static bool IsValidScheduleTime(string value) =>
            TimeSpan.TryParseExact(value.Trim(), new[] { @"hh\:mm", @"h\:mm" },
                System.Globalization.CultureInfo.InvariantCulture, out var time)
            && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);

        /// <summary>Проверяет, что строка — допустимый шаблон даты-времени .NET.</summary>
        private static bool IsValidTimestampFormat(string? format)
        {
            if (string.IsNullOrWhiteSpace(format))
                return true;
            try
            {
                _ = DateTime.Now.ToString(format!.Trim());
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Инициализирует UI вкладки «Настройки» для режима функциональности (Этап 10 StartManager):
        /// выбирает текущий режим и показывает подсказки о портативном режиме и файле 1CLaunch.cfg.
        /// </summary>
        private void InitializeFunctionalModeUi()
        {
            try
            {
                if (FunctionalModeComboBox != null)
                {
                    var mode = _viewModel.FunctionalMode;
                    for (var i = 0; i < FunctionalModeComboBox.Items.Count; i++)
                    {
                        if (FunctionalModeComboBox.Items[i] is ComboBoxItem it &&
                            string.Equals(it.Tag as string, mode, StringComparison.OrdinalIgnoreCase))
                        {
                            FunctionalModeComboBox.SelectedIndex = i;
                            break;
                        }
                    }
                }

                if (FunctionalModeHintText != null)
                {
                    // Подсказка по текущему режиму функциональности (issue #263): у каждого
                    // режима («Пользователь»/«Специалист»/«Разработчик») своё пояснение,
                    // а не только у ограничивающего «Пользователя». Пояснение обновляется
                    // сразу при выборе режима, чтобы разницу «Специалист»/«Разработчик»
                    // пользователь видел без повторного открытия окна.
                    void UpdateFunctionalModeHint()
                    {
                        var code = FunctionalModeComboBox?.SelectedItem is ComboBoxItem it && it.Tag is string c
                            ? c
                            : _viewModel.FunctionalMode;
                        FunctionalModeHintText.Text = Models.FunctionalModes.Parse(code) switch
                        {
                            Models.FunctionalMode.User => LocalizationManager.T("FunctionalMode.UserHint"),
                            Models.FunctionalMode.Developer => LocalizationManager.T("FunctionalMode.DeveloperHint"),
                            _ => LocalizationManager.T("FunctionalMode.SpecialistHint")
                        };
                    }

                    UpdateFunctionalModeHint();
                    if (FunctionalModeComboBox != null)
                        FunctionalModeComboBox.SelectionChanged += (_, __) => UpdateFunctionalModeHint();
                }

                if (LaunchConfigHintText != null)
                {
                    var hasConfig = Configuration_Management.Services.OneCLaunchConfigReader.FindConfigFile() != null;
                    LaunchConfigHintText.Text = hasConfig
                        ? LocalizationManager.T("FunctionalMode.LaunchConfigPresent")
                        : LocalizationManager.T("FunctionalMode.LaunchConfigAbsent");
                    if (Configuration_Management.Services.PortablePaths.IsPortable)
                        LaunchConfigHintText.Text += " " + LocalizationManager.T("FunctionalMode.Portable");
                }
            }
            catch
            {
                // Инициализация подсказок не должна ломать открытие окна настроек.
            }
        }

        private void OnSave_Click(object sender, RoutedEventArgs e)
        {
            // Валидация времени расписания синхронизации: обычный TimeSpan.TryParse
            // принимал «9» как девять суток и «25:00» как длительность, из-за чего
            // расписание молча не срабатывало (issue #207). Не сохраняем заведомо
            // неверное значение и показываем пример правильного.
            if (SyncTriggerComboBox.SelectedIndex == (int)IbasesSyncTrigger.Schedule)
            {
                var schedule = SyncScheduleTimePicker.Text?.Trim() ?? string.Empty;
                if (!IsValidScheduleTime(schedule))
                {
                    _dialogs.ShowWarning(LocalizationManager.T("Settings.Ibases.ScheduleTimeInvalid"),
                        LocalizationManager.T("Settings.Ibases.ScheduleTime"));
                    return;
                }
            }

            // Валидация шаблона даты-времени для имени файла выгрузки (issue #207):
            // не сохраняем неверный шаблон, чтобы следующая выгрузка .dt/.cf не падала.
            if (AddTimestampToExportFileNameCheck.IsChecked == true &&
                !IsValidTimestampFormat(ExportTimestampFormatComboBox?.Text))
            {
                _dialogs.ShowWarning(LocalizationManager.T("Settings.TimestampInvalid"),
                    LocalizationManager.T("Settings.Bases.TimestampFormat"));
                return;
            }

            // Сохраняем версии платформы и дополнительные пути поиска.
            _viewModel.SetAdditionalPlatformSearchPaths(_additionalPlatformPaths);
            _viewModel.SetInstalledPlatformVersions(_installedPlatformVersions);

            // Режим «Разрядности по умолчанию» (Настройки → Платформы).
            _viewModel.ApplyDefaultArchitecture(DefaultArchComboBox.SelectedIndex switch
            {
                1 => "X86",
                2 => "Priority",
                _ => "X64"
            });

            // Режим функциональности (Этап 10 StartManager): «Пользователь»/«Специалист»/«Разработчик».
            if (FunctionalModeComboBox?.SelectedItem is ComboBoxItem fmItem &&
                fmItem.Tag is string fmCode &&
                !string.IsNullOrEmpty(fmCode))
            {
                _viewModel.FunctionalMode = fmCode;
            }

            // Сохраняем настройки синхронизации с файлом ibases.v8i.
            var s = _settings.Sync;
            var filePath = SyncFilePathTextBox.Text?.Trim() ?? string.Empty;
            var interval = SettingsViewModel.IbasesSyncSettings.ParseInterval(SyncIntervalTextBox.Text);
            var scheduleTime = SyncScheduleTimePicker.Text?.Trim() ?? string.Empty;
            _viewModel.ApplyIbasesSyncSettings(s.Mode, filePath, s.Trigger, interval, scheduleTime,
                IbasesBackupEnabledCheck.IsChecked ?? true,
                int.TryParse(IbasesBackupKeepCountBox.Text, out var keep) && keep > 0 ? keep : 5,
                IbasesSaveAfterEditCheck?.IsChecked ?? true);

            // Сохраняем настройки резервного копирования профиля.
            _viewModel.ApplyProfileBackupSettings(_profileDirBox.Text, _profileRestoreCheck.IsChecked == true);

            // Сохраняем настройки отображения списка баз.
            // Видимость колонок читается из тех же элементов списка, где задаётся
            // и порядок: флажок каждой строки и есть её видимость.
            bool VisibleOf(string key) => _columnOrderItems.FirstOrDefault(i => i.Key == key)?.Visible ?? true;

            _viewModel.ApplyDisplaySettings(
                ShowFavoritesButtonCheck.IsChecked ?? false,
                ShowPinnedButtonCheck.IsChecked ?? false,
                ShowTagsCheck.IsChecked ?? false,
                VisibleOf("Version"),
                VisibleOf("LaunchMode"),
                VisibleOf("ServerBase"),
                VisibleOf("LastLaunch"),
                GroupByGroupCheck.IsChecked ?? true,
                ShowFavoritesOnlyCheck.IsChecked ?? false,
                VisibleOf("Size"),
                VisibleOf("Modified"),
                VisibleOf("Configuration"),
                VisibleOf("ConfigurationVersion"),
                ShowEmptyGroupsCheck?.IsChecked ?? false,
                _columnOrderItems.Select(i => i.Key).ToList(),
                VisibleOf("Actions"));

            _viewModel.ShowRightPanelDetails = ShowRightPanelDetailsCheck?.IsChecked ?? true;
            _viewModel.ShowSessionLaunchPanel = ShowSessionLaunchPanelCheck?.IsChecked ?? true;
            _viewModel.ApplyStatusBarSettings(
                StatusShowConnectionPathCheck?.IsChecked ?? true,
                StatusShowArchitectureCheck?.IsChecked ?? true,
                StatusShowLaunchModeCheck?.IsChecked ?? true,
                StatusShowPortCheck?.IsChecked ?? true,
                StatusShowPlatformVersionCheck?.IsChecked ?? true,
                StatusShowClientTypeCheck?.IsChecked ?? false,
                StatusShowConnectionTypeCheck?.IsChecked ?? false,
                StatusShowUserCheck?.IsChecked ?? false,
                StatusShowIdCheck?.IsChecked ?? false);
            var hkEnterprise = ReadHotkeyBox(HotkeyEnterpriseBox);
            var hkConfigurator = ReadHotkeyBox(HotkeyConfiguratorBox);
            var hkFavorite = ReadHotkeyBox(HotkeyFavoriteBox);
            var hkEdit = ReadHotkeyBox(HotkeyEditBox);
            var hkDelete = ReadHotkeyBox(HotkeyDeleteBox);
            var hkClearCache = ReadHotkeyBox(HotkeyClearCacheBox);
            var hkAdd = ReadHotkeyBox(HotkeyAddBox);
            var hkPin = ReadHotkeyBox(HotkeyPinBox);
            var hkShowAll = ReadHotkeyBox(HotkeyShowAllBox);
            var hkShowFavorites = ReadHotkeyBox(HotkeyShowFavoritesBox);
            var hkShowRecent = ReadHotkeyBox(HotkeyShowRecentBox);
            var hkClearSearch = ReadHotkeyBox(HotkeyClearSearchBox);
            var hkClearTags = ReadHotkeyBox(HotkeyClearTagsBox);
            var hkRightPanelDetails = ReadHotkeyBox(HotkeyRightPanelDetailsBox);
            var hkSwitchUser = ReadHotkeyBox(HotkeySwitchUserBox);
            var hkSessionLock = ReadHotkeyBox(HotkeySessionLockBox);
            var hkLockApp = ReadHotkeyBox(HotkeyLockAppBox);
            var hkCheckIntegrity = ReadHotkeyBox(HotkeyCheckIntegrityBox);
            var hkServerConsole = ReadHotkeyBox(HotkeyServerConsoleBox);
            // Копия экрана по хоткею (функция №30, Этап 8).
            var hkScreenshot = ReadHotkeyBox(HotkeyScreenshotBox);

            // Проверка: одна клавиша — одно действие (пустые «Нет» не учитываются).
            var assigned = new (string Name, string Key)[]
            {
                (LocalizationManager.T("Main.Enterprise"), hkEnterprise),
                (LocalizationManager.T("Main.SectionConfigurator"), hkConfigurator),
                (LocalizationManager.T("Main.Favorites"), hkFavorite),
                (LocalizationManager.T("Main.EditShort"), hkEdit),
                (LocalizationManager.T("Common.Delete"), hkDelete),
                (LocalizationManager.T("Main.ClearCache"), hkClearCache),
                (LocalizationManager.T("Main.AddBase"), hkAdd),
                (LocalizationManager.T("Main.Pin"), hkPin),
                (LocalizationManager.T("Main.AllBasesTooltip"), hkShowAll),
                (LocalizationManager.T("Main.FavoritesTooltip"), hkShowFavorites),
                (LocalizationManager.T("Main.RecentTooltip"), hkShowRecent),
                (LocalizationManager.T("Main.ClearSearch"), hkClearSearch),
                (LocalizationManager.T("Main.ClearTags"), hkClearTags),
                (LocalizationManager.T("Main.CollapseRightPanel"), hkRightPanelDetails),
                (LocalizationManager.T("Main.SwitchUser"), hkSwitchUser),
                (LocalizationManager.T("SessionLock.Title"), hkSessionLock),
                (LocalizationManager.T("AppLock.LockTitle"), hkLockApp),
                (LocalizationManager.T("Admin.CheckIntegrityTitle"), hkCheckIntegrity),
                (LocalizationManager.T("Admin.ServerConsoleTitle"), hkServerConsole),
                (LocalizationManager.T("Settings.Screenshot.Title"), hkScreenshot)
            };
            var duplicates = SettingsViewModel.FindDuplicateHotkeys(assigned).ToList();
            if (duplicates.Count > 0)
            {
                var msg = string.Join("\n", duplicates.Select(g =>
                    string.Format(LocalizationManager.T("Settings.Hotkeys.AssignedTo"), g.Key,
                        string.Join(", ", g.Select(x => x.Name)))));
                _dialogs.ShowWarning(
                    string.Format(LocalizationManager.T("Settings.Hotkeys.DuplicateMsg"), msg),
                    LocalizationManager.T("Settings.Hotkeys.DuplicateTitle"));
                return;
            }

            // Имя COM-коннектора 1С по шаблону версии платформы (issue #175).
            _viewModel.ComConnectorNameTemplate = ComConnectorNameTemplateBox.Text?.Trim() ?? "";
            // Таймаут определения свойств конфигурации через COM (issue #174).
            if (ComDetectTimeoutMsBox != null
                && int.TryParse(ComDetectTimeoutMsBox.Text, out var detectTimeout))
                _viewModel.ComDetectTimeoutMs = detectTimeout;
            // Глубина истории запусков одной базы (issue #246).
            if (MaxLaunchHistoryDepthBox != null
                && int.TryParse(MaxLaunchHistoryDepthBox.Text, out var historyDepth))
                _viewModel.MaxLaunchHistoryPerBase = historyDepth;

            _viewModel.ApplyAppBehaviorSettings(
                AllowMultipleInstancesCheck.IsChecked ?? false,
                CheckForUpdatesOnStartupCheck?.IsChecked ?? true,
                AutoUpdateEnabledCheck?.IsChecked ?? true,
                ShowTagFilterPanelCheck.IsChecked ?? true,
                CloseToTrayCheck.IsChecked ?? false,
                ShowTrayIconCheck.IsChecked ?? true,
                hkEnterprise,
                hkConfigurator,
                hkFavorite,
                hkEdit,
                hkDelete,
                hkClearCache,
                hkAdd,
                hkPin,
                EscapeToTrayCheck.IsChecked ?? true,
                hkShowAll,
                hkShowFavorites,
                hkShowRecent,
                RememberWindowLayoutCheck.IsChecked ?? true,
                ReadAfterLaunchAction(),
                hotkeyClearSearch: hkClearSearch,
                hotkeyClearTags: hkClearTags,
                hotkeyRightPanelDetails: hkRightPanelDetails,
                hotkeySwitchUser: hkSwitchUser,
                hotkeySessionLock: hkSessionLock,
                hotkeyLockApp: hkLockApp,
                hotkeyCheckIntegrity: hkCheckIntegrity,
                hotkeyServerConsole: hkServerConsole);

            // Копия экрана (функция №30, Этап 8): сочетание и каталог сохранения.
            _viewModel.ScreenshotHotkey = hkScreenshot;
            _viewModel.ScreenshotSaveDirectory = ScreenshotDirectoryBox?.Text?.Trim() ?? "";
            // Авторизация на сайте 1С при проверке обновлений конфигураций.
            if (UpdatesLoginBox != null)
                _viewModel.UpdatesLogin = UpdatesLoginBox.Text?.Trim() ?? "";
            if (UpdatesPasswordBox != null)
                _viewModel.UpdatesPassword = UpdatesPasswordBox.Password;
            _viewModel.SaveSettings();

            var templatePaths = TemplatePathsList?.Items.Cast<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                ?? new System.Collections.Generic.List<string>();
            _viewModel.SetTemplateCatalogPaths(templatePaths);

            // Добавление даты-времени к имени файла при выгрузке (JSON, .dt, .cf).
            _viewModel.ApplyExportFileNameSettings(AddTimestampToExportFileNameCheck?.IsChecked ?? true);

            // Шаблон (формат) отметки даты и времени для имени файла при выгрузке.
            _viewModel.ApplyExportTimestampFormat(ExportTimestampFormatComboBox?.Text ?? "yyyyMMdd_HHmmss");


            // Порядок горячих клавиш избранного.
            _viewModel.SetFavoriteHotkeyOrder(_favoriteHotkeyItems.Select(i => i.Key));

            // Сохраняем все темы, изменённые во вкладке «Цветовое оформление»: каждая тема
            // хранит собственные настройки независимо (встроенные — в своём слоте базовой
            // темы, пользовательские — в своём JSON-файле).
            _settings.PersistEditedSchemes();
            ThemeDebug($"Settings OK: applying '{_settings.CurrentColorScheme.Name}' (colors light={_settings.CurrentColorScheme.LightColors.Count}, dark={_settings.CurrentColorScheme.DarkColors.Count})");
            _viewModel.ApplyColorScheme(_settings.CurrentColorScheme);

            // Сохраняем настройки шрифта интерфейса (общий и отдельных областей).
            ReadFontSelection();
            _viewModel.SaveElementFonts(_settings.ElementFonts);

            // Применяем выбранный язык интерфейса только при сохранении (issue #206):
            // «Отмена» не должна менять язык и перезаписывать settings.json.
            if (!string.IsNullOrEmpty(_pendingLanguageCode) &&
                !string.Equals(_pendingLanguageCode, LocalizationManager.Instance.CurrentLanguage,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                _viewModel.ApplyLanguage(_pendingLanguageCode);
                // Перестраиваем список тем: отображаемые подписи встроенных тем
                // локализованы и должны обновиться при смене языка. Сохранённое имя
                // (канонический ключ «Светлая»/«Тёмная») не меняется.
                RefreshSchemeComboBox();
            }

            DialogResult = true;
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>Заполняет комбобокс «Действие по двойному щелчку» глобальной настройкой (функция №28 StartManager).</summary>
        private void InitGlobalDoubleClickCombo()
        {
            if (DoubleClickActionGlobalCombo is null || _viewModel is null) return;
            DoubleClickActionGlobalCombo.ItemsSource = new[]
            {
                LocalizationManager.T("Connection.DefaultLaunchEnterprise"),
                LocalizationManager.T("Connection.DefaultLaunchConfigurator"),
                LocalizationManager.T("Connection.DblClickNone")
            };
            DoubleClickActionGlobalCombo.SelectedIndex = _viewModel.DefaultDoubleClickAction switch
            {
                Configuration_Management.Models.DoubleClickAction.Configurator => 1,
                Configuration_Management.Models.DoubleClickAction.None => 2,
                _ => 0
            };
        }

        /// <summary>Обработчик смены глобального «действия по двойному щелчку» (функция №28).</summary>
        private void OnDoubleClickActionGlobal_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel is null || sender is not ComboBox combo)
                return;
            _viewModel.SetDefaultDoubleClickAction(combo.SelectedIndex switch
            {
                1 => Configuration_Management.Models.DoubleClickAction.Configurator,
                2 => Configuration_Management.Models.DoubleClickAction.None,
                _ => Configuration_Management.Models.DoubleClickAction.Enterprise
            });
        }

        /// <summary>Элемент списка тем.</summary>
        private sealed class SchemeComboItem
        {
            public string Name { get; set; } = string.Empty;
            public string? DisplayName { get; set; }
            public bool IsBuiltIn { get; set; }
            public override string ToString() => string.IsNullOrEmpty(DisplayName) ? Name : DisplayName;
        }

        /// <summary>Элемент редактора цветов: подпись, ключ, HEX и кисть-образец.</summary>
        private sealed class ColorItem : INotifyPropertyChanged
        {
            public string Key { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;

            private string _hex = "#000000";
            public string Hex
            {
                get => _hex;
                set
                {
                    _hex = value;
                    OnPropertyChanged(nameof(Hex));
                    ColorBrush = ParseBrush(value);
                    OnPropertyChanged(nameof(ColorBrush));
                }
            }

            private SolidColorBrush _colorBrush = new(Colors.Black);
            public SolidColorBrush ColorBrush
            {
                get => _colorBrush;
                private set { _colorBrush = value; OnPropertyChanged(nameof(ColorBrush)); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            private static SolidColorBrush ParseBrush(string hex)
            {
                try
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                }
                catch
                {
                    return new SolidColorBrush(Colors.Transparent);
                }
            }
        }

        /// <summary>Элемент списка колонок: хранит ключ, имя и флаг видимости.
        /// Один элемент объединяет порядок и видимость колонки — оба редактируются
        /// в одном списке на вкладке «Отображение».</summary>
        private sealed class ColumnOrderItem
        {
            public string Key { get; init; } = string.Empty;
            public string Display { get; init; } = string.Empty;
            public bool Visible { get; set; } = true;
            public MaterialDesignThemes.Wpf.PackIconKind IconKind { get; init; }

            public override string ToString() => Display;
        }

        /// <summary>Элемент списка областей интерфейса для выбора шрифта.</summary>
        private sealed class ElementScopeItem
        {
            public string Key { get; }
            public ElementScopeItem(string key) { Key = key; }
            public override string ToString() => Themes.ThemeManager.FontScopeDisplayName(Key);
        }

        /// <summary>Доступные начертания шрифта. Технические Weight/Style не локализуются.</summary>
        private static readonly FontFaceItem[] FontFaces =
        {
            new() { Key = "Settings.Font.StyleNormal", Weight = "Normal", Style = "Normal" },
            new() { Key = "Settings.Font.StyleBold", Weight = "Bold", Style = "Normal" },
            new() { Key = "Settings.Font.StyleItalic", Weight = "Normal", Style = "Italic" },
            new() { Key = "Settings.Font.StyleBoldItalic", Weight = "Bold", Style = "Italic" }
        };

        /// <summary>
        /// Компаратор для сортировки версий по убыванию.
        /// Учитывает суффикс разрядности «(32)» / «(64)»: в пределах одной версии
        /// 64-битный вариант считается более новым.
        /// </summary>
        private sealed class VersionComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (x == y) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                var result = CompareCore(x, y);
                if (result != 0)
                    return result;

                // Версии совпадают — сравниваем разрядность (64 > 32).
                return GetArch(x).CompareTo(GetArch(y));
            }

            private static int CompareCore(string x, string y)
            {
                PlatformVersionService.ParseVariant(x, out var xv, out _);
                PlatformVersionService.ParseVariant(y, out var yv, out _);

                var xParts = xv.Split('.').Select(int.Parse).ToArray();
                var yParts = yv.Split('.').Select(int.Parse).ToArray();

                var length = Math.Max(xParts.Length, yParts.Length);
                for (var i = 0; i < length; i++)
                {
                    var xVal = i < xParts.Length ? xParts[i] : 0;
                    var yVal = i < yParts.Length ? yParts[i] : 0;
                    if (xVal != yVal)
                        return xVal.CompareTo(yVal);
                }

                return 0;
            }

            private static int GetArch(string variant)
            {
                PlatformVersionService.ParseVariant(variant, out _, out var architecture);
                return architecture == "64" ? 1 : 0;
            }
        }

        /// <summary>
        /// Preview-обработчик ESC в окне настроек (issue #270): первый ESC закрывает открытые
        /// всплывающие подсказки, а не всё окно целиком. Если подсказка была закрыта — событие
        /// помечается обработанным, и на этом же ESC кнопка «Отмена» (IsCancel) окно не закроет;
        /// повторный ESC уже закрывает окно как обычно. Инвариант «сначала подсказка, потом окно»
        /// тот же, что у главного окна (issue #261).
        /// </summary>
        private void OnSettingsPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape &&
                Keyboard.Modifiers == ModifierKeys.None &&
                CloseOpenToolTips())
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Закрывает все открытые всплывающие подсказки (ToolTip) окна настроек (issue #270).
        /// Возвращает true, если была закрыта хотя бы одна подсказка. Основной путь — закрытие
        /// через владельцев, записанных класс-обработчиками <see cref="ToolTip.OpenedEvent"/>/
        /// <see cref="ToolTip.ClosedEvent"/>, поэтому не зависит от того, где физически отрисован
        /// попап (в визуальном дереве окна или во внешнем HWND/Popup). Резервные пути — обход
        /// визуального дерева окна и цепочка визуальных родителей элемента под курсором/в фокусе.
        /// Используется и по ESC, и при потере фокуса окна (issue #275).
        /// </summary>
        private bool CloseOpenToolTips()
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

            // Резервный путь: обход визуального дерева окна (для подсказок, не попавших
            // в _openToolTips).
            closed = CloseToolTipsIn(this);

            // Попап открытой подсказки размещается вне визуального дерева окна (отдельный
            // HWND/Popup), поэтому до «хозяина» добираемся и по курсору/фокусу.
            closed |= CloseToolTipByMouseOrFocus();

            return closed;
        }

        /// <summary>Закрывает все открытые <see cref="System.Windows.Controls.ToolTip"/> в поддереве.</summary>
        private bool CloseToolTipsIn(DependencyObject root)
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
        private bool CloseToolTipByMouseOrFocus()
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

        /// <summary>
        /// Глобальный обработчик изменения <see cref="ToolTip.IsOpenProperty"/> (issue #270):
        /// ведёт <see cref="_openToolTips"/> по фактическому состоянию свойства, покрывая
        /// подсказки, не попавшие в <see cref="OnToolTipOpened"/> (например, строковые
        /// ToolTip="..."), и гарантирует, что первый ESC закрывает тултип, а не всё окно.
        /// </summary>
        private void OnToolTipIsOpenGlobalChanged(object? sender, EventArgs e)
        {
            if (sender is System.Windows.Controls.ToolTip tip)
            {
                var owner = tip.PlacementTarget ?? tip;
                if (tip.IsOpen)
                    _openToolTips.Add(owner);
                else
                    _openToolTips.Remove(owner);
            }
        }

        /// <summary>Класс-обработчик открытия <see cref="ToolTip"/> (issue #270): запоминает владельца.</summary>
        private void OnToolTipOpened(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ToolTip tip)
            {
                var owner = tip.PlacementTarget ?? tip;
                _openToolTips.Add(owner);
            }
        }

        /// <summary>Класс-обработчик закрытия <see cref="ToolTip"/> (issue #270): убирает владельца.</summary>
        private void OnToolTipClosed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ToolTip tip)
            {
                var owner = tip.PlacementTarget ?? tip;
                _openToolTips.Remove(owner);
            }
        }

        /// <summary>
        /// Класс-обработчик ESC на типе <see cref="ToolTip"/> (issue #270): когда клавиша приходится
        /// на открытую подсказку (фокус во внешнем попапе/HWND), закрывает подсказку и помечает
        /// событие обработанным, чтобы первый ESC не закрыл всё окно настроек.
        /// </summary>
        private void OnToolTipPreviewKeyDown(object sender, KeyEventArgs e)
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
        /// Класс-обработчик вето на повторное открытие подсказки (issue #270). Вызывается до показа
        /// тултипа владельца: если владелец подавлен после ESC и указатель всё ещё над ним (или не
        /// истёк короткий интервал) — показ отменяется. Как только курсор ушёл и интервал истёк —
        /// подавление снимается.
        /// </summary>
        private void OnToolTipOpening(object sender, ToolTipEventArgs e)
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
        private void SuppressToolTipOwner(DependencyObject owner)
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