using System.Collections.ObjectModel;
using System.Linq;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Узел «тип метаданных» дерева отчёта: локализованное имя и объекты типа.</summary>
public sealed class ConfigDiffTypeNodeViewModel
{
    public ConfigDiffTypeNodeViewModel(string displayName, IEnumerable<MetadataObject> objects)
    {
        DisplayName = displayName;
        foreach (var obj in objects)
            Objects.Add(new ConfigDiffObjectItemViewModel(obj));
    }

    /// <summary>Локализованное имя типа (например «Документ»).</summary>
    public string DisplayName { get; }

    /// <summary>Число объектов данного типа в отчёте.</summary>
    public int Count => Objects.Count;

    /// <summary>Заголовок узла дерева: «Имя типа (N)».</summary>
    public string Header => $"{DisplayName} ({Count})";

    /// <summary>Объекты типа в порядке отчёта.</summary>
    public ObservableCollection<ConfigDiffObjectItemViewModel> Objects { get; } = new();
}

/// <summary>Строка отчёта об одном объекте метаданных.</summary>
public sealed class ConfigDiffObjectItemViewModel
{
    public ConfigDiffObjectItemViewModel(MetadataObject obj)
    {
        Name = obj.Name;
        Kind = obj.Kind;
        FileCount = obj.FileCount;
        TotalBytes = obj.TotalBytes;
        StatusText = LocalizationManager.T(StatusKey(obj.Kind));
    }

    /// <summary>Имя объекта метаданных.</summary>
    public string Name { get; }

    /// <summary>Статус сравнения (для раскраски строк в окне).</summary>
    public DiffChangeKind Kind { get; }

    /// <summary>Локализованный статус («Добавлен», «Изменён», «Удалён», «Без изменений»).</summary>
    public string StatusText { get; }

    /// <summary>Число файлов объекта в выгрузке.</summary>
    public int FileCount { get; }

    /// <summary>Суммарный размер объекта в байтах.</summary>
    public long TotalBytes { get; }

    /// <summary>Отображение размера: целое число байт (для CSV — то же значение).</summary>
    public string SizeText => TotalBytes.ToString();

    private static string StatusKey(DiffChangeKind kind) => kind switch
    {
        DiffChangeKind.Added => "ConfigDiff.StatusAdded",
        DiffChangeKind.Changed => "ConfigDiff.StatusChanged",
        DiffChangeKind.Removed => "ConfigDiff.StatusRemoved",
        _ => "ConfigDiff.StatusUnchanged"
    };
}

/// <summary>
/// Чистая ViewModel окна результата сравнения конфигураций (0.3.9.99): дерево
/// «Тип → объекты», сводка, шапка и подготовка данных экспорта CSV/TXT.
/// Чистый .NET без WPF/Avalonia-зависимостей — используется обеими платформами.
/// </summary>
public sealed class ConfigDiffResultViewModel : ViewModelBase
{
    private readonly ConfigurationDiffResult _result;

    public ConfigDiffResultViewModel(ConfigurationDiffResult result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        BuildTypes();
    }

    /// <summary>Узлы «тип → объекты» в фиксированном порядке типов (неизвестные — в конце).</summary>
    public ObservableCollection<ConfigDiffTypeNodeViewModel> Types { get; } = new();

    /// <summary>Шапка: что с чем сравнивали («Левая сторона ↔ Правая сторона»).</summary>
    public string HeaderText => $"{_result.LeftLabel} \u2194 {_result.RightLabel}";

    /// <summary>Сводка «Добавлено: X · Изменено: Y · Удалено: Z · Без изменений: N».</summary>
    public string SummaryText => string.Format(
        LocalizationManager.T("ConfigDiff.SummaryFormat"),
        _result.AddedCount,
        _result.ChangedCount,
        _result.RemovedCount,
        _result.UnchangedCount);

    /// <summary>Затраченное время на операцию.</summary>
    public string ElapsedText => string.Format(
        LocalizationManager.T("ConfigDiff.ElapsedFormat"),
        FormatElapsed(_result.Elapsed));

    /// <summary>Флаг «конфигурация в целом изменена/не изменена» (корневой Configuration.xml).</summary>
    public string RootChangedText => LocalizationManager.T(
        _result.RootFileChanged ? "ConfigDiff.RootChanged" : "ConfigDiff.RootUnchanged");

    /// <summary>Есть ли вообще отличия (для подсказки «отличий не найдено»).</summary>
    public bool HasDifferences => _result.AddedCount + _result.ChangedCount + _result.RemovedCount > 0;

    /// <summary>Строки CSV-экспорта (заголовок + объекты).</summary>
    public IReadOnlyList<IReadOnlyList<string?>> BuildCsvRows()
        => ConfigurationDiffReporter.BuildCsvRows(_result, LocalizationManager.T);

    /// <summary>Текстовый отчёт для экспорта TXT.</summary>
    public string BuildTextReport()
        => ConfigurationDiffReporter.BuildText(_result, LocalizationManager.T);

    private void BuildTypes()
    {
        var typeDirs = MetadataTypeLocalizer.SortTypes(_result.Objects.Select(o => o.TypeDir).Distinct());
        foreach (var typeDir in typeDirs)
        {
            var displayName = MetadataTypeLocalizer.GetDisplayName(typeDir, LocalizationManager.T);
            Types.Add(new ConfigDiffTypeNodeViewModel(
                displayName,
                _result.Objects.Where(o => o.TypeDir == typeDir)));
        }
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