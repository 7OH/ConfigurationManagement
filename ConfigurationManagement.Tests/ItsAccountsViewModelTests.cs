using System;
using System.IO;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты списка учётных записей ИТС (issue #333): после удаления строки коллекция
/// <see cref="ItsAccountsViewModel.Rows"/> пересобирается сразу, а не только после
/// перезапуска приложения.
/// </summary>
public sealed class ItsAccountsViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public ItsAccountsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_its_vm_tests_" + Guid.NewGuid().ToString("N"));
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

    private ItsAccountsStore CreateStore() => new(directoryOverride: _tempDir);

    [Fact]
    public void Add_ThenDelete_RowsAreRefreshedImmediately()
    {
        var store = CreateStore();
        var vm = new ItsAccountsViewModel(store);

        var created = new ItsAccount { Name = "Аккаунт ИТС", Login = "user@its", Password = "pwd" };
        vm.Add(created);

        Assert.Single(vm.Rows);
        Assert.NotEmpty(created.Id); // хранилище назначает идентификатор

        // Удаляем — строка должна исчезнуть сразу (до фикса оставалась до перезапуска).
        vm.Delete(created.Id);

        Assert.Empty(vm.Rows);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Delete_PrimaryEntry_RowsShowNewPrimary()
    {
        var store = CreateStore();
        var vm = new ItsAccountsViewModel(store);

        var first = new ItsAccount { Name = "Первый", Login = "a@its" };
        var second = new ItsAccount { Name = "Второй", Login = "b@its" };
        vm.Add(first);
        vm.Add(second);

        // Основная после добавления первой записи — первая.
        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);

        vm.Delete(first.Id);

        // Остался один элемент, и он теперь основной (правило «0 основных → первая»).
        var row = Assert.Single(vm.Rows);
        Assert.Equal(second.Id, row.Id);
        Assert.True(row.IsPrimary);
    }
}