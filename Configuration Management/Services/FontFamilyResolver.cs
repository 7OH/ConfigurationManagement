namespace Configuration_Management.Services;

/// <summary>
/// Чистый хелпер выбора имени семейства шрифта из текста редактируемого поля
/// выбора шрифта (issue #329). Комбобокс настроек переведён в режим
/// IsEditable: пользователь может ввести имя установленного в системе шрифта,
/// которого нет в списке, и оно должно сохраниться как есть — читать нужно
/// Text поля, а не SelectedItem (иначе введённое значение молча сбрасывается
/// на шрифт по умолчанию). Используется WPF- и Avalonia-версиями настроек.
/// </summary>
public static class FontFamilyResolver
{
    /// <summary>
    /// Возвращает имя семейства шрифта из текста поля; пустой/пробельный текст
    /// заменяется на <paramref name="fallback"/>. Ведущие/хвостовые пробелы
    /// обрезаются, регистр сохраняется (имена шрифтов сравниваются без учёта
    /// регистра при применении).
    /// </summary>
    public static string Resolve(string? comboText, string fallback)
        => string.IsNullOrWhiteSpace(comboText) ? fallback : comboText!.Trim();
}