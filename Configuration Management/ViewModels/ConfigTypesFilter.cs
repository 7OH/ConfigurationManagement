using System;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Чистый фильтр строк списка типовых конфигураций (issue #321, часть 2): подстрока
/// без учёта регистра по наименованию, коду, сегменту адреса, нику на releases.1c.ru
/// и сводке редакций. Пустой запрос пропускает все строки. Без платформенных
/// зависимостей — используется окнами WPF и Avalonia и покрывается юнит-тестами.
/// </summary>
public static class ConfigTypesFilter
{
    /// <summary>
    /// Проверяет, проходит ли строка <paramref name="row"/> под текстовый запрос
    /// <paramref name="query"/>. Пустой/пробельный запрос совпадает с любой строкой.
    /// </summary>
    public static bool Matches(ConfigTypeItemViewModel row, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var q = query.Trim();
        return Contains(row.Name, q)
            || Contains(row.Code, q)
            || Contains(row.UrlCode, q)
            || Contains(row.Nick, q)
            || Contains(row.EditionsSummary, q);
    }

    private static bool Contains(string? value, string query) =>
        value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}