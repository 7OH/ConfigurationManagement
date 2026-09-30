using Configuration_Management.Services;

namespace Configuration_Management.Models;

/// <summary>Область применения пользовательского действия в контекстных меню.</summary>
public enum CustomActionScope
{
    /// <summary>Только в меню базы (одиночная/выбранная).</summary>
    Base,

    /// <summary>Только в меню группы (выполняется для всех баз группы).</summary>
    Group,

    /// <summary>И в меню базы, и в меню группы.</summary>
    Both
}

/// <summary>
/// Пользовательское действие (функция 7): произвольная команда/скрипт через системный shell
/// с подстановкой параметров выбранной базы. Хранится в JSON-файле custom_actions.json
/// (см. Services.CustomActionsStore).
/// </summary>
public class CustomAction
{
    /// <summary>Идентификатор действия (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Имя действия — показывается в контекстном меню.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Команда/шаблон скрипта (одна строка). Поддерживаются токены %...% и {...}:
    /// {ИмяБазы}, {СтрокаПодключения}, {Каталог}, {ПутьИБ}, {Id}, {Тип}, {Сервер},
    /// {ИмяНаСервере}, {ИмяГруппы}, {Пользователь}, {Пароль}, {Дата} и др.
    /// Передаётся системному shell как есть (после подстановки).
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>Интерпретатор (shell): Auto — по платформе (cmd.exe/sh).</summary>
    public ScriptShell Shell { get; set; } = ScriptShell.Auto;

    /// <summary>Область применения: база / группа / обе.</summary>
    public CustomActionScope Scope { get; set; } = CustomActionScope.Both;

    /// <summary>
    /// Показывать действие в блоке мультивыделения «Для выделенных (N)…»: при true
    /// команда выполняется для каждой выделенной базы (параллельно).
    /// </summary>
    public bool SupportsBatch { get; set; }

    /// <summary>Выполнять без подтверждения (индивидуальный признак действия).</summary>
    public bool RunWithoutConfirm { get; set; }

    /// <summary>
    /// Экранировать подставляемые значения для выбранного shell (по умолчанию true):
    /// имя базы, пути и пр. оборачиваются в кавычки/экранируются спецсимволы —
    /// команда безопасна при пробелах и спецсимволах в значениях.
    /// </summary>
    public bool EscapeValues { get; set; } = true;

    /// <summary>Таймаут ожидания завершения команды (мс), как pre-команды: по умолчанию 30 с.</summary>
    public int TimeoutMs { get; set; } = ExternalCommandRunner.DefaultPreCommandTimeoutMs;

    /// <summary>Горячая клавиша действия (опционально), например «F8» или «Ctrl+Alt+F8».</summary>
    public string Hotkey { get; set; } = "";

    /// <summary>Рабочая папка процесса (опционально); пусто — наследуется каталог приложения.</summary>
    public string WorkingDirectory { get; set; } = "";
}