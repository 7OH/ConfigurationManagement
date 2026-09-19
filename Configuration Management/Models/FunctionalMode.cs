namespace Configuration_Management.Models;

/// <summary>
/// Режим функциональности приложения (Этап 10 дорожной карты StartManager).
/// Определяет доступный набор возможностей и ограничение «системного меню».
/// <list type="bullet">
/// <item><see cref="User"/> («Пользователь») — только базовые операции запуска;
/// системное меню (редактирование, удаление, выгрузки, администрирование) скрыто.</item>
/// <item><see cref="Specialist"/> («Специалист») — полный набор операций со списком баз.</item>
/// <item><see cref="Developer"/> («Разработчик») — на текущем этапе полностью совпадает
/// со «Специалистом» и даёт такой же полный доступ; разработческие инструменты
/// конфигуратора запланированы на последующие этапы дорожной карты.</item>
/// </list>
/// Хранится в настройках строкой (<see cref="AppSettings.FunctionalMode"/>) для обратной
/// совместимости, а разбор/сравнение выполняют канонические константы <see cref="FunctionalModes"/>.
/// </summary>
public enum FunctionalMode
{
    /// <summary>«Пользователь»: ограниченный набор возможностей (системное меню скрыто).</summary>
    User,

    /// <summary>«Специалист»: полный набор операций со списком баз.</summary>
    Specialist,

    /// <summary>«Разработчик»: на текущем этапе полностью совпадает со «Специалистом»;
    /// разработческие инструменты конфигуратора запланированы на последующие этапы.</summary>
    Developer
}

/// <summary>
/// Канонические строковые значения режима функциональности и вспомогательные методы
/// разбора/сравнения. Строки стабильны и не зависят от локали — они хранятся в
/// <c>settings.json</c>, поэтому смена языка интерфейса не ломает сохранённый режим.
/// </summary>
public static class FunctionalModes
{
    /// <summary>Каноническое значение режима «Пользователь».</summary>
    public const string User = "User";

    /// <summary>Каноническое значение режима «Специалист».</summary>
    public const string Specialist = "Specialist";

    /// <summary>Каноническое значение режима «Разработчик».</summary>
    public const string Developer = "Developer";

    /// <summary>Режим по умолчанию — «Специалист» (полный набор операций со списком баз).</summary>
    public const string Default = Specialist;

    /// <summary>Разбирает строковое значение в <see cref="FunctionalMode"/>; неизвестное — <see cref="FunctionalMode.Specialist"/>.</summary>
    public static FunctionalMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return FunctionalMode.Specialist;

        var v = value.Trim();
        if (string.Equals(v, User, StringComparison.OrdinalIgnoreCase))
            return FunctionalMode.User;
        if (string.Equals(v, Developer, StringComparison.OrdinalIgnoreCase))
            return FunctionalMode.Developer;
        return FunctionalMode.Specialist;
    }

    /// <summary>Возвращает каноническую строку для <see cref="FunctionalMode"/>.</summary>
    public static string ToString(FunctionalMode mode) => mode switch
    {
        FunctionalMode.User => User,
        FunctionalMode.Developer => Developer,
        _ => Specialist
    };

    /// <summary>Признак режима «Пользователь» (ограничение системного меню).</summary>
    public static bool IsUser(string? value) =>
        string.Equals((value ?? string.Empty).Trim(), User, StringComparison.OrdinalIgnoreCase);

    /// <summary>Признак режима «Разработчик».</summary>
    public static bool IsDeveloper(string? value) =>
        string.Equals((value ?? string.Empty).Trim(), Developer, StringComparison.OrdinalIgnoreCase);
}