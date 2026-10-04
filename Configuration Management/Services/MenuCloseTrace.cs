using System;
using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Диагностическая трассировка клика, закрывающего контекстное меню дерева
/// (issue #340, седьмая попытка). Включается env-переменной <c>CM_MENUCLOSE_TRACE=1</c>
/// (по образцу <c>CM_TOOLTIP_TRACE</c>/<c>CM_COLUMNS_TRACE</c>) и пишет полную
/// последовательность событий меню/клика/выделения в
/// <c>%TEMP%\cm_menuclose_trace.log</c>. Общий для WPF и Avalonia.
/// </summary>
public static class MenuCloseTrace
{
    private static readonly bool Enabled = string.Equals(
        Environment.GetEnvironmentVariable("CM_MENUCLOSE_TRACE"),
        "1",
        StringComparison.OrdinalIgnoreCase);

    private static readonly string TracePath = Path.Combine(Path.GetTempPath(), "cm_menuclose_trace.log");

    private static readonly object Lock = new();

    /// <summary>
    /// Пишет строку в лог-файл трассировки. Без трейса (<c>CM_MENUCLOSE_TRACE</c> не равен 1)
    /// вызов ничего не делает. Ошибки записи игнорируются — трассировка не должна влиять на
    /// работу приложения.
    /// </summary>
    public static void Log(string message)
    {
        if (!Enabled)
            return;
        try
        {
            lock (Lock)
            {
                File.AppendAllText(
                    TracePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}\r\n");
            }
        }
        catch
        {
            // Трассировка не должна влиять на работу приложения.
        }
    }
}