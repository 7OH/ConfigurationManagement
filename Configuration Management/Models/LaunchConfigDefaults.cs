namespace Configuration_Management.Models;

/// <summary>
/// Параметры запуска 1С по умолчанию из файла <c>1CLaunch.cfg</c> (Этап 10 дорожной
/// карты StartManager). Содержит ключи, которые стартер StartManager хранил в конфиге:
/// <list type="bullet">
/// <item><c>configpath</c> — тип конфигурации/каталога: <c>asp</c> / <c>usp</c> / <c>sp</c>;</item>
/// <item><c>configdir</c> — каталог конфигураций;</item>
/// <item><c>appmode</c> — режим приложения (например 1 — обычное приложение, 2 — управляемое);</item>
/// <item><c>selectmodeoff</c> — отключить «режим выбора» (1 — включено, 0 — выключено).</item>
/// </list>
/// Значения служат исходными параметрами по умолчанию для запуска и окна «Параметры».
/// Приоритет над ними имеют аргументы командной строки приложения и собственные
/// параметры запуска информационной базы (см. <see cref="Services.OneCLaunchConfigReader"/>).
/// </summary>
public sealed class LaunchConfigDefaults
{
    /// <summary>Тип конфигурации: asp / usp / sp (пусто — не задано).</summary>
    public string ConfigPath { get; set; } = "";

    /// <summary>Каталог конфигураций (пусто — не задан).</summary>
    public string ConfigDir { get; set; } = "";

    /// <summary>Режим приложения (пусто — не задан).</summary>
    public string AppMode { get; set; } = "";

    /// <summary>Признак отключения «режима выбора» (selectmodeoff=1).</summary>
    public bool SelectModeOff { get; set; }

    /// <summary>Признак наличия хотя бы одного заданного значения.</summary>
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(ConfigPath) ||
        !string.IsNullOrWhiteSpace(ConfigDir) ||
        !string.IsNullOrWhiteSpace(AppMode) ||
        SelectModeOff;
}