using System.Collections.Generic;
using Configuration_Management.Localization;

namespace Configuration_Management.Services;

/// <summary>
/// Вид замечания перед установкой платформы 1С. Предупреждения (<see cref="Warning"/>)
/// требуют подтверждения пользователя; информирование (<see cref="Info"/>) только
/// выводится в журнал и показывается в диалоге без блокировки продолжения.
/// </summary>
public enum PlatformPreflightWarningKind
{
    /// <summary>Информирование (например, установка без прав администратора).</summary>
    Info,

    /// <summary>Предупреждение, требующее решения пользователя (занятые процессы,
    /// мало места, непроверенная подпись).</summary>
    Warning
}

/// <summary>
/// Замечание проверки перед установкой платформы: вид и локализованный текст
/// (ключи «PlatformUpdate.Error.*»).
/// </summary>
public sealed record PlatformInstallWarning(PlatformPreflightWarningKind Kind, string Text);

/// <summary>
/// Проверка готовности к установке технологической платформы 1С перед запуском
/// установщика (функция 9, этап 0.3.9.214): занятые процессы 1С, права
/// администратора, свободное место на диске (размер дистрибутива + 1 ГБ запаса)
/// и подпись файла. Чистый класс без платформенных зависимостей — входные данные
/// собираются в ViewModel через инжектируемые делегаты, поэтому проверка
/// покрывается unit-тестами.
/// </summary>
public static class PlatformInstallPreflight
{
    /// <summary>Дополнительный запас свободного места сверх размера дистрибутива, байт (1 ГБ).</summary>
    public const long RequiredExtraBytes = 1024L * 1024 * 1024;

    /// <summary>
    /// Собирает список замечаний перед установкой.
    /// </summary>
    /// <param name="runningProcesses">Имена запущенных процессов 1С (пустой список — не заняты).</param>
    /// <param name="isAdmin">True — процесс работает с правами администратора.</param>
    /// <param name="freeBytes">Свободное место на целевом диске, байт; null — определить не удалось.</param>
    /// <param name="neededBytes">Размер дистрибутива, байт (к нему добавляется <see cref="RequiredExtraBytes"/>).</param>
    /// <param name="isSigned">True — подпись файла установщика проверена и действительна.</param>
    /// <returns>Список замечаний (пустой — можно устанавливать без подтверждения).</returns>
    public static IReadOnlyList<PlatformInstallWarning> Check(
        IReadOnlyList<string>? runningProcesses,
        bool isAdmin,
        long? freeBytes,
        long neededBytes,
        bool isSigned)
    {
        var warnings = new List<PlatformInstallWarning>(4);

        if (runningProcesses is { Count: > 0 })
        {
            warnings.Add(new PlatformInstallWarning(
                PlatformPreflightWarningKind.Warning,
                string.Format(
                    LocalizationManager.T("PlatformUpdate.Error.RunningProcesses"),
                    string.Join(", ", runningProcesses))));
        }

        if (!isAdmin)
        {
            warnings.Add(new PlatformInstallWarning(
                PlatformPreflightWarningKind.Info,
                LocalizationManager.T("PlatformUpdate.Error.NotAdmin")));
        }

        if (freeBytes is long free && free < neededBytes + RequiredExtraBytes)
        {
            warnings.Add(new PlatformInstallWarning(
                PlatformPreflightWarningKind.Warning,
                LocalizationManager.T("PlatformUpdate.Error.NotEnoughSpace")));
        }

        if (!isSigned)
        {
            warnings.Add(new PlatformInstallWarning(
                PlatformPreflightWarningKind.Warning,
                LocalizationManager.T("PlatformUpdate.Error.Signature")));
        }

        return warnings;
    }
}