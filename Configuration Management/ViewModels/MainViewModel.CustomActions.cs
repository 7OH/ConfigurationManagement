using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Мост функции «Пользовательские действия контекстного меню» (0.3.9.195, функция 7):
/// кэш действий из <see cref="ICustomActionsStore"/>, выполнение действия для целей
/// области (одиночная база / мультивыделение / группа) с подтверждением по умолчанию,
/// маскированием пароля в истории/журнале, записью истории запусков баз, уведомлением
/// о сводке и индикацией выполнения. Общий для обеих платформ файл (без #if); диалоги
/// и сохранение списка делегируются платформенным partial-хукам
/// (MainViewModel.CustomActions.Windows.cs / *.Avalonia.cs).
/// </summary>
public partial class MainViewModel
{
    private IReadOnlyList<CustomAction>? _customActionsCache;

    /// <summary>
    /// Кэш пользовательских действий: загружается при первом обращении (при старте — из
    /// конструктора) и перечитывается <see cref="ReloadCustomActions"/> после окна настроек.
    /// </summary>
    public IReadOnlyList<CustomAction> CustomActions =>
        _customActionsCache ??= LoadCustomActionsFromStore();

    /// <summary>Перечитывает кэш действий из хранилища (после закрытия окна настроек).</summary>
    public void ReloadCustomActions()
    {
        _customActionsCache = LoadCustomActionsFromStore();
        _hotkeyCustomActions = null;
        OnPropertyChanged(nameof(CustomActions));
        OnPropertyChanged(nameof(HotkeyCustomActions));
    }

    private IReadOnlyDictionary<string, CustomAction>? _hotkeyCustomActions;

    /// <summary>
    /// Действия по нормализованной горячей клавише (0.3.9.198): ключ — сочетание без
    /// ведущих/хвостовых пробелов, сравнение без учёта регистра; пустые хоткеи пропускаются.
    /// Дубли в редакторе запрещены валидацией, но при ручной правке JSON-файла берётся
    /// последний по порядку список. Используется для регистрации хоткеев окна (WPF/Avalonia).
    /// </summary>
    public IReadOnlyDictionary<string, CustomAction> HotkeyCustomActions =>
        _hotkeyCustomActions ??= BuildHotkeyCustomActions();

    private IReadOnlyDictionary<string, CustomAction> BuildHotkeyCustomActions()
    {
        var result = new Dictionary<string, CustomAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in CustomActions)
        {
            var hotkey = action.Hotkey?.Trim();
            if (string.IsNullOrEmpty(hotkey))
                continue;
            // Дубль сочетания при ручной правке файла: побеждает последний по порядку.
            result[hotkey] = action;
        }
        return result;
    }

    private IReadOnlyList<CustomAction> LoadCustomActionsFromStore()
    {
        try
        {
            return AppServices.GetRequiredService<ICustomActionsStore>().LoadAll();
        }
        catch
        {
            // Сервис может отсутствовать в изолированном контексте — действия не критичны.
            return Array.Empty<CustomAction>();
        }
    }

    private ICommand? _showCustomActionsSettingsCommand;

    /// <summary>
    /// Команда открытия окна «Пользовательские действия» (0.3.9.196): настраивает список
    /// действий (окно списка + редактор). Вызывается из «Утилит» и пункта «Настроить
    /// действия…» контекстного меню (интеграция меню — этапы 0.3.9.197/0.3.9.198).
    /// Открытие окна — платформенный partial-метод <see cref="ExecuteShowCustomActionsSettings"/>;
    /// после закрытия окна кэш <see cref="CustomActions"/> перечитывается, чтобы подменю
    /// актуализировалось при следующем открытии.
    /// </summary>
    public ICommand ShowCustomActionsSettingsCommand =>
        _showCustomActionsSettingsCommand ??= new RelayCommand(ExecuteShowCustomActionsSettings);

    /// <summary>
    /// Открывает окно списка пользовательских действий и перечитывает кэш после его
    /// закрытия (платформенная реализация: Windows/WPF — ShowDialog, Avalonia/Linux —
    /// ShowSync; см. MainViewModel.CustomActions.Windows.cs / *.Avalonia.cs).
    /// </summary>
    partial void ExecuteShowCustomActionsSettings();

    private bool _isCustomActionRunning;

    /// <summary>Идёт ли выполнение пользовательского действия (индикация: пункты меню недоступны).</summary>
    public bool IsCustomActionRunning
    {
        get => _isCustomActionRunning;
        private set => SetProperty(ref _isCustomActionRunning, value);
    }

    private string _runningCustomActionText = "";

    /// <summary>Текст индикатора выполняющегося действия (статус-бар/заголовок пункта).</summary>
    public string RunningCustomActionText
    {
        get => _runningCustomActionText;
        private set => SetProperty(ref _runningCustomActionText, value);
    }

    /// <summary>
    /// Выполняет действие для целей контекста:
    /// <list type="bullet">
    /// <item><see cref="CustomActionContext.SingleBase"/> — выбранная база
    /// (<see cref="SelectedInfobase"/>);</item>
    /// <item><see cref="CustomActionContext.Batch"/> — базы мультивыделения
    /// (<see cref="BatchSelectedInfobases"/>);</item>
    /// <item><see cref="CustomActionContext.Group"/> — базы текущей группы
    /// (<see cref="SelectedGroupNode"/>.<see cref="GroupNodeViewModel.FullPath"/>).</item>
    /// </list>
    /// Все цели дополнительно проходят <see cref="CustomActionFilter.FilterVisibleTargets"/>
    /// (скрытые приватные базы исключаются — страховка приватности). Пустые цели → предупреждение
    /// без выполнения. Подтверждение запрашивается, если включено глобально
    /// (<see cref="AppSettings.ConfirmCustomActions"/>) и не отключено индивидуально
    /// (<see cref="CustomAction.RunWithoutConfirm"/>). Команды выполняются параллельно
    /// (<see cref="CustomActionRunner.RunBatchAsync"/>); в историю запусков каждой базы пишется
    /// запись «Действие:<имя>» с командой, в которой значение пароля заменено на «***».
    /// Индикация <see cref="IsCustomActionRunning"/> поднята на время выполнения (в finally —
    /// снята).
    /// </summary>
    /// <param name="action">Выполняемое действие.</param>
    /// <param name="context">Контекст вызова (определяет набор целей).</param>
    /// <param name="executeAsync">Исполнитель команды; null — стандартный
    /// <see cref="ExternalCommandRunner.RunAsync"/> (для тестов моста передаётся fake).</param>
    public async Task ExecuteCustomActionAsync(
        CustomAction action,
        CustomActionContext context,
        Func<string, int, CancellationToken, Task<bool>>? executeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        // Защита от повторного вызова во время выполнения (двойной клик по пункту меню).
        if (IsCustomActionRunning)
            return;

        var targets = SelectCustomActionTargets(context);
        if (targets.Count == 0)
        {
            ShowCustomActionWarning(
                LocalizationManager.T("CustomAction.NoTargets"),
                LocalizationManager.T("CustomAction.Confirm.Title"));
            return;
        }

        var globalConfirm = ReadConfirmCustomActionsSetting();
        if (CustomActionExecutionPlan.ShouldConfirm(action, globalConfirm))
        {
            var message = string.Format(
                LocalizationManager.T("CustomAction.Confirm.Format"), action.Name, targets.Count);
            var title = LocalizationManager.T("CustomAction.Confirm.Title");
            if (!ConfirmCustomAction(message, title))
                return;
        }

        IsCustomActionRunning = true;
        RunningCustomActionText = string.Format(LocalizationManager.T("CustomAction.Running"), action.Name);
        try
        {
            var runner = new CustomActionRunner(executeAsync);
            var results = await runner.RunBatchAsync(action, targets, CancellationToken.None);

            foreach (var result in results)
            {
                var entry = CustomActionExecutionPlan.BuildHistoryEntry(runner, action, result);
                entry.Infobase.AddLaunchHistory(entry.Mode, entry.Details);
            }
            AfterCustomActionsHistorySaved();

            var succeeded = results.Count(r => r.Success);
            var failed = results.Count - succeeded;
            _logger.Info(
                $"Действие «{action.Name}»: выполнено баз {results.Count}, успешно — {succeeded}, ошибок — {failed}");
            _logger.Info(string.Format(
                LocalizationManager.T("CustomAction.DoneSummaryFormat"), action.Name, succeeded, failed));
            NotifyCustomActionCompleted(action, succeeded, failed);
        }
        finally
        {
            IsCustomActionRunning = false;
            RunningCustomActionText = "";
        }
    }

    /// <summary>Глобальная настройка подтверждения выполнения действий (по умолчанию включено).</summary>
    private static bool ReadConfirmCustomActionsSetting()
    {
        try
        {
            return AppServices.GetRequiredService<IInfobaseRepository>()
                .LoadSettings()?.ConfirmCustomActions ?? true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Цели контекста из видимых баз: приватные базы заблокированного профиля исключаются.</summary>
    private IReadOnlyList<Infobase> SelectCustomActionTargets(CustomActionContext context)
    {
        var canShowPrivate = CanShowPrivateBasesForConnectionReplace();
        var currentGroup = SelectedGroupNode?.FullPath;
        return CustomActionExecutionPlan.SelectTargets(
            context, SelectedInfobase, Infobases.ToList(), BatchSelectedIds, currentGroup, canShowPrivate);
    }

    /// <summary>
    /// Видимые цели контекста для индикации подменю контекстного меню (0.3.9.197):
    /// та же фильтрация, что и при выполнении <see cref="ExecuteCustomActionAsync"/>
    /// (приватные базы скрытого профиля исключаются). Пустой список — подменю не показывается.
    /// </summary>
    public IReadOnlyList<Infobase> GetCustomActionTargets(CustomActionContext context)
        => SelectCustomActionTargets(context);

    /// <summary>
    /// Готовая структура пунктов подменю «Пользовательские действия…» (0.3.9.198):
    /// единая точка отбора для WPF и Avalonia. Определяет контекст по состоянию выделения
    /// (мультивыделение > база > группа), отбирает действия
    /// <see cref="CustomActionFilter.SelectActions"/> и проверяет видимость целей
    /// <see cref="GetCustomActionTargets"/> (приватные базы скрытого профиля скрывают
    /// подменю). Нет контекста/действий/целей → пустой список.
    /// </summary>
    /// <param name="context">Определённый контекст (SingleBase при отсутствии контекста).</param>
    /// <param name="targetCount">Число видимых целей контекста (0 — подменю не показывать).</param>
    public IReadOnlyList<CustomActionMenuItemInfo> BuildCustomActionMenuItems(
        out CustomActionContext context, out int targetCount)
    {
        var determined = CustomActionExecutionPlan.DetermineMenuContext(
            BatchSelectedCount, SelectedInfobase is not null, SelectedGroupNode is not null);
        if (determined is null)
        {
            context = CustomActionContext.SingleBase;
            targetCount = 0;
            return Array.Empty<CustomActionMenuItemInfo>();
        }

        context = determined.Value;
        var menuContext = context;
        var targets = GetCustomActionTargets(menuContext);
        targetCount = targets.Count;
        var actions = CustomActionExecutionPlan.SelectMenuActions(CustomActions, menuContext, targets);
        return actions
            .Select(a => new CustomActionMenuItemInfo(a, menuContext))
            .ToList();
    }

    /// <summary>Системное уведомление о завершении действия со сводкой (fire-and-forget).</summary>
    private void NotifyCustomActionCompleted(CustomAction action, int succeeded, int failed)
    {
        try
        {
            AppServices.GetRequiredService<INotificationService>().Show(
                LocalizationManager.T("App.Title"),
                string.Format(LocalizationManager.T("Notify.CustomActionDone"), action.Name, succeeded, failed),
                failed == 0 ? NotificationKind.Success : NotificationKind.Warning);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Системное уведомление о действии «{action.Name}» не показано: {ex.Message}");
        }
    }

    // ---------- Платформенные хуки (Windows: _dialogs/Save; Avalonia: _dialog/SaveSilently) ----------

    /// <summary>Запрашивает подтверждение выполнения (платформенная реализация).</summary>
    private partial bool ConfirmCustomAction(string message, string title);

    /// <summary>Показывает предупреждение (платформенная реализация).</summary>
    partial void ShowCustomActionWarning(string message, string title);

    /// <summary>Сохраняет список баз после записи истории запусков (платформенная реализация).</summary>
    partial void AfterCustomActionsHistorySaved();
}

/// <summary>
/// Чистый помощник моста «Пользовательские действия» (0.3.9.195): выбор целей области,
/// решение о подтверждении и построение записи истории запуска с маскированием пароля.
/// Без зависимостей от платформы — покрыт юнит-тестами (CustomActionExecutionPlanTests),
/// т.к. конструктор <see cref="MainViewModel"/> слишком нагружен для прямого теста.
/// </summary>
public static class CustomActionExecutionPlan
{
    /// <summary>
    /// Отбирает цели области из списка баз с учётом приватности:
    /// SingleBase — выбранная база; Batch — базы мультивыделения (по Id, порядок списка);
    /// Group — базы текущей группы (по полному пути). Результат проходит
    /// <see cref="CustomActionFilter.FilterVisibleTargets"/> (скрытые приватные исключаются).
    /// </summary>
    public static IReadOnlyList<Infobase> SelectTargets(
        CustomActionContext context,
        Infobase? selectedInfobase,
        IReadOnlyList<Infobase> infobases,
        IReadOnlyCollection<string>? batchIds,
        string? currentGroup,
        bool canShowPrivateBases)
    {
        IReadOnlyList<Infobase> raw;
        switch (context)
        {
            case CustomActionContext.SingleBase:
                raw = selectedInfobase is null ? Array.Empty<Infobase>() : new[] { selectedInfobase };
                break;
            case CustomActionContext.Batch:
                raw = ConnectionReplacementPlanner.SelectCandidates(
                    infobases ?? Array.Empty<Infobase>(), batchIds, null, ConnectionReplaceScope.BatchSelected);
                break;
            case CustomActionContext.Group:
                raw = ConnectionReplacementPlanner.SelectCandidates(
                    infobases ?? Array.Empty<Infobase>(), null, currentGroup, ConnectionReplaceScope.CurrentGroup);
                break;
            default:
                raw = Array.Empty<Infobase>();
                break;
        }

        return CustomActionFilter.FilterVisibleTargets(raw, canShowPrivateBases);
    }

    /// <summary>
    /// Нужно ли запрашивать подтверждение перед выполнением: глобальная настройка
    /// (<see cref="AppSettings.ConfirmCustomActions"/>) и индивидуальный флаг действия
    /// «Выполнять без подтверждения» (<see cref="CustomAction.RunWithoutConfirm"/>).
    /// </summary>
    public static bool ShouldConfirm(CustomAction action, bool globalConfirmEnabled)
        => globalConfirmEnabled && !action.RunWithoutConfirm;

    /// <summary>
    /// Контекст подменю «Пользовательские действия…» по состоянию выделения (0.3.9.198):
    /// мультивыделение > выбранная база > выбранная группа; нет контекста → <c>null</c>
    /// (подменю скрыто). Единая точка определения для WPF и Avalonia (меню и горячие клавиши).
    /// </summary>
    public static CustomActionContext? DetermineMenuContext(
        int batchSelectedCount, bool hasSelectedInfobase, bool hasSelectedGroupNode)
    {
        if (batchSelectedCount > 1)
            return CustomActionContext.Batch;
        if (hasSelectedInfobase)
            return CustomActionContext.SingleBase;
        if (hasSelectedGroupNode)
            return CustomActionContext.Group;
        return null;
    }

    /// <summary>
    /// Отбирает действия для подменю (0.3.9.198): контекст и видимые цели непустые →
    /// <see cref="CustomActionFilter.SelectActions"/>, иначе пустой список. Единая логика
    /// построения подменю для WPF и Avalonia (риск «расхождения платформ»).
    /// </summary>
    public static IReadOnlyList<CustomAction> SelectMenuActions(
        IReadOnlyList<CustomAction> actions,
        CustomActionContext context,
        IReadOnlyList<Infobase> visibleTargets)
    {
        if (actions is null || actions.Count == 0 || visibleTargets is null || visibleTargets.Count == 0)
            return Array.Empty<CustomAction>();
        return CustomActionFilter.SelectActions(actions, context);
    }

    /// <summary>
    /// Запись истории запуска базы: режим «Действие:<имя действия>» и полная командная
    /// строка с обёрткой интерпретатора, в которой значение пароля базы заменено на «***»
    /// (<see cref="CustomActionRunner.MaskSecrets"/>).
    /// </summary>
    public static CustomActionHistoryEntry BuildHistoryEntry(
        CustomActionRunner runner, CustomAction action, CustomActionRunResult result, DateTime? now = null)
    {
        var ib = result.Infobase;
        var password = ib.Connection?.Password;
        var masked = CustomActionRunner.MaskSecrets(runner.BuildLogCommandLine(action, ib, now), password);
        return new CustomActionHistoryEntry(ib, "Действие:" + action.Name, masked);
    }
}

/// <summary>Запись истории запуска для одной базы: база, режим и детали с маскированным паролем.</summary>
/// <param name="Infobase">База, в историю которой добавляется запись.</param>
/// <param name="Mode">Режим записи (префикс «Действие:<имя>»).</param>
/// <param name="Details">Команда с маскированным паролем.</param>
public sealed record CustomActionHistoryEntry(Infobase Infobase, string Mode, string Details);

/// <summary>
/// Пункт подменю «Пользовательские действия…» (0.3.9.198): действие и контекст вызова,
/// для которого оно отобрано (<see cref="MainViewModel.BuildCustomActionMenuItems"/>).
/// </summary>
/// <param name="Action">Действие (пункт меню: имя, хоткей).</param>
/// <param name="Context">Контекст вызова (определяет цели выполнения).</param>
public sealed record CustomActionMenuItemInfo(CustomAction Action, CustomActionContext Context);