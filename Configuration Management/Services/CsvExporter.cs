using System.IO;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Чистый экспорт табличных данных в CSV (RFC 4180) для открытия в Excel.
/// Разделитель «;» — стандарт для русской локали Excel; файл пишется в UTF-8
/// с BOM, иначе Excel не распознаёт кириллицу. Класс не зависит от WPF/Avalonia
/// и используется обеими платформами.
/// </summary>
public static class CsvExporter
{
    /// <summary>Разделитель полей: точка с запятой (стандарт русской локали Excel).</summary>
    public const char Separator = ';';

    /// <summary>Кодировка выходного файла: UTF-8 с BOM.</summary>
    public static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Экранирует одно поле по правилам RFC 4180: если поле содержит разделитель,
    /// двойную кавычку или перевод строки — оно заключается в двойные кавычки,
    /// а внутренние кавычки удваиваются. Прочие поля возвращаются как есть.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (!value.Contains(Separator) && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
            return value;

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            if (ch == '"')
                sb.Append("\"\"");
            else
                sb.Append(ch);
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Собирает одну строку CSV из полей (каждое поле экранируется).</summary>
    public static string JoinRow(IEnumerable<string?> fields)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var field in fields)
        {
            if (!first)
                sb.Append(Separator);
            sb.Append(Escape(field));
            first = false;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Формирует полное содержимое CSV-документа: строки разделяются CRLF
    /// (требование RFC 4180), в конце файла — перевод строки.
    /// </summary>
    public static string BuildDocument(IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.Append(JoinRow(row));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Пишет CSV-файл в UTF-8 с BOM (иначе Excel не распознаёт кириллицу).
    /// Строки разделяются CRLF по RFC 4180.
    /// </summary>
    public static void WriteFile(string path, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var content = BuildDocument(rows);
        File.WriteAllText(path, content, Utf8WithBom);
    }
}