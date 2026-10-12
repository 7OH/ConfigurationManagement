using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Согласованность локализации окна «Обозреватель метаданных» (issue #365).
/// <list type="bullet">
/// <item>каждый ключ <c>MetadataExplorer.*</c>, используемый в разметке/коде окна,
/// обязан существовать в словарях ru.json И en.json — иначе интерфейс показывает
/// «голый» ключ вместо текста (как было с ошибкой загрузки из окна сравнения);</item>
/// <item>сервис выгрузки не должен переиспользовать ключи окна сравнения
/// (<c>ConfigDiff.Err*</c>/<c>ConfigDiff.Stage*</c>) — сообщения об ошибках и этапы
/// прогресса обозревателя имеют собственный текст;</item>
/// <item>WPF-окно использует явный <c>ItemTemplate</c> вместо <c>DisplayMemberPath</c>:
/// поле выбора ModernComboBox показывало тип объекта вместо имени базы (приём issue #324).</item>
/// </list>
/// </summary>
public sealed class MetadataExplorerWindowLocalizationTests
{
    /// <summary>Возвращает корень репозитория (каталог с проектами приложения и тестов).</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Configuration Management", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "ConfigurationManagement.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Словари языка: <c>strings</c> из Languages/ru.json и Languages/en.json.</summary>
    private static HashSet<string> LoadLanguageKeys(string fileName)
    {
        var json = ReadProjectFile("Configuration Management", "Localization", "Languages", fileName);
        using var doc = JsonDocument.Parse(json);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.GetProperty("strings").EnumerateObject())
            keys.Add(property.Name);
        return keys;
    }

    /// <summary>Ключи <c>MetadataExplorer.*</c>, упомянутые в файле (XAML и C#).</summary>
    private static HashSet<string> CollectMetadataExplorerKeys(string content)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(content, @"\{loc:Loc\s+([A-Za-z][\w.]*)\}"))
            keys.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(content, "LocalizationManager\\.T\\(\"([A-Za-z][\\w.]*)\"\\)"))
            keys.Add(m.Groups[1].Value);
        keys.RemoveWhere(key => !key.StartsWith("MetadataExplorer.", StringComparison.Ordinal));
        return keys;
    }

    [Fact]
    public void ExplorerWindowKeys_ExistInRuAndEnDictionaries()
    {
        var files = new[]
        {
            Path.Combine("Configuration Management", "Views", "MetadataExplorerWindow.xaml"),
            Path.Combine("Configuration Management", "Views", "MetadataExplorerWindow.xaml.cs"),
            Path.Combine("Configuration Management", "Views", "MetadataExplorerWindow.Avalonia.cs"),
            Path.Combine("Configuration Management", "Views", "MetadataExplorerProgressWindow.cs"),
            Path.Combine("Configuration Management", "Views", "MetadataExplorerProgressWindow.Avalonia.cs"),
            Path.Combine("Configuration Management", "ViewModels", "MetadataExplorerViewModel.cs"),
            Path.Combine("Configuration Management", "Services", "MetadataExplorerService.cs"),
        };

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
            used.UnionWith(CollectMetadataExplorerKeys(ReadProjectFile(file)));

        Assert.NotEmpty(used);

        var ru = LoadLanguageKeys("ru.json");
        var en = LoadLanguageKeys("en.json");

        foreach (var key in used.OrderBy(k => k, StringComparer.Ordinal))
        {
            Assert.True(ru.Contains(key), $"Ключ {key} отсутствует в ru.json (окно обозревателя показало бы ключ вместо текста).");
            Assert.True(en.Contains(key), $"Ключ {key} отсутствует в en.json.");
        }
    }

    [Fact]
    public void ExplorerService_DoesNotReuseComparisonWindowKeys()
    {
        // issue #365: по кнопке «Загрузить» показывалось «Не удалось выполнить операцию
        // сравнения конфигураций» — текст окна сравнения. Сервис обязан использовать
        // только собственные ключи MetadataExplorer.*.
        var service = ReadProjectFile("Configuration Management", "Services", "MetadataExplorerService.cs");

        Assert.DoesNotContain("ConfigDiff.", service);
        Assert.Contains("MetadataExplorer.Errors.LoadFailed", service);
        Assert.Contains("MetadataExplorer.Errors.LoadTimeout", service);
    }

    [Fact]
    public void ExplorerWindowXaml_UsesExplicitItemTemplate_InsteadOfDisplayMemberPath()
    {
        // issue #365: поле выбора ModernComboBox с DisplayMemberPath показывало
        // Configuration_Management.Models.Infobase вместо имени базы — нужен явный ItemTemplate.
        var xaml = ReadProjectFile("Configuration Management", "Views", "MetadataExplorerWindow.xaml");
        xaml = Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        Assert.DoesNotContain("DisplayMemberPath", xaml);
        Assert.Contains("<ComboBox.ItemTemplate>", xaml);
    }

    [Fact]
    public void ExplorerWindowXaml_IsResizableWithStandardMaximizeButton()
    {
        // issue #365: ширина окна = MinWidth (панель источника не обрезается), а у
        // изменяемого окна WindowChromeHelper рисует типовую кнопку максимизации.
        var xaml = ReadProjectFile("Configuration Management", "Views", "MetadataExplorerWindow.xaml");
        xaml = Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
        var windowTag = xaml[..xaml.IndexOf('>')];

        Assert.Contains("ResizeMode=\"CanResize\"", windowTag);

        var minWidth = Regex.Match(windowTag, "MinWidth=\"([0-9.]+)\"");
        Assert.True(minWidth.Success, "MinWidth не задан.");
        Assert.True(double.Parse(minWidth.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= 1140,
            "MinWidth меньше ширины панели источника — кнопка «Загрузить» будет обрезаться.");
    }
}
