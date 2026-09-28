using System.Linq;
using System.Text;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Формирование отчёта о сравнении конфигураций (0.3.9.99): строки CSV
/// (через <see cref="CsvExporter"/>, UTF-8 BOM, разделитель «;») и текстовый
/// отчёт с блочной группировкой по типам метаданных. Чистый класс без UI:
/// локализация — колбэком <c>Func<string,string></c> (см.
/// <see cref="MetadataTypeLocalizer"/>), поэтому покрыт юнит-тестами.
/// </summary>
public static class ConfigurationDiffReporter
{
    private static readonly string[] StatusKeys =
    {
        "ConfigDiff.StatusAdded",
        "ConfigDiff.StatusChanged",
        "ConfigDiff.StatusRemoved",
        "ConfigDiff.StatusUnchanged"
    };

    private static string StatusText(DiffChangeKind kind, Func<string, string> t)
        => t(StatusKeys[(int)kind]);

    /// <summary>
    /// Строки CSV-отчёта: заголовок «Тип;Имя;Статус;Файлов;Размер,байт» и по одной
    /// строке на каждый объект (все статусы — полная картина сравнения).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string?>> BuildCsvRows(
        ConfigurationDiffResult result,
        Func<string, string> t)
    {
        var rows = new List<IReadOnlyList<string?>>();
        rows.Add(new[]
        {
            t("ConfigDiff.ColumnType"),
            t("ConfigDiff.ColumnName"),
            t("ConfigDiff.ColumnStatus"),
            t("ConfigDiff.ColumnFiles"),
            t("ConfigDiff.ColumnSize")
        });

        foreach (var obj in result.Objects)
        {
            rows.Add(new string?[]
            {
                MetadataTypeLocalizer.GetDisplayName(obj.TypeDir, t),
                obj.Name,
                StatusText(obj.Kind, t),
                obj.FileCount.ToString(),
                obj.TotalBytes.ToString()
            });
        }

        return rows;
    }

    /// <summary>
    /// Текстовый отчёт: заголовок (что с чем сравнивали, время, флаг «конфигурация
    /// в целом изменена»), сводка и блочная группировка объектов по типам
    /// («Тип — N»: строки «Имя | Статус | Файлов | Размер»).
    /// </summary>
    public static string BuildText(
        ConfigurationDiffResult result,
        Func<string, string> t)
    {
        var sb = new StringBuilder();

        sb.AppendLine(result.LeftLabel + " \u2194 " + result.RightLabel);
        sb.AppendLine(string.Format(t("ConfigDiff.ElapsedFormat"), FormatElapsed(result.Elapsed)));
        sb.AppendLine(t(result.RootFileChanged ? "ConfigDiff.RootChanged" : "ConfigDiff.RootUnchanged"));
        sb.AppendLine();
        sb.AppendLine(string.Format(
            t("ConfigDiff.SummaryFormat"),
            result.AddedCount,
            result.ChangedCount,
            result.RemovedCount,
            result.UnchangedCount));
        sb.AppendLine();

        var types = MetadataTypeLocalizer.SortTypes(result.Objects.Select(o => o.TypeDir).Distinct());
        foreach (var typeDir in types)
        {
            var ofType = result.Objects.Where(o => o.TypeDir == typeDir).ToList();
            sb.AppendLine(string.Format(
                t("ConfigDiff.TypeHeaderFormat"),
                MetadataTypeLocalizer.GetDisplayName(typeDir, t),
                ofType.Count));
            foreach (var obj in ofType)
            {
                sb.AppendLine(string.Format(
                    t("ConfigDiff.ObjectLineFormat"),
                    obj.Name,
                    StatusText(obj.Kind, t),
                    obj.FileCount,
                    obj.TotalBytes));
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 60)
            return Math.Max(1, (int)elapsed.TotalSeconds) + " \u0441";
        var minutes = (int)elapsed.TotalMinutes;
        var seconds = elapsed.Seconds;
        return $"{minutes} \u043c\u0438\u043d {seconds} \u0441";
    }
}