namespace Configuration_Management.Services;

/// <summary>Запущенный процесс платформы 1С (клиент/конфигуратор).</summary>
/// <param name="ProcessName">Имя процесса (например «1cv8c.exe»).</param>
/// <param name="CommandLine">Полная командная строка процесса (может быть пуста — нет прав на чтение).</param>
public sealed record RunningOneCProcess(string ProcessName, string CommandLine);

/// <summary>
/// Список запущенных процессов платформы 1С на текущей машине. Используется
/// индикатором «база сейчас запущена»: командные строки процессов сопоставляются
/// с параметрами подключения баз (<see cref="RunningInfobaseMatcher"/>).
/// Реализация обязана тихо деградировать: при любой ошибке (нет прав, WMI
/// недоступен) возвращается пустой список, а не исключение.
/// </summary>
public interface IRunningInfobasesService
{
    /// <summary>Текущие процессы 1С (1cv8/1cv8c и совместимые) с их командными строками.</summary>
    System.Collections.Generic.IReadOnlyList<RunningOneCProcess> GetRunning();
}
