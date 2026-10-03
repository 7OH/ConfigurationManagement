using System;
using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты построителя списка выбора учётной записи ИТС (issue #333/#322): дедупликация
/// пункта «Основная» — виртуальный пункт (Id == null) добавляется только если в
/// справочнике нет реальной записи с именем <see cref="ItsAccountsStore.PrimaryName"/>;
/// сравнение имён регистронезависимо; при дублях записей «Основная» в файле (ручная
/// правка) в списке выбора остаётся первая из них.
/// </summary>
public sealed class ItsAccountSelectionBuilderTests : IDisposable
{
    private readonly string _tempDir;

    public ItsAccountSelectionBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_its_selection_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Игнорируем: каталог мог быть занят или уже удалён.
        }
    }

    private static ItsAccount Sample(string name, string login, bool primary = false) => new()
    {
        Name = name,
        Login = login,
        Password = "pwd-" + login,
        IsPrimary = primary,
    };

    private ItsAccountsStore CreateStore() => new(directoryOverride: _tempDir);

    [Fact]
    public void Build_StoreHasPrimaryNamedEntry_NoVirtualItem()
    {
        // Мигрированные старые настройки дают реальную запись «Основная» (PrimaryName).
        var store = CreateStore();
        store.Save(new[]
        {
            Sample(ItsAccountsStore.PrimaryName, "main@its", primary: true),
            Sample("Дополнительная", "extra@its"),
        });

        var items = ItsAccountSelectionBuilder.Build(store);

        // Виртуального пункта (Id == null) нет — реальная «Основная» уже в списке.
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, i => i.Id is null);
        Assert.Single(items, i => i.Name == ItsAccountsStore.PrimaryName);
        Assert.Equal("main@its", store.GetPrimary()?.Login);
    }

    [Fact]
    public void Build_NoPrimaryNamedEntry_AddsVirtualItemFirst()
    {
        var store = CreateStore();
        store.Save(new[]
        {
            Sample("Бухгалтерия", "acc@its", primary: true),
            Sample("ЗУП", "zup@its"),
        });

        var items = ItsAccountSelectionBuilder.Build(store);

        // Записи «Основная» нет → виртуальный пункт первый, всего N+1.
        Assert.Equal(3, items.Count);
        Assert.Null(items[0].Id);
        Assert.Equal("Бухгалтерия", items[1].Name);
        Assert.Equal("ЗУП", items[2].Name);
    }

    [Fact]
    public void Build_PrimaryNameComparison_IsCaseInsensitive()
    {
        var store = CreateStore();
        // Ручная правка файла/другая раскладка: имя в другом регистре не создаёт дубль.
        store.Save(new[]
        {
            Sample("ОСНОВНАЯ", "main@its", primary: true),
        });

        var items = ItsAccountSelectionBuilder.Build(store);

        Assert.Single(items);
        Assert.NotNull(items[0].Id); // реальная запись, виртуальный пункт не добавлен
        Assert.Equal("ОСНОВНАЯ", items[0].Name);
    }

    [Fact]
    public void Build_DuplicatePrimaryNamedEntries_KeepsFirstOnly()
    {
        var store = CreateStore();
        var first = Sample(ItsAccountsStore.PrimaryName, "first@its", primary: true);
        var second = Sample(ItsAccountsStore.PrimaryName, "second@its");
        store.Save(new[] { first, second, Sample("Дополнительная", "extra@its") });

        var items = ItsAccountSelectionBuilder.Build(store);

        // Двух записей «Основная» в файле быть не должно; в списке выбора — только первая.
        Assert.Equal(2, items.Count);
        Assert.Single(items, i => i.Name == ItsAccountsStore.PrimaryName);
        Assert.Contains(items, i => i.Id == first.Id);
        Assert.DoesNotContain(items, i => i.Id == second.Id);
        Assert.DoesNotContain(items, i => i.Id is null);
    }

    [Fact]
    public void IndexOf_KnownAccountId_FindsRealEntry()
    {
        var store = CreateStore();
        var extra = Sample("Дополнительная", "extra@its");
        store.Save(new[]
        {
            Sample(ItsAccountsStore.PrimaryName, "main@its", primary: true),
            extra,
        });

        var items = ItsAccountSelectionBuilder.Build(store);
        var index = ItsAccountSelectionBuilder.IndexOf(items, extra.Id);

        Assert.Equal(1, index);
        Assert.Equal(extra.Id, items[index].Id);
    }

    [Fact]
    public void IndexOf_UnknownOrEmptyId_FallsBackToFirstItem()
    {
        var store = CreateStore();
        store.Save(new[] { Sample(ItsAccountsStore.PrimaryName, "main@its", primary: true) });

        var items = ItsAccountSelectionBuilder.Build(store);

        Assert.Equal(0, ItsAccountSelectionBuilder.IndexOf(items, string.Empty));
        Assert.Equal(0, ItsAccountSelectionBuilder.IndexOf(items, "несуществующий-id"));
        Assert.Equal(0, ItsAccountSelectionBuilder.IndexOf(items, null));
    }

    [Fact]
    public void Build_EmptyName_ShowsPlaceholderInsteadOfBlank()
    {
        // Пустое наименование записи не должно превращаться в «невидимый» пункт
        // выпадающего списка (issue #333): подставляем нейтральный плейсхолдер.
        var store = CreateStore();
        var blank = new ItsAccount { Id = "acc-blank", Name = "   ", Login = "blank@its" };
        store.Save(new[] { blank });

        var items = ItsAccountSelectionBuilder.Build(store);

        var item = Assert.Single(items, i => i.Id == blank.Id);
        Assert.False(string.IsNullOrWhiteSpace(item.Name));
        Assert.DoesNotContain(items, i => i.Id == blank.Id && i.Name is null);
    }

    [Fact]
    public void Build_VirtualPrimaryItem_HasDisplayName()
    {
        // Виртуальный пункт «Основная» (Id == null) должен иметь непустое отображаемое
        // имя: ComboBox выбора использует DisplayMemberPath=Name, пустой пункт «не читался»
        // бы как отсутствие (issue #333).
        var store = CreateStore();
        store.Save(new[] { Sample("Бухгалтерия", "acc@its", primary: true) });

        var items = ItsAccountSelectionBuilder.Build(store);

        var primary = Assert.Single(items, i => i.Id is null);
        Assert.False(string.IsNullOrWhiteSpace(primary.Name));
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Name)));
    }

    [Fact]
    public void Build_AllItems_HaveDisplayNamesForComboBox()
    {
        // Отображение в ComboBox строится по Name — ни один пункт (реальные записи
        // и виртуальный) не должен оставаться без имени (issue #333).
        var store = CreateStore();
        store.Save(new[]
        {
            Sample("Бухгалтерия", "acc@its", primary: true),
            Sample("   ", "blank@its"),
            new ItsAccount { Id = "null-name", Name = null!, Login = "n@its" },
        });

        var items = ItsAccountSelectionBuilder.Build(store);

        Assert.Equal(4, items.Count); // «Основная» (виртуальная) + 3 записи
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Name)));
    }

    [Fact]
    public void SelectionItem_ToString_ReturnsDisplayName()
    {
        // Страховка отображения (issue #333): даже если в конкретном ComboBox пропущен
        // DisplayMemberPath, показывается имя пункта, а не тип/идентификатор.
        var named = new ItsAccountSelectionItem("acc-1", "Бухгалтерия");
        Assert.Equal("Бухгалтерия", named.ToString());

        var primary = new ItsAccountSelectionItem(null, "Основная");
        Assert.Equal("Основная", primary.ToString());
    }
}