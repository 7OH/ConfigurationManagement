using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистого экспорта в CSV (RFC 4180): экранирование полей, разделитель «;»
/// и запись файла в UTF-8 с BOM (иначе Excel не распознаёт кириллицу).
/// </summary>
public sealed class CsvExporterTests
{
    [Fact]
    public void Escape_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CsvExporter.Escape(null));
        Assert.Equal(string.Empty, CsvExporter.Escape(string.Empty));
    }

    [Fact]
    public void Escape_PlainText_Unchanged()
    {
        Assert.Equal("База", CsvExporter.Escape("База"));
        Assert.Equal("1C:Предприятие", CsvExporter.Escape("1C:Предприятие"));
    }

    [Fact]
    public void Escape_FieldWithSeparator_Quoted()
    {
        Assert.Equal("\"Имя;особое\"", CsvExporter.Escape("Имя;особое"));
    }

    [Fact]
    public void Escape_FieldWithQuotes_DoublesInnerQuotes()
    {
        Assert.Equal("\"say \"\"hi\"\"\"", CsvExporter.Escape("say \"hi\""));
    }

    [Fact]
    public void Escape_FieldWithNewLine_QuotedAndKeepsLineBreak()
    {
        Assert.Equal("\"a\nb\"", CsvExporter.Escape("a\nb"));
        Assert.Equal("\"a\r\nb\"", CsvExporter.Escape("a\r\nb"));
    }

    [Fact]
    public void JoinRow_SeparatesEscapedFields()
    {
        var row = CsvExporter.JoinRow(new string?[] { "Имя", "группа;1", "нет" });
        Assert.Equal("Имя;\"группа;1\";нет", row);
    }

    [Fact]
    public void BuildDocument_UsesCrLf()
    {
        var doc = CsvExporter.BuildDocument(new[]
        {
            new[] { "a", "b" },
            new[] { "c", "d" }
        });
        Assert.Equal("a;b\r\nc;d\r\n", doc);
    }

    [Fact]
    public void WriteFile_WritesUtf8WithBom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"csv-{Guid.NewGuid():N}.csv");
        try
        {
            CsvExporter.WriteFile(path, new[] { new[] { "Имя", "Значение" } });

            var bytes = File.ReadAllBytes(path);
            // BOM UTF-8: EF BB BF — без него Excel не распознаёт кириллицу.
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

            var text = File.ReadAllText(path, CsvExporter.Utf8WithBom);
            Assert.Contains("Имя", text);
            Assert.Contains("Значение", text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}