using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистый парсер текстового отчёта по истории хранилища конфигурации
/// («Обозреватель хранилища конфигурации», 0.3.9.127). Разбирает вывод
/// /ConfigurationRepositoryReport в текстовом виде в список <see cref="RepositoryVersion"/>.
///
/// РАЗВЕДКА ЭТАПА 1 (2026-09-28, платформа 8.3.27.2325): грамматика ключей
/// /ConfigurationRepositoryF/N/P + /ConfigurationRepositoryReport подтверждена на реальной
/// платформе (ошибка «Хранилище … не обнаружено» — человекочитаемый текст в логе /Out,
/// ExitCode = 0). Однако фактический формат файла отчёта проверить не удалось: создание
/// файлового хранилища на этой машине блокируется политикой записи (коды 1AC5-5074/0E2D-1589),
/// а по документации (Appendix 7 руководства администратора 8.3.24) отчёт формируется в виде
/// табличного документа (.mxl); текстовые форматы (.txt/.html) не документированы.
/// Поэтому парсер носит ЗАПАСНОЙ характер (устойчивый, документированный ниже формат строк),
/// а при недоступности читаемого текста сервис ограничивает историю актуальной версией.
///
/// Формат строк отчёта (ожидаемый TAB-разделитель, как при экспорте табличного документа):
///   №версии [TAB] дата время [TAB] автор [TAB] комментарий…
/// Запасной вариант без табуляций — разбиение по пробелам (комментарий склеивается обратно).
/// Устойчивость: заголовочные/служебные строки и «кривые» строки (первое поле — не число)
/// пропускаются; пустые поля принимают значение по умолчанию (default(DateTime), пустая
/// строка); актуальной помечается версия с наибольшим номером (отчёт может быть отсортирован
/// как по возрастанию, так и по убыванию номеров).
/// </summary>
public static class RepositoryHistoryParser
{
    /// <summary>
    /// Разбирает текст отчёта по истории хранилища. Пустой/нечитаемый текст даёт пустой список
    /// (сервис тогда возвращает одну запись «актуальная версия», см.
    /// <c>RepositoryStorageService.GetHistoryAsync</c>).
    /// </summary>
    public static IReadOnlyList<RepositoryVersion> ParseReport(string? text)
    {
        var versions = new List<RepositoryVersion>();
        if (string.IsNullOrWhiteSpace(text))
            return versions;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim('\r', '\t', ' ');
            if (line.Length == 0)
                continue;

            var fields = SplitFields(line);
            if (fields.Length == 0)
                continue;

            // Первое поле обязано быть целым номером версии: заголовки отчёта («Версия»,
            // название периода, шапка таблицы и т.п.) не проходят — «кривые» строки пропускаются.
            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                continue;

            // Дата может быть в локальном или инвариантном формате («28.09.2026 19:00:00»,
            // «28.09.2026»); при неудаче остаётся default(DateTime) — пустое поле.
            // При разбиении по пробелам (запасной вариант) время занимает отдельный токен —
            // «2026-09-28 19:00:00» раскалывается на два: склеиваем токен времени обратно.
            var dateText = fields.Length > 1 ? fields[1] : string.Empty;
            var authorIndex = 2; // поле сразу после даты
            if (fields.Length > 2 && dateText.IndexOf(':') < 0 && fields[2].IndexOf(':') >= 0)
            {
                dateText += " " + fields[2];
                authorIndex = 3;
            }

            DateTime date = default;
            if (!string.IsNullOrWhiteSpace(dateText) &&
                !DateTime.TryParse(dateText, CultureInfo.CurrentCulture, DateTimeStyles.None, out date) &&
                !DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                date = default;
            }

            // Автор — токен сразу после даты; комментарий (может содержать пробелы) — остаток
            // (пустые поля отбрасываются, чтобы не появлялись лишние пробелы при склейке).
            var author = fields.Length > authorIndex ? fields[authorIndex] : string.Empty;
            var comment = fields.Length > authorIndex + 1
                ? string.Join(" ", fields.Skip(authorIndex + 1).Where(f => f.Length > 0))
                : string.Empty;

            versions.Add(new RepositoryVersion(number, date, author, comment, IsCurrent: false));
        }

        if (versions.Count > 0)
        {
            // Актуальной считаем версию с наибольшим номером — независимо от порядка сортировки отчёта.
            var maxNumber = versions.Max(v => v.Number);
            var index = versions.FindIndex(v => v.Number == maxNumber);
            if (index >= 0)
                versions[index] = versions[index] with { IsCurrent = true };
        }

        return versions;
    }

    /// <summary>
    /// Разбивает строку отчёта на поля: предпочтительно по табуляциям (экспорт табличного
    /// документа) — пустые поля СОХРАНЯЮТСЯ (пустая дата/автор → значения по умолчанию);
    /// если табуляций нет — по последовательностям пробелов (запасной вариант; комментарий
    /// с пробелами при этом разобьётся и будет склеен обратно в ParseReport).
    /// </summary>
    private static string[] SplitFields(string line)
    {
        if (line.IndexOf('\t') >= 0)
            return line.Split('\t', StringSplitOptions.TrimEntries);
        return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}