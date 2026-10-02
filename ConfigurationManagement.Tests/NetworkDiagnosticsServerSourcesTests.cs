using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты объединения источников списка серверов окна «Диагностика подключения»
/// (issue #335): клиент-серверные базы списка + сохранённые порты
/// (<see cref="IServerPortsStore"/>) + адрес цели проверки.
/// </summary>
public sealed class NetworkDiagnosticsServerSourcesTests
{
    [Fact]
    public void Merge_DeduplicatesAndSorts_AcrossAllSources()
    {
        var store = new FakePortsStore(new Dictionary<string, ServerPortsSettings>
        {
            ["B"] = new(1540, 1541, 1542),
            ["c"] = ServerPortsSettings.Empty
        });

        var result = NetworkDiagnosticsServerSources.Merge(
            new[] { "b", "a", "B " }, store, "A");

        // «a» повторяется трижды (базы + цель), «b» — дважды (база + хранилище);
        // итог — без дублей, по алфавиту.
        Assert.Equal(new[] { "a", "b", "c" }, result);
    }

    [Fact]
    public void Merge_AllSourcesEmpty_TargetHostStillIncluded()
    {
        var result = NetworkDiagnosticsServerSources.Merge(null, null, "  srv  ");

        Assert.Equal(new[] { "srv" }, result);
    }

    [Fact]
    public void Merge_EmptyTargetHost_Skipped()
    {
        var result = NetworkDiagnosticsServerSources.Merge(new[] { "a" }, null, "   ");

        Assert.Equal(new[] { "a" }, result);
    }

    [Fact]
    public void Merge_BrokenStore_DoesNotThrow()
    {
        var result = NetworkDiagnosticsServerSources.Merge(
            new[] { "a" }, new ThrowingPortsStore(), "srv");

        Assert.Equal(new[] { "a", "srv" }, result);
    }

    private sealed class FakePortsStore : IServerPortsStore
    {
        private readonly IReadOnlyDictionary<string, ServerPortsSettings> _data;

        public FakePortsStore(IReadOnlyDictionary<string, ServerPortsSettings> data) => _data = data;

        public IReadOnlyDictionary<string, ServerPortsSettings> Load() => _data;

        public void Save(string server, ServerPortsSettings ports)
        {
        }
    }

    private sealed class ThrowingPortsStore : IServerPortsStore
    {
        public IReadOnlyDictionary<string, ServerPortsSettings> Load() =>
            throw new InvalidOperationException("boom");

        public void Save(string server, ServerPortsSettings ports)
        {
        }
    }
}