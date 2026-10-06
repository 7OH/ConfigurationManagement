using System;
using System.Runtime.CompilerServices;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Глобальная подготовка тестового процесса (выполняется один раз при загрузке сборки,
/// до всех тестов). С 0.3.9.315 INFO-диагностика редиректов/входа портала 1С пишется только
/// при включённом флаге CM_REDIRECT в trace.json (issue #347); тесты входа полагаются на эти
/// записи — включаем флаг env-переменной процесса. Env-переменная — только override
/// включения (см. <see cref="Configuration_Management.Services.TraceFlags"/>), файлового I/O
/// при этом не происходит, поэтому на остальные тесты влияния нет.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Init()
    {
        try { Environment.SetEnvironmentVariable("CM_REDIRECT", "1"); }
        catch { /* env недоступен — часть тестов входа потеряет INFO-записи */ }
    }
}