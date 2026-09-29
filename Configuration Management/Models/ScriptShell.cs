namespace Configuration_Management.Models;

/// <summary>
/// Интерпретатор (shell) для запуска сценария скрипта (issue #308, п.9).
/// <para>
/// <see cref="Auto"/> — определяется по платформе при запуске: Windows — cmd.exe,
/// Linux — /bin/sh (прежнее поведение, сохраняется для старых сценариев).
/// Явные значения (<see cref="Cmd"/>, <see cref="PowerShell"/>, <see cref="Sh"/>)
/// оборачивают команду соответствующим интерпретатором независимо от платформы
/// (например, PowerShell можно выбрать и на Windows, и на Linux при наличии pwsh).
/// </para>
/// </summary>
public enum ScriptShell
{
    /// <summary>Автоопределение по платформе: cmd.exe на Windows, /bin/sh на Linux.</summary>
    Auto = 0,

    /// <summary>Windows: <c>cmd.exe /c <команда></c>.</summary>
    Cmd = 1,

    /// <summary>PowerShell: <c>powershell -NoProfile -Command <команда></c>.</summary>
    PowerShell = 2,

    /// <summary>Unix-шелл: <c>/bin/sh -c <команда></c>.</summary>
    Sh = 3
}