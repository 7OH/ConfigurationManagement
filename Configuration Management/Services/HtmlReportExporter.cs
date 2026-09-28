using System.IO;
using System.Text;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Генерация самодостаточного HTML-отчёта по информационным базам (0.3.9.131):
/// стили встроены в документ, внешних зависимостей нет — файл открывается в любом
/// браузере и пригоден для печати/рассылки. Класс чистый .NET (как <see cref="CsvExporter"/>):
/// не зависит от WPF/Avalonia, все подписи приходят готовыми в
/// <see cref="HtmlReportDocument.Labels"/> — используется обеими платформами.
/// </summary>
public static class HtmlReportExporter
{
    /// <summary>Кодировка выходного файла: UTF-8 (без BOM — достаточно для HTML).</summary>
    public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Экранирует текст для встраивания в HTML: заменяет амперсанд, угловые скобки,
    /// двойные и одинарные кавычки на HTML-сущности. Имена баз/групп и подписи —
    /// пользовательский ввод, без экранирования HTML-разметка может сломаться
    /// или стать вектором инъекции.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length + 16);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&':
                    // Сущность собирается из двух фрагментов, чтобы не попадать под
                    // XML-декодирование и остаться в исходнике как &.
                    sb.Append('&');
                    sb.Append("amp;");
                    break;
                case '<':
                    // Сущности собираются из фрагментов (см. case '&').
                    sb.Append('&');
                    sb.Append("lt;");
                    break;
                case '>':
                    sb.Append('&');
                    sb.Append("gt;");
                    break;
                case '"':
                    // Сущность собирается из двух фрагментов, чтобы не попадать под
                    // XML-декодирование и остаться в исходнике как ".
                    sb.Append('&');
                    sb.Append("quot;");
                    break;
                case '\'':
                    sb.Append('&');
                    sb.Append("#39;");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Строит полный HTML-документ: шапка (приложение, дата/время, профиль), сводка,
    /// навигация (якорь «Только проблемы» + ссылки по группам) и таблицы по секциям
    /// групп с подсветкой проблемных строк инлайновыми стилями.
    /// </summary>
    public static string BuildDocument(HtmlReportDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var l = doc.Labels ?? new HtmlReportLabels();
        var sb = new StringBuilder(4096);

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"ru\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\"/>");
        sb.AppendLine($"  <title>{Escape(doc.AppName)} — {Escape(l.Header)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    *{box-sizing:border-box}");
        sb.AppendLine("    body{font-family:\"Segoe UI\",Roboto,Arial,sans-serif;margin:24px;color:#1F2937;background:#F9FAFB}");
        sb.AppendLine("    h1{font-size:22px;margin:0 0 4px}");
        sb.AppendLine("    .meta{color:#6B7280;font-size:13px;margin:0 0 14px}");
        sb.AppendLine("    .nav{margin:0 0 18px;font-size:14px;line-height:26px}");
        sb.AppendLine("    .nav a{color:#2563EB;text-decoration:none;margin-right:14px;white-space:nowrap}");
        sb.AppendLine("    .nav a:hover{text-decoration:underline}");
        sb.AppendLine("    .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px;margin-bottom:20px}");
        sb.AppendLine("    .card{background:#FFFFFF;border:1px solid #E5E7EB;border-radius:8px;padding:10px 14px}");
        sb.AppendLine("    .card .num{font-size:20px;font-weight:600}");
        sb.AppendLine("    .card .lbl{font-size:12px;color:#6B7280}");
        sb.AppendLine("    .card.warn{background:#FEF2F2;border-color:#FCA5A5}");
        sb.AppendLine("    .card.ok{background:#F0FDF4;border-color:#86EFAC}");
        sb.AppendLine("    section{margin-bottom:24px}");
        sb.AppendLine("    h2{font-size:16px;border-bottom:2px solid #E5E7EB;padding-bottom:6px}");
        sb.AppendLine("    table{border-collapse:collapse;width:100%;background:#FFFFFF;font-size:13px}");
        sb.AppendLine("    th,td{border:1px solid #E5E7EB;padding:6px 8px;text-align:left;vertical-align:top}");
        sb.AppendLine("    th{background:#F3F4F6;font-weight:600;white-space:nowrap}");
        sb.AppendLine("    tr.problem td{background-color:#FDE8E8}");
        sb.AppendLine("    .badge{display:inline-block;padding:1px 8px;border-radius:10px;font-size:12px;font-weight:500;white-space:nowrap}");
        sb.AppendLine("    .badge-ok{background:#DCFCE7;color:#15803D}");
        sb.AppendLine("    .badge-bad{background:#FEE2E2;color:#B91C1C}");
        sb.AppendLine("    .legend{font-size:12px;color:#6B7280;margin-bottom:20px}");
        sb.AppendLine("    .only-problems tr.data-row:not(.problem){display:none}");
        sb.AppendLine("    @media print{body{background:#FFFFFF}.nav{display:none}}");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"<h1>{Escape(l.Header)}</h1>");
        sb.AppendLine($"<p class=\"meta\">{Escape(doc.AppName)} · {Escape(doc.GeneratedAtText)} · {Escape(doc.ProfileName)}</p>");

        AppendNavigation(sb, doc, l);
        AppendSummary(sb, doc, l);
        AppendSections(sb, doc, l);

        sb.AppendLine("<script>");
        sb.AppendLine("function toggleProblems(){document.body.classList.toggle('only-problems');}");
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    /// <summary>
    /// Пишет HTML-отчёт в файл (UTF-8 без BOM).
    /// </summary>
    public static void WriteFile(string path, HtmlReportDocument doc)
    {
        var content = BuildDocument(doc);
        File.WriteAllText(path, content, Utf8);
    }

    /// <summary>Навигация: якорь «Только проблемы» + ссылки-якоря по секциям групп.</summary>
    private static void AppendNavigation(StringBuilder sb, HtmlReportDocument doc, HtmlReportLabels l)
    {
        sb.AppendLine("  <div class=\"nav\">");
        sb.AppendLine($"    <a href=\"#summary\">{Escape(l.ShowAll)}</a>");
        sb.AppendLine($"    <a href=\"#\" onclick=\"toggleProblems();return false;\">{Escape(l.OnlyProblems)}</a>");
        if (doc.Rows.Count > 0)
        {
            sb.AppendLine($"    <span>{Escape(l.GroupNav)}</span>");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < doc.Rows.Count; i++)
            {
                var groupKey = GroupSectionKey(doc.Rows[i].GroupPath);
                if (seen.Add(groupKey.Key))
                    sb.AppendLine($"    <a href=\"#{groupKey.Id}\">{Escape(doc.Rows[i].GroupPath)}</a>");
            }
        }
        sb.AppendLine("  </div>");
    }

    /// <summary>Сводка: карточки со значениями по последним известным данным.</summary>
    private static void AppendSummary(StringBuilder sb, HtmlReportDocument doc, HtmlReportLabels l)
    {
        sb.AppendLine("  <div class=\"grid\" id=\"summary\">");
        sb.AppendLine("    <div class=\"card\"><div class=\"num\">" + doc.TotalCount + "</div><div class=\"lbl\">" + Escape(l.SummaryTotal) + "</div></div>");
        sb.AppendLine("    <div class=\"card ok\"><div class=\"num\">" + doc.AvailableCount + "</div><div class=\"lbl\">" + Escape(l.SummaryAvailable) + "</div></div>");
        var warnClass = doc.UnavailableCount > 0 ? " warn" : string.Empty;
        sb.AppendLine("    <div class=\"card" + warnClass + "\"><div class=\"num\">" + doc.UnavailableCount + "</div><div class=\"lbl\">" + Escape(l.SummaryUnavailable) + "</div></div>");
        sb.AppendLine("    <div class=\"card\"><div class=\"num\">" + doc.WithBackupsCount + "</div><div class=\"lbl\">" + Escape(l.SummaryWithBackups) + "</div></div>");
        sb.AppendLine("    <div class=\"card\"><div class=\"num\">" + Escape(FormatSize(doc.TotalSizeBytes)) + "</div><div class=\"lbl\">" + Escape(l.SummarySize) + "</div></div>");
        var problemClass = doc.ProblemCount > 0 ? " warn" : string.Empty;
        sb.AppendLine("    <div class=\"card" + problemClass + "\"><div class=\"num\">" + doc.ProblemCount + "</div><div class=\"lbl\">" + Escape(l.SummaryProblems) + "</div></div>");
        sb.AppendLine("  </div>");
    }

    /// <summary>Таблицы по секциям групп; проблемные строки подсвечены инлайновым стилем.</summary>
    private static void AppendSections(StringBuilder sb, HtmlReportDocument doc, HtmlReportLabels l)
    {
        if (doc.Rows.Count == 0)
        {
            sb.AppendLine("  <p>" + Escape(l.SummaryTotal) + "</p>");
            return;
        }

        // Группируем строки по группе с сохранением порядка появления.
        var order = new List<(string Key, string Id, string Title, List<HtmlReportRow> Rows)>();
        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in doc.Rows)
        {
            var (key, id) = GroupSectionKey(row.GroupPath);
            if (indexByKey.TryGetValue(key, out var idx))
            {
                order[idx].Rows.Add(row);
            }
            else
            {
                indexByKey[key] = order.Count;
                order.Add((key, id, row.GroupPath, new List<HtmlReportRow> { row }));
            }
        }

        foreach (var section in order)
        {
            sb.AppendLine($"  <section id=\"{section.Id}\">");
            sb.AppendLine($"    <h2>{Escape(section.Title)} ({section.Rows.Count})</h2>");
            sb.AppendLine("    <table>");
            sb.AppendLine("      <thead><tr>");
            AppendHeaderCell(sb, l.ColName);
            AppendHeaderCell(sb, l.ColGroup);
            AppendHeaderCell(sb, l.ColType);
            AppendHeaderCell(sb, l.ColConnection);
            AppendHeaderCell(sb, l.ColAvailability);
            AppendHeaderCell(sb, l.ColConfiguration);
            AppendHeaderCell(sb, l.ColSize);
            AppendHeaderCell(sb, l.ColLastBackup);
            AppendHeaderCell(sb, l.ColModified);
            AppendHeaderCell(sb, l.ColTags);
            AppendHeaderCell(sb, l.ColFavorite);
            sb.AppendLine("      </tr></thead>");
            sb.AppendLine("      <tbody>");
            foreach (var row in section.Rows)
                AppendRow(sb, row, l);
            sb.AppendLine("      </tbody>");
            sb.AppendLine("    </table>");
            sb.AppendLine("  </section>");
        }

        sb.AppendLine($"  <p class=\"legend\">{Escape(l.Legend)}</p>");
    }

    private static void AppendHeaderCell(StringBuilder sb, string label)
    {
        sb.AppendLine("        <th>" + Escape(label) + "</th>");
    }

    private static void AppendRow(StringBuilder sb, HtmlReportRow row, HtmlReportLabels l)
    {
        var problemStyle = row.HasProblem ? " style=\"background-color:#FDE8E8\"" : string.Empty;
        sb.AppendLine("        <tr class=\"data-row" + (row.HasProblem ? " problem" : string.Empty) + "\"" + problemStyle + ">");
        AppendCell(sb, row.Name);
        AppendCell(sb, row.GroupPath);
        AppendCell(sb, row.Type);
        AppendCell(sb, row.ConnectionString);
        sb.Append("        <td>");
        sb.Append(row.IsAvailable
            ? "<span class=\"badge badge-ok\">" + Escape(l.Available) + "</span>"
            : "<span class=\"badge badge-bad\">" + Escape(l.Unavailable) + "</span>");
        if (!string.IsNullOrWhiteSpace(row.AvailabilityText))
            sb.Append(" " + Escape(row.AvailabilityText));
        sb.AppendLine("</td>");
        AppendCell(sb, row.Configuration);
        AppendCell(sb, row.Size);
        AppendCell(sb, row.LastBackup);
        AppendCell(sb, row.Modified);
        AppendCell(sb, row.Tags);
        AppendCell(sb, row.Favorite);
        sb.AppendLine("        </tr>");
    }

    private static void AppendCell(StringBuilder sb, string value)
    {
        sb.AppendLine("        <td>" + Escape(value) + "</td>");
    }

    /// <summary>Стабильный id секции группы: «g-<n>» по порядку первой встречи группы.</summary>
    private static (string Key, string Id) GroupSectionKey(string groupPath)
    {
        var key = string.IsNullOrWhiteSpace(groupPath) ? string.Empty : groupPath.Trim();
        // Хэш по имени группы (без учёта регистра) — id не содержит спецсимволов,
        // при этом одинаковые группы дают одинаковые id в разных запусках.
        var hash = Math.Abs(string.GetHashCode(key, StringComparison.OrdinalIgnoreCase)) % 1000000;
        return (key, $"g-{hash}");
    }

    /// <summary>Форматирует суммарный размер ИБ; 0 (неизвестен) — «—».</summary>
    private static string FormatSize(long bytes)
    {
        return bytes <= 0 ? "—" : Infobase.FormatSize(bytes);
    }
}