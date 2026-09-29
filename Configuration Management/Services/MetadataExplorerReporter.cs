using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Строка экспорта «Обозревателя метаданных» (этап 4 цикла 0.3.9.132–0.3.9.136):
/// сводка объекта (имя, размер, путь) + детали (синоним, комментарий, счётчики
/// подчинённых, иерархичность). Собирается в <c>MetadataExplorerViewModel.BuildExportRows()</c>
/// из сводок дерева и деталей <see cref="MetadataXmlParser.ReadObjectDetails"/>.
/// <see cref="IsHierarchical"/> — nullable: <c>null</c>, когда детали недоступны
/// (битый XML) и признак неприменим (форматируется как пустая ячейка).
/// </summary>
public sealed record MetadataExportRow
{
    /// <summary>Каталог типа в выгрузке (например <c>Catalog</c>); локализуется через
    /// <see cref="MetadataTypeLocalizer"/> при формировании отчёта.</summary>
    public required string TypeDir { get; init; }

    /// <summary>Имя объекта метаданных.</summary>
    public required string Name { get; init; }

    /// <summary>Синоним объекта (первый item Properties/Synonym); пусто, если не задан.</summary>
    public required string Synonym { get; init; }

    /// <summary>Комментарий к объекту (Properties/Comment); пусто, если не задан.</summary>
    public required string Comment { get; init; }

    /// <summary>Число реквизитов (файлы *.xml в Attributes/).</summary>
    public required int AttributeCount { get; init; }

    /// <summary>Число табличных частей (файлы *.xml в TabularSections/).</summary>
    public required int TabularSectionCount { get; init; }

    /// <summary>Число форм (файлы *.xml в Forms/).</summary>
    public required int FormCount { get; init; }

    /// <summary>Число команд (файлы *.xml в Commands/).</summary>
    public required int CommandCount { get; init; }

    /// <summary>Признак иерархичности/упорядоченной иерархичности; null — неприменимо
    /// (детали не прочитаны). Форматируется «Да»/«Нет»/пусто через ключи локализации.</summary>
    public bool? IsHierarchical { get; init; }

    /// <summary>Число файлов объекта (сводка: 1 для файлового, сумма по подкаталогам).</summary>
    public required int FileCount { get; init; }

    /// <summary>Суммарный размер файлов объекта в байтах.</summary>
    public required long TotalBytes { get; init; }

    /// <summary>Относительный путь объекта в выгрузке (<c>Configuration/<Тип>/<Имя></c>).</summary>
    public required string RelPath { get; init; }
}

/// <summary>
/// Формирование отчёта «Обозревателя метаданных» (0.3.9.135, этап 4): строки CSV
/// (через <see cref="CsvExporter"/>, UTF-8 BOM, разделитель «;») и текстовый отчёт
/// с шапкой (источник/конфигурация/дата — передаётся строкой <paramref name="headerLine"/>,
/// собирается ViewModel) и блочной группировкой по типам метаданных. Чистый класс без UI:
/// локализация — колбэком <c>Func<string,string></c> (образец
/// <see cref="ConfigurationDiffReporter"/>), поэтому покрыт юнит-тестами.
/// </summary>
public static class MetadataExplorerReporter
{
    /// <summary>
    /// Строки CSV-отчёта: заголовок
    /// «Тип;Имя;Синоним;Комментарий;Реквизиты;Табличные части;Формы;Команды;Иерархический;
    /// Файлов;Размер,байт;Путь в выгрузке» и по одной строке на каждый объект
    /// (порядок — как в переданном списке; типы локализуются через
    /// <see cref="MetadataTypeLocalizer"/>). Экранирование спецсимволов — в
    /// <see cref="CsvExporter.Escape"/>.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string?>> BuildCsvRows(
        IReadOnlyList<MetadataExportRow> rows,
        Func<string, string> t)
    {
        var result = new List<IReadOnlyList<string?>> { CsvHeader(t) };

        foreach (var row in rows)
        {
            result.Add(new string?[]
            {
                MetadataTypeLocalizer.GetDisplayName(row.TypeDir, t),
                row.Name,
                row.Synonym,
                row.Comment,
                row.AttributeCount.ToString(),
                row.TabularSectionCount.ToString(),
                row.FormCount.ToString(),
                row.CommandCount.ToString(),
                HierarchicalText(row.IsHierarchical, t),
                row.FileCount.ToString(),
                row.TotalBytes.ToString(),
                row.RelPath
            });
        }

        return result;
    }

    /// <summary>
    /// Текстовый отчёт: шапка (источник: база/.cf, конфигурация и версия, дата
    /// формирования — собирается ViewModel в <paramref name="headerLine"/>) и блочная
    /// группировка объектов по типам («Тип — N»: строки
    /// «Имя | Синоним | реквизитов: X | ТЧ: Y | форм: Z | размер: N»).
    /// </summary>
    public static string BuildText(
        string headerLine,
        IReadOnlyList<MetadataExportRow> rows,
        Func<string, string> t)
    {
        var sb = new StringBuilder();

        sb.AppendLine(headerLine);
        sb.AppendLine();

        var types = MetadataTypeLocalizer.SortTypes(rows.Select(r => r.TypeDir).Distinct());
        foreach (var typeDir in types)
        {
            var ofType = rows.Where(r => r.TypeDir == typeDir).ToList();
            sb.AppendLine(string.Format(
                t("MetadataExplorer.Report.TypeHeaderFormat"),
                MetadataTypeLocalizer.GetDisplayName(typeDir, t),
                ofType.Count));
            foreach (var row in ofType)
            {
                sb.AppendLine(string.Format(
                    t("MetadataExplorer.Report.ObjectLineFormat"),
                    row.Name,
                    row.Synonym,
                    row.AttributeCount,
                    row.TabularSectionCount,
                    row.FormCount,
                    row.TotalBytes));
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Заголовок CSV: колонки через ключи локализации (порядок фиксирован).</summary>
    private static IReadOnlyList<string?> CsvHeader(Func<string, string> t) => new[]
    {
        t("MetadataExplorer.ColumnType"),
        t("MetadataExplorer.ColumnName"),
        t("MetadataExplorer.ColumnSynonym"),
        t("MetadataExplorer.ColumnComment"),
        t("MetadataExplorer.ColumnAttributes"),
        t("MetadataExplorer.ColumnTabularSections"),
        t("MetadataExplorer.ColumnForms"),
        t("MetadataExplorer.ColumnCommands"),
        t("MetadataExplorer.ColumnHierarchical"),
        t("MetadataExplorer.ColumnFiles"),
        t("MetadataExplorer.ColumnSize"),
        t("MetadataExplorer.ColumnRelPath")
    };

    /// <summary>Признак иерархичности: true → «Да», false → «Нет», null → пусто (неприменимо).</summary>
    private static string HierarchicalText(bool? value, Func<string, string> t) => value switch
    {
        null => string.Empty,
        true => t("Common.Yes"),
        false => t("Common.No")
    };
}