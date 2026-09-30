using System.Text.RegularExpressions;

namespace Configuration_Management.Services;

/// <summary>Маскирует секреты перед показом диагностического текста пользователю.</summary>
internal static class SensitiveDataMasker
{
    /// <summary>
    /// Значение DBPwd в строке подключения 1С. Внутренняя кавычка кодируется парой кавычек,
    /// поэтому пара должна поглощаться целиком, прежде чем одиночная кавычка закроет значение.
    /// </summary>
    private static readonly Regex DbPasswordRegex = new(
        @"(\bDBPwd\s*=\s*"")(?:(?:"""")|[^""])*""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Значение --password в командной строке rac (встроенный монитор серверов 1С):
    /// скрывается целиком при журналировании команд rac.
    /// </summary>
    private static readonly Regex RacPasswordRegex = new(
        @"(--password=)[^\s]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Пароль хранилища конфигурации в командной строке пакетных операций DESIGNER:
    /// ключ /ConfigurationRepositoryP "значение". Значение скрывается полностью при
    /// формировании <c>DesignerBatchInfo.CommandLine</c>, т.к. при ошибке операции
    /// CompleteDesignerBatch выводит командную строку в ErrorMessage (пароль не должен
    /// утекать в UI/журнал). При необходимости закрывает и аналогичный ключ -Pwd"…".
    /// </summary>
    private static readonly Regex RepositoryPasswordRegex = new(
        @"(/ConfigurationRepositoryP\s*"")(?:(?:"""")|[^""])*""|-Pwd\s*""[^""]*""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Скрывает пароль СУБД во всём тексте — как в показанной команде CREATEINFOBASE,
    /// так и в диагностике платформы, если она повторила строку подключения.
    /// </summary>
    internal static string MaskDbPassword(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return DbPasswordRegex.Replace(text, match => match.Groups[1].Value + "********\"");
    }

    /// <summary>
    /// Маскирует пароль администратора кластера в командной строке rac
    /// (аргумент <c>--password=...</c> заменяется на <c>--password=***</c>).
    /// Пароль rac НЕ сохраняется на диск и НЕ пишется в журнал приложения.
    /// </summary>
    internal static string MaskRacPassword(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return RacPasswordRegex.Replace(text, match => match.Groups[1].Value + "***");
    }

    /// <summary>
    /// Заменяет точное значение секрета на «***» во всём тексте (для логов): используется
    /// при журналировании команд пользовательских действий (функция 7) — подставленное
    /// значение пароля базы не должно попадать в историю запусков/журнал/уведомления.
    /// Пустой секрет или текст → строка без изменений. Простая подстановка подстроки:
    /// работает и в экранированном виде («pass»/'pass') — замена не зависит от кавычек.
    /// </summary>
    internal static string MaskValue(string? text, string? secret)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(secret))
            return text ?? "";

        return text.Replace(secret, "***", StringComparison.Ordinal);
    }

    /// <summary>
    /// Маскирует пароль хранилища конфигурации в командной строке 1cv8: значение ключа
    /// /ConfigurationRepositoryP "..." заменяется на «***» (и аналогичный -Pwd"…").
    /// Имя ключа и путь в других /ConfigurationRepository*-ключах не искажаются.
    /// Применяется при формировании <see cref="OneCLauncher.DesignerBatchInfo.CommandLine"/>
    /// для всех repository-операций (выгрузка версии, отчёт, захват/отмена захвата,
    /// обновление из хранилища).
    /// </summary>
    internal static string? MaskRepositoryPassword(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return RepositoryPasswordRegex.Replace(text, match =>
            match.Groups[1].Success
                ? match.Groups[1].Value + "***\""
                : "-Pwd\"***\"");
    }
}
