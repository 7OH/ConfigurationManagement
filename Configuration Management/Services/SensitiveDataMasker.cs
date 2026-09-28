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
}
