using System;
using System.Collections.Generic;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Форма редактирования пользовательского действия контекстного меню (0.3.9.195, функция 7):
/// наименование, команда-шаблон, область применения, интерпретатор, флажки, таймаут (в
/// секундах), горячая клавиша и рабочая папка. Чистый .NET, используется обеими платформами.
/// Таймаут хранится в секундах для удобства ввода и конвертируется в
/// <see cref="CustomAction.TimeoutMs"/> в <see cref="ApplyTo"/>.
/// </summary>
public class CustomActionEditViewModel : ViewModelBase
{
    /// <summary>
    /// Доступные токены подстановки (вставляются двойным кликом в поле команды): русские
    /// имена в фигурных скобках наравне с прежним синтаксисом <c>%…%</c> (цикл 0.3.9.194).
    /// </summary>
    public static readonly IReadOnlyList<ScriptTokenHint> AvailableTokens = new[]
    {
        new ScriptTokenHint("{ИмяБазы}", "CustomAction.TokenName"),
        new ScriptTokenHint("{СтрокаПодключения}", "CustomAction.TokenConnectionString"),
        new ScriptTokenHint("{Каталог}", "CustomAction.TokenCatalog"),
        new ScriptTokenHint("{ПутьИБ}", "CustomAction.TokenPath"),
        new ScriptTokenHint("{Id}", "CustomAction.TokenId"),
        new ScriptTokenHint("{Тип}", "CustomAction.TokenType"),
        new ScriptTokenHint("{Сервер}", "CustomAction.TokenServer"),
        new ScriptTokenHint("{ИмяНаСервере}", "CustomAction.TokenDatabase"),
        new ScriptTokenHint("{ИмяГруппы}", "CustomAction.TokenGroup"),
        new ScriptTokenHint("{Пользователь}", "CustomAction.TokenUser"),
        new ScriptTokenHint("{Пароль}", "CustomAction.TokenPassword"),
        new ScriptTokenHint("{Дата}", "CustomAction.TokenDate")
    };

    /// <summary>
    /// Доступные значения интерпретатора для выпадающего списка формы
    /// (порядок: Авто, cmd, PowerShell, sh) — как у сценариев запуска скриптов.
    /// </summary>
    public static IReadOnlyList<ScriptShell> ShellOptions { get; } =
        new[] { ScriptShell.Auto, ScriptShell.Cmd, ScriptShell.PowerShell, ScriptShell.Sh };

    /// <summary>Нижняя граница таймаута, секунды.</summary>
    public const int MinTimeoutSeconds = 1;

    /// <summary>Верхняя граница таймаута, секунды (10 минут).</summary>
    public const int MaxTimeoutSeconds = 600;

    public CustomActionEditViewModel(CustomAction? action)
    {
        if (action is not null)
        {
            Name = action.Name;
            Command = action.Command ?? "";
            SelectedScope = action.Scope;
            SelectedShell = action.Shell;
            SupportsBatch = action.SupportsBatch;
            RunWithoutConfirm = action.RunWithoutConfirm;
            EscapeValues = action.EscapeValues;
            TimeoutSeconds = Math.Clamp(action.TimeoutMs / 1000, MinTimeoutSeconds, MaxTimeoutSeconds);
            Hotkey = action.Hotkey ?? "";
            WorkingDirectory = action.WorkingDirectory ?? "";
        }
    }

    /// <summary>Наименование действия (показывается в контекстном меню).</summary>
    public string Name { get; set; } = "";

    /// <summary>Команда/шаблон скрипта (может быть многострочной).</summary>
    public string Command { get; set; } = "";

    /// <summary>Область применения: база / группа / обе.</summary>
    public CustomActionScope SelectedScope { get; set; } = CustomActionScope.Both;

    /// <summary>Интерпретатор (shell): Auto — по платформе (cmd.exe/sh).</summary>
    public ScriptShell SelectedShell { get; set; } = ScriptShell.Auto;

    /// <summary>Показывать действие в блоке мультивыделения «Для выделенных (N)…».</summary>
    public bool SupportsBatch { get; set; }

    /// <summary>Выполнять без подтверждения (индивидуальный признак действия).</summary>
    public bool RunWithoutConfirm { get; set; }

    /// <summary>Экранировать подставляемые значения для выбранного shell (по умолчанию true).</summary>
    public bool EscapeValues { get; set; } = true;

    /// <summary>Таймаут ожидания завершения команды, секунды (диапазон 1…600).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Горячая клавиша действия (опционально), например «F8» или «Ctrl+Alt+F8».</summary>
    public string Hotkey { get; set; } = "";

    /// <summary>Рабочая папка процесса (опционально); пусто — каталог приложения.</summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>
    /// Валидация полей формы. Возвращает ключ локализации ошибки либо <c>null</c>,
    /// если всё корректно: имя и команда непустые, таймаут в диапазоне [1..600] секунд.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "CustomAction.NameRequired";
        if (string.IsNullOrWhiteSpace(Command))
            return "CustomAction.CommandRequired";
        if (TimeoutSeconds < MinTimeoutSeconds || TimeoutSeconds > MaxTimeoutSeconds)
            return "CustomAction.TimeoutInvalid";
        return null;
    }

    /// <summary>
    /// Переносит заполненные поля формы в действие. Идентификатор <see cref="CustomAction.Id"/>
    /// НЕ трогается (перезапись по тому же Id в хранилище не плодит дубликат).
    /// </summary>
    public void ApplyTo(CustomAction action)
    {
        action.Name = Name.Trim();
        action.Command = Command.Trim();
        action.Scope = SelectedScope;
        action.Shell = SelectedShell;
        action.SupportsBatch = SupportsBatch;
        action.RunWithoutConfirm = RunWithoutConfirm;
        action.EscapeValues = EscapeValues;
        action.TimeoutMs = Math.Clamp(TimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds) * 1000;
        action.Hotkey = Hotkey.Trim();
        action.WorkingDirectory = WorkingDirectory.Trim();
    }

    /// <summary>
    /// Пример полной командной строки действия с подстановками для указанной базы
    /// (живая подсказка в окне редактора): <see cref="ScriptParameterResolver.BuildActionShellCommandLine"/>
    /// с обёрткой выбранного интерпретатора действия.
    /// </summary>
    public static string BuildExampleCommandLine(CustomAction draft, Infobase? exampleBase, DateTime? now = null)
        => ScriptParameterResolver.BuildActionShellCommandLine(draft, exampleBase, now);
}