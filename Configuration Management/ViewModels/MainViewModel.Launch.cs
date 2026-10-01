#if WINDOWS
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Main ViewModel (partial class split by feature blocks, see MainViewModel.*.cs).</summary>
public partial class MainViewModel : ViewModelBase
{
    private void ScheduleSave()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _saveDebounceCts = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SaveDebounceMs, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                    return;

                // Снимок коллекции на UI-потоке, запись файла — в фоне.
                List<Infobase> snapshot = Application.Current?.Dispatcher is { } dispatcher
                    ? await dispatcher.InvokeAsync(() => Infobases.ToList())
                    : Infobases.ToList();

                if (token.IsCancellationRequested)
                    return;

                await _repository.SaveAsync(snapshot, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Новый клик отменил предыдущее сохранение — нормально.
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка отложенного сохранения баз", ex);
                try
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                        _dialogs.ShowError(
                            string.Format(LocalizationManager.T("Main.ErrSaveBases"), ex.Message),
                            LocalizationManager.T("Main.ErrSaveBasesTitle")));
                }
                catch
                {
                    // ignore secondary UI failures
                }
            }
        }, token);
    }

    private void LaunchEnterpriseWithParams(object? parameter)
    {
        if (SelectedInfobase is null) return;
        var dlg = new Configuration_Management.LaunchParametersWindow(
            SelectedInfobase.LaunchParameters ?? "",
            CustomLaunchParameters,
            SetCustomLaunchParameters)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true) return;
        var ib = SelectedInfobase;
        var saved = ib.LaunchParameters ?? "";
        // Разовые параметры действуют только на этот запуск: pre-команда может
        // выполняться до 30 секунд, поэтому подменённое значение держим в ядре
        // и возвращаем в finally после фактического запуска 1С.
        _ = LaunchWithParamsCoreAsync(ib, saved, dlg.Result ?? "", OneCLaunchMode.Enterprise);
    }

    private void LaunchConfiguratorWithParams(object? parameter)
    {
        if (SelectedInfobase is null) return;
        var dlg = new Configuration_Management.LaunchParametersWindow(
            SelectedInfobase.LaunchParameters ?? "",
            CustomLaunchParameters,
            SetCustomLaunchParameters)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true) return;
        var ib = SelectedInfobase;
        var saved = ib.LaunchParameters ?? "";
        _ = LaunchWithParamsCoreAsync(ib, saved, dlg.Result ?? "", OneCLaunchMode.Configurator);
    }

    /// <summary>
    /// Ядро запуска с разовыми параметрами: подмена параметров → pre-команда →
    /// запуск → post-команда; исходные параметры возвращаются всегда.
    /// </summary>
    private async Task LaunchWithParamsCoreAsync(Infobase ib, string savedParameters, string newParameters, OneCLaunchMode mode)
    {
        try
        {
            ib.LaunchParameters = newParameters;
            var ok = await RunScriptedLaunchAsync(ib, () => _launcher.Launch(ib, mode));
            if (ok)
            {
                ib.LastLaunchDate = DateTime.Now;
                Save();
            }
            else
            {
                ShowLaunchFailed();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка запуска базы «{ib.Name}» с параметрами", ex);
            ShowLaunchFailed();
        }
        finally
        {
            ib.LaunchParameters = savedParameters;
        }
    }

    private void LaunchEnterpriseWithAuth(object? parameter)
    {
        if (SelectedInfobase is null) return;
        var ib = SelectedInfobase;
        var conn = ib.Connection;
        var savedUser = conn.User;
        var savedPwd = conn.Password;
        var savedAuth = conn.AuthenticationMode;
        // Учётные данные очищаются на время запуска (чтобы платформа спросила их сама):
        // pre-команда выполняется до фактического запуска, восстановление — в finally.
        _ = LaunchWithAuthCoreAsync(ib, conn, savedUser, savedPwd, savedAuth);
    }

    /// <summary>
    /// Ядро запуска с запросом авторизации: очистка учётных данных → pre-команда →
    /// запуск → post-команда; прежние значения восстанавливаются всегда.
    /// </summary>
    private async Task LaunchWithAuthCoreAsync(
        Infobase ib, ConnectionSettings conn, string savedUser, string savedPwd, AuthenticationMode savedAuth)
    {
        try
        {
            conn.User = string.Empty;
            conn.Password = string.Empty;
            conn.AuthenticationMode = AuthenticationMode.Prompt;
            var ok = await RunScriptedLaunchAsync(ib, () => _launcher.Launch(ib, OneCLaunchMode.Enterprise));
            if (ok)
            {
                ib.LastLaunchDate = DateTime.Now;
                Save();
            }
            else
            {
                ShowLaunchFailed();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка запуска базы «{ib.Name}» с запросом авторизации", ex);
            ShowLaunchFailed();
        }
        finally
        {
            conn.User = savedUser;
            conn.Password = savedPwd;
            conn.AuthenticationMode = savedAuth;
        }
    }

    /// <summary>
    /// Обёртка запуска с пользовательскими скриптами (функция №8, 0.3.9.98):
    /// pre-команда (ожидание с таймаутом) → действие запуска → post-команда
    /// (fire-and-forget при успехе). Возвращает результат действия запуска.
    /// </summary>
    private async Task<bool> RunScriptedLaunchAsync(Infobase ib, Func<bool> launchAction)
    {
        await RunPreLaunchScriptAsync(ib);
        var ok = launchAction();
        if (ok)
            RunPostLaunchScript(ib);
        return ok;
    }

    private void LaunchNativeStarter()
    {
        if (!InfobaseMaintenanceService.OpenNativeStarter())
            _dialogs.ShowError(LocalizationManager.T("Main.ErrStartStarter"));
    }

    /// <summary>
    /// Единая точка запуска 1С. parameter — LaunchKind, строка имени enum или null (Enterprise).
    /// Для Enterprise учитываются переопределения «Текущая сессия» (клиент и разрядность).
    /// Перед запуском выполняется пользовательская pre-команда (функция №8, 0.3.9.98),
    /// после успешного старта — post-команда без ожидания.
    /// </summary>
    private void Launch(object? parameter, bool runAsAdmin = false, Infobase? target = null)
    {
        var ib = target ?? SelectedInfobase;
        if (ib is null)
            return;

        // Асинхронное ядро: pre-команда может ждать завершения до 30 секунд,
        // поэтому блокировать UI-поток нельзя — запуск продолжается после неё.
        _ = LaunchCoreAsync(ib, parameter, runAsAdmin);
    }

    /// <summary>
    /// Асинхронное ядро единой точки запуска: pre-команда → запуск 1С → post-команда.
    /// </summary>
    private async Task LaunchCoreAsync(Infobase ib, object? parameter, bool runAsAdmin)
    {
        try
        {
            var kind = ResolveLaunchKind(parameter);
            await RunPreLaunchScriptAsync(ib);

            bool ok;
            switch (kind)
            {
                case LaunchKind.Configurator:
                    ok = _launcher.Launch(ib, OneCLaunchMode.Configurator, runAsAdmin);
                    break;
                case LaunchKind.Thin32:
                    ok = _launcher.Launch(ib, OneCLaunchMode.Enterprise, OneCClientType.Thin, OneCArchitecture.x86, runAsAdmin);
                    break;
                case LaunchKind.Thick32:
                    ok = _launcher.Launch(ib, OneCLaunchMode.Enterprise, OneCClientType.Thick, OneCArchitecture.x86, runAsAdmin);
                    break;
                case LaunchKind.Thin64:
                    ok = _launcher.Launch(ib, OneCLaunchMode.Enterprise, OneCClientType.Thin, OneCArchitecture.x64, runAsAdmin);
                    break;
                case LaunchKind.Thick64:
                    ok = _launcher.Launch(ib, OneCLaunchMode.Enterprise, OneCClientType.Thick, OneCArchitecture.x64, runAsAdmin);
                    break;
                default:
                    ok = LaunchEnterpriseWithSessionOverrides(ib, runAsAdmin);
                    break;
            }

            if (ok)
            {
                RunPostLaunchScript(ib);
                var sessionDetails = string.Format(
                    LocalizationManager.T("Main.LaunchHistorySessionDetails"),
                    _sessionClientMode, _sessionArchitecture);
                ib.AddLaunchHistory(kind.ToString(), BuildLaunchDetails(sessionDetails, ib));
                InfobasesView.Refresh();
                Save();
                _logger.Info($"Запущена база «{ib.Name}» ({kind}, клиент={_sessionClientMode}, арх={_sessionArchitecture})");
                NotifyAfterLaunch();
            }
            else
            {
                _logger.Warn($"Не удалось запустить базу «{ib.Name}» ({kind})");
                ShowLaunchFailed();
            }
        }
        catch (Exception ex)
        {
            // Скрипты не должны ронять запуск базы: любые ошибки логируем и продолжаем.
            _logger.Error($"Ошибка при запуске базы «{ib.Name}» ({ResolveLaunchKind(parameter)})", ex);
            ShowLaunchFailed();
        }
    }

    /// <summary>
    /// Выполняет пользовательскую команду «перед запуском» (функция №8, 0.3.9.98):
    /// ожидание завершения с таймаутом 30 секунд. При ошибке/таймауте предупреждает
    /// пользователя, НО не блокирует запуск базы — он продолжается.
    /// </summary>
    private async Task RunPreLaunchScriptAsync(Infobase ib)
    {
        if (string.IsNullOrWhiteSpace(ib.PreLaunchCommand))
            return;

        var ok = await ExternalCommandRunner.RunAsync(
            ib.PreLaunchCommand, ExternalCommandRunner.DefaultPreCommandTimeoutMs).ConfigureAwait(true);

        if (ok)
        {
            _logger.Info($"Pre-команда базы «{ib.Name}» выполнена: {ib.PreLaunchCommand}");
        }
        else
        {
            _logger.Warn($"Pre-команда базы «{ib.Name}» завершилась с ошибкой или таймаутом: {ib.PreLaunchCommand}");
            _dialogs.ShowWarning(
                string.Format(LocalizationManager.T("Launch.PreCommandFailed"), ib.PreLaunchCommand),
                LocalizationManager.T("Launch.CommandsTitle"));
        }
    }

    /// <summary>
    /// Запускает пользовательскую команду «после запуска» (функция №8, 0.3.9.98)
    /// без ожидания завершения (fire-and-forget).
    /// </summary>
    private void RunPostLaunchScript(Infobase ib)
    {
        if (string.IsNullOrWhiteSpace(ib.PostLaunchCommand))
            return;

        ExternalCommandRunner.RunDetached(ib.PostLaunchCommand);
        _logger.Info($"Запущена post-команда базы «{ib.Name}»: {ib.PostLaunchCommand}");
    }

    /// <summary>
    /// Детали истории запуска с маркером пользовательских команд (0.3.9.98):
    /// например «pre: ras connect …; post: start …». Пустые части пропускаются.
    /// </summary>
    private static string BuildLaunchDetails(string baseDetails, Infobase ib)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseDetails))
            parts.Add(baseDetails);
        if (!string.IsNullOrWhiteSpace(ib.PreLaunchCommand))
            parts.Add("pre: " + ib.PreLaunchCommand);
        if (!string.IsNullOrWhiteSpace(ib.PostLaunchCommand))
            parts.Add("post: " + ib.PostLaunchCommand);
        return string.Join("; ", parts);
    }

    /// <summary>Сообщение пользователю о неудачном запуске (детальная причина — в логе сервиса).</summary>
    private void ShowLaunchFailed()
    {
        _dialogs.ShowError(
            LocalizationManager.T("Main.OperationFailedDefault"),
            LocalizationManager.T("Launcher.LaunchErrorTitle"));
    }

    /// <summary>
    /// Запуск 1С:Предприятие с учётом переключателей «Текущая сессия».
    /// </summary>
    private bool LaunchEnterpriseWithSessionOverrides(Infobase ib, bool runAsAdmin = false)
    {
        // Полностью «Авто» — стандартная логика по настройкам базы.
        if (_sessionClientMode == SessionClientMode.Auto &&
            _sessionArchitecture == SessionArchitectureMode.Auto)
        {
            return _launcher.Launch(ib, OneCLaunchMode.Enterprise, runAsAdmin);
        }

        OneCClientType? client = _sessionClientMode switch
        {
            SessionClientMode.Thin => OneCClientType.Thin,
            SessionClientMode.Thick => OneCClientType.Thick,
            // «Обычный режим» и «Толстый (обычные формы)» объединены в один пункт
            // (issue #144): толстый клиент в обычных формах.
            SessionClientMode.Ordinary => OneCClientType.Thick,
            _ => ResolveClientFromInfobase(ib)
        };

        // Разрядность полностью определяет лаунчер по приоритету (issue #146):
        // 1) «Текущая сессия» (передана через OneCLauncher.SessionArchitecture),
        // 2) суффикс версии, 3) глобальная настройка, 4) настройка базы / priority.
        var arch = OneCLauncher.ResolveArchitecture(ib.Architecture, ib.PlatformVersion);

        // Режим форм: «Толстый (управляемые формы)» и «Обычный режим» задают его явно;
        // в остальных случаях берём из настройки базы при автоматическом клиенте.
        // «Обычный режим» соответствует бывшему «Толстый (обычные формы)» (issue #144).
        OneCRunMode? runMode = _sessionClientMode switch
        {
            SessionClientMode.Thick => OneCRunMode.Managed,
            SessionClientMode.Ordinary => OneCRunMode.Ordinary,
            SessionClientMode.Auto => OneCLauncher.GetRunModeFromLaunchMode(ib.LaunchMode),
            _ => null
        };

        return _launcher.Launch(ib, OneCLaunchMode.Enterprise, client, runMode, arch, runAsAdmin);
    }

    /// <summary>Тип клиента из настройки базы (LaunchMode).</summary>
    private static OneCClientType? ResolveClientFromInfobase(Infobase ib)
    {
        if (string.Equals(ib.LaunchMode, "Автоматический", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.Equals(ib.LaunchMode, "Толстый клиент (обычные формы)", StringComparison.OrdinalIgnoreCase))
            return OneCClientType.Thick;
        if (string.Equals(ib.LaunchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
            return OneCClientType.Thick;
        if (string.Equals(ib.LaunchMode, "Тонкий клиент", StringComparison.OrdinalIgnoreCase))
            return OneCClientType.Thin;
        // Веб и прочее — без принудительного /RunMode
        return null;
    }

    private static LaunchKind ResolveLaunchKind(object? parameter) => parameter switch
    {
        LaunchKind k => k,
        string s when Enum.TryParse<LaunchKind>(s, true, out var parsed) => parsed,
        _ => LaunchKind.Enterprise
    };

    private bool FilterInfobase(object item)
    {
        if (item is not Infobase infobase)
            return false;

        if (_listViewMode == ListViewMode.Favorites && !infobase.IsFavorite)
            return false;
        if (_listViewMode == ListViewMode.Recent && !infobase.LastLaunchDate.HasValue)
            return false;

        if (_activeTagFilterSet.Count > 0
            && !_activeTagFilterSet.All(t =>
                infobase.Tags.Any(bt => string.Equals(bt, t, StringComparison.OrdinalIgnoreCase))))
            return false;

        var filter = SearchText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(filter))
            return true;

        return infobase.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || (infobase.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || (infobase.Group?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || (infobase.PlatformVersion?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || (infobase.ServerDatabaseDisplay?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || (infobase.ConnectionStringDisplay?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || infobase.Tags.Any(t => t.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Сохранение списка баз после точечного изменения (версия платформы и т.п.).</summary>
    public void PersistInfobasesAfterInlineEdit()
    {
        try
        {
            Save();
            InfobasesView?.Refresh();
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка сохранения после правки базы", ex);
            _dialogs.ShowError(
                string.Format(LocalizationManager.T("Main.ErrSaveChange"), ex.Message),
                LocalizationManager.T("Main.ErrSaveBasesTitle"));
        }
    }

    private void Save()
    {
        try
        {
            _repository.Save(Infobases.ToList());
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка сохранения баз", ex);
            throw;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            await _repository.SaveAsync(Infobases.ToList()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка асинхронного сохранения баз", ex);
            _dialogs.ShowError(
                string.Format(LocalizationManager.T("Main.ErrSaveBases"), ex.Message),
                LocalizationManager.T("Main.ErrSaveBasesTitle"));
        }
    }

    private void SaveGroups()
    {
        try
        {
            _repository.SaveGroups(Groups.ToList());
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка сохранения групп", ex);
            throw;
        }
    }

    private async Task SaveGroupsAsync()
    {
        try
        {
            await _repository.SaveGroupsAsync(Groups.ToList()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка асинхронного сохранения групп", ex);
        }
    }

    /// <summary>
    /// Применяет выбранный язык интерфейса и сохраняет его в настройках.
    /// Язык применяется сразу (окна с привязками Loc обновляются) и
    /// восстанавливается при следующем запуске.
    /// </summary>
    /// <param name="code">Код языка, например "ru", "en" или загруженного внешнего.</param>
    public void ApplyLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return;

        try
        {
            var settings = _repository.LoadSettings();
            settings.Language = code;
            _repository.SaveSettings(settings);
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось сохранить язык интерфейса", ex);
        }

        try
        {
            Configuration_Management.Localization.LocalizationManager.Instance.SetLanguage(code);
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось применить язык интерфейса", ex);
        }
    }

    /// <summary>Интеграция с проводником Windows (функция №12): включена ли ассоциация .1CD и контекстное меню.</summary>
    public bool ExplorerIntegrationEnabled
    {
        get => _explorerIntegrationEnabled;
        set => SetProperty(ref _explorerIntegrationEnabled, value);
    }

    /// <summary>
    /// Применяет настройку интеграции с проводником Windows (функция №12): при включении
    /// регистрирует ассоциацию <c>.1CD</c> и команды контекстного меню, при выключении —
    /// удаляет их из реестра. Затем сохраняет настройку. Ошибки реестра не роняют приложение.
    /// </summary>
    public void ApplyExplorerIntegration(bool enabled)
    {
        try
        {
            var service = AppServices.GetRequiredService<IExplorerIntegrationService>();
            if (service.IsAvailable)
            {
                if (enabled) service.Register();
                else service.Unregister();
            }
        }
        catch (Exception ex)
        {
            try { _logger.Warn("[explorer] Не удалось изменить интеграцию с проводником: " + ex.Message); }
            catch { /* ignore */ }
        }

        ExplorerIntegrationEnabled = enabled;
        ScheduleSaveSettings();
    }

    public void SaveSettings()
    {
        _repository.SaveSettings(new AppSettings
        {
            // Актуальный язык интерфейса сохраняется всегда, чтобы выбор
            // пользователя не затирался при закрытии окна (OnClosing).
            Language = Configuration_Management.Localization.LocalizationManager.Instance.CurrentLanguage,
            ShowFavoritesOnly = _showFavoritesOnly,
            GroupByGroup = _groupByGroup,
            ShowEmptyGroups = _showEmptyGroups,
            Theme = _savedTheme,
            ActiveColorScheme = _activeColorScheme,
            // Устаревшие раздельные слоты больше не ведутся.
            LightColorScheme = null,
            DarkColorScheme = null,
            CollapsedGroups = _collapsedGroups.ToList(),
            InstalledPlatformVersions = _installedPlatformVersions,
            AdditionalPlatformSearchPaths = _additionalPlatformSearchPaths,
            NameColumnWidth = _nameColumnWidth,
            VersionColumnWidth = _versionColumnWidth,
            LaunchModeColumnWidth = _launchModeColumnWidth,
            ServerColumnWidth = _serverColumnWidth,
            LastLaunchColumnWidth = _lastLaunchColumnWidth,
            ShowFavoritesButton = _showFavoritesButton,
            ShowPinnedButton = _showPinnedButton,
            ShowTags = _showTags,
            ShowTagFilterPanel = _showTagFilterPanel,
            AllowMultipleInstances = _allowMultipleInstances,
            CheckForUpdatesOnStartup = _checkForUpdatesOnStartup,
            AutoUpdateEnabled = _autoUpdateEnabled,
            ComConnectorNameTemplate = _comConnectorNameTemplate,
            ComDetectTimeoutMs = _comDetectTimeoutMs,
            AvailabilityTcpPrecheckEnabled = _tcpPrecheckEnabled,
            MaxLaunchHistoryPerBase = _maxLaunchHistoryPerBase,
            ShowVersionColumn = _showVersionColumn,
            ShowConfigurationColumn = _showConfigurationColumn,
            ShowConfigurationVersionColumn = _showConfigurationVersionColumn,
            ConfigurationColumnWidth = _configurationColumnWidth,
            ConfigurationVersionColumnWidth = _configurationVersionColumnWidth,
            ActionsColumnWidth = _actionsColumnWidth,
            ShowRightPanelDetails = _showRightPanelDetails,
            ShowSessionLaunchPanel = _showSessionLaunchPanel,
            SessionClientMode = _sessionClientMode.ToString(),
            SessionArchitecture = _sessionArchitecture.ToString(),
            DefaultArchitecture = _defaultArchitecture,
            StatusShowConnectionPath = _statusShowConnectionPath,
            StatusShowArchitecture = _statusShowArchitecture,
            StatusShowLaunchMode = _statusShowLaunchMode,
            StatusShowPort = _statusShowPort,
            StatusShowPlatformVersion = _statusShowPlatformVersion,
            StatusShowClientType = _statusShowClientType,
            StatusShowConnectionType = _statusShowConnectionType,
            StatusShowUser = _statusShowUser,
            StatusShowId = _statusShowId,
            ShowLaunchModeColumn = _showLaunchModeColumn,
            ShowServerColumn = _showServerColumn,
            ShowLastLaunchColumn = _showLastLaunchColumn,
            ShowSizeColumn = _showSizeColumn,
            ShowActionsColumn = _showActionsColumn,
            SizeColumnWidth = _sizeColumnWidth,
            ShowModifiedColumn = _showModifiedColumn,
            ModifiedColumnWidth = _modifiedColumnWidth,
            ShowLastBackupColumn = _showLastBackupColumn,
            LastBackupColumnWidth = _lastBackupColumnWidth,
            ColumnOrder = _columnOrder.ToList(),
            WindowWidth = _windowWidth,
            WindowHeight = _windowHeight,
            WindowLeft = _windowLeft,
            WindowTop = _windowTop,
            WindowState = _windowState,
            RememberWindowLayout = _rememberWindowLayout,
            IbasesSyncMode = _ibasesSyncMode,
            IbasesSyncFilePath = _ibasesSyncFilePath,
            IbasesSyncTrigger = _ibasesSyncTrigger,
            IbasesSyncIntervalMinutes = _ibasesSyncIntervalMinutes,
            IbasesSyncScheduleTime = _ibasesSyncScheduleTime,
            IbasesBackupEnabled = _ibasesBackupEnabled,
            IbasesBackupKeepCount = _ibasesBackupKeepCount,
            IbasesLastSyncExportUtc = _ibasesLastSyncExportUtc,
            AddTimestampToExportFileName = _addTimestampToExportFileName,
            ExportTimestampFormat = _exportTimestampFormat,
            CloseToTray = _closeToTray,
            AfterLaunchAction = _afterLaunchAction,
            ShowTrayIcon = _showTrayIcon,
            ShowSystemNotifications = _showSystemNotifications,
            CatchUpMissedTasks = _catchUpMissedTasks,
            MaintenanceFreeSpaceWarningGb = _maintenanceFreeSpaceWarningGb,
            EscapeToTray = _escapeToTray,
            ConfirmCustomActions = _confirmCustomActions,
            CompactMode = _compactMode,
            ExplorerIntegrationEnabled = _explorerIntegrationEnabled,
            // Автозапуск при старте ОС (функция №31) и копия экрана (функция №30, Этап 8).
            AutoStartEnabled = _autoStartEnabled,
            ScreenshotHotkey = _screenshotHotkey,
            ScreenshotSaveDirectory = _screenshotSaveDirectory,
            TemplateCatalogPaths = _templateCatalogPaths.ToList(),
            HotkeyEnterprise = _hotkeyEnterprise,
            HotkeyConfigurator = _hotkeyConfigurator,
            HotkeyFavorite = _hotkeyFavorite,
            HotkeyEdit = _hotkeyEdit,
            HotkeyDelete = _hotkeyDelete,
            HotkeyClearCache = _hotkeyClearCache,
            HotkeyAdd = _hotkeyAdd,
            HotkeyPin = _hotkeyPin,
            HotkeyShowAll = _hotkeyShowAll,
            HotkeyShowFavorites = _hotkeyShowFavorites,
            HotkeyShowRecent = _hotkeyShowRecent,
            HotkeyClearSearch = _hotkeyClearSearch,
            HotkeyClearTags = _hotkeyClearTags,
            HotkeyRightPanelDetails = _hotkeyRightPanelDetails,
            HotkeyFindInList = _hotkeyFindInList,
            HotkeySwitchUser = _hotkeySwitchUser,
            HotkeyCheckUpdate = _hotkeyCheckUpdate,
            HotkeyActualReleases = _hotkeyActualReleases,
            HotkeyPlatformUpdate = _hotkeyPlatformUpdate,
            // Авторизация на сайте 1С (HTTP Basic Auth) при проверке обновлений конфигураций.
            UpdatesLogin = _updatesLogin,
            UpdatesPassword = _updatesPassword,
            // Блокировка сеансов ИБ (функция №20, Ctrl+Alt+L) и временная блокировка приложения (функция №19).
            HotkeySessionLock = _hotkeySessionLock,
            HotkeyLockApp = _hotkeyLockApp,
            // Администрирование ИБ (Этап 6, функция №29 + консоль серверов).
            HotkeyCheckIntegrity = _hotkeyCheckIntegrity,
            HotkeyServerConsole = _hotkeyServerConsole,
            AppLockPasswordHash = _appLockPasswordHash,
            // Активная блокировка переживает перезапуск приложения (issue #294).
            AppLockActive = _appLockActive,
            // Масштаб строк списка и его хоткеи (issue #303).
            ListZoomFactor = _listZoomFactor,
            HotkeyZoomIn = _hotkeyZoomIn,
            HotkeyZoomOut = _hotkeyZoomOut,
            HotkeyZoomReset = _hotkeyZoomReset,
            SortField = _sortField,
            SortAscending = _sortAscending,
            FavoriteHotkeyIds = _favoriteHotkeyIds.ToList(),
            NoGroupColor = _noGroupColor,
            NoGroupIconColor = _noGroupIconColor,
            NoGroupIcon = _noGroupIcon,
            PinnedColor = _pinnedColor,
            PinnedIconColor = _pinnedIconColor,
            PinnedIcon = _pinnedIcon,
            FontFamily = _fontFamily,
            FontSize = _fontSize,
            FontWeight = _fontWeight,
            FontStyle = _fontStyle,
            ElementFonts = _elementFonts,
            LastSelectedInfobaseId = _lastSelectedInfobaseId,
            LastSelectedGroupPath = _lastSelectedGroupPath,
            CustomLaunchParameters = _customLaunchParameters.ToList(),
            // Глобальное действие по двойному щелчку на базе (функция №28 StartManager).
            DefaultDoubleClickAction = _defaultDoubleClickAction,
            ProfileBackupDirectory = _profileBackupDirectory,
            ProfileRestoreOnStartup = _profileRestoreOnStartup,
            FileSizeCache = new Dictionary<string, Models.FileSizeCacheEntry>(_fileSizeCache),
            // Режим функциональности (Этап 10 StartManager): «Пользователь»/«Специалист»/«Разработчик».
            FunctionalMode = _functionalMode,
            LaunchConfigDefaults = _launchConfigDefaults
        });
    }

    /// <summary>
    /// Сохраняет ширины колонок списка баз в настройках.
    /// </summary>
    public void SaveColumnWidths(double nameWidth, double versionWidth, double configurationWidth, double configurationVersionWidth, double launchModeWidth, double serverWidth, double lastLaunchWidth, double actionsWidth)
    {
        NameColumnWidth = nameWidth;
        VersionColumnWidth = versionWidth;
        ConfigurationColumnWidth = configurationWidth;
        ConfigurationVersionColumnWidth = configurationVersionWidth;
        LaunchModeColumnWidth = launchModeWidth;
        ServerColumnWidth = serverWidth;
        LastLaunchColumnWidth = lastLaunchWidth;
        ActionsColumnWidth = actionsWidth;
        SaveSettings();
    }

    /// <summary>
    /// Обновляет ширины колонок в памяти (без сохранения в файл).
    /// Используется для синхронизации колонок строк во время перетаскивания разделителя.
    /// </summary>
    public void UpdateColumnWidths(double nameWidth, double versionWidth, double configurationWidth, double configurationVersionWidth, double launchModeWidth, double serverWidth, double lastLaunchWidth, double actionsWidth)
    {
        NameColumnWidth = nameWidth;
        VersionColumnWidth = versionWidth;
        ConfigurationColumnWidth = configurationWidth;
        ConfigurationVersionColumnWidth = configurationVersionWidth;
        LaunchModeColumnWidth = launchModeWidth;
        ServerColumnWidth = serverWidth;
        LastLaunchColumnWidth = lastLaunchWidth;
        ActionsColumnWidth = actionsWidth;
    }
}
#endif
