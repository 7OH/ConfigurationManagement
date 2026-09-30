using System.Reflection;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой подстановки параметров сценариев запуска скриптов
/// (<see cref="ScriptParameterResolver"/>, issue #308): %name%, вложенные свойства
/// подключения через точку (включая пароль), динамические ключи рефлексией,
/// дата в пользовательском формате, поведение неизвестного ключа и построение
/// примера командной строки.
/// </summary>
public sealed class ScriptParameterResolverTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 28, 14, 5, 7);

    private static Infobase MakeInfobase()
    {
        var ib = new Infobase { Name = "Бухгалтерия" };
        ib.Connection.Type = ConnectionType.ClientServer;
        ib.Connection.Server = "srv-1c";
        ib.Connection.DatabaseName = "acc_main";
        ib.Connection.Port = 1541;
        ib.Connection.Password = "secret-pass";
        return ib;
    }

    private static Dictionary<string, string> Map(Infobase? ib = null) =>
        ScriptParameterResolver.BuildValueMap(ib);

    // ------------------- %name% и вложенные свойства подключения -------------------

    [Fact]
    public void Resolve_Name_SubstitutesBaseName()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("База: %name%", map, FixedNow);

        Assert.Equal("База: Бухгалтерия", result);
    }

    [Fact]
    public void Resolve_ConnectionServer_SubstitutesServer()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("/S:%connection.server%", map, FixedNow);

        Assert.Equal("/S:srv-1c", result);
    }

    [Fact]
    public void Resolve_NestedConnectionKeys_SubstituteAll()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve(
            "%name%|%connection.server%|%connection.database%|%connection.filePath%",
            map, FixedNow);

        Assert.Equal("Бухгалтерия|srv-1c|acc_main|", result);
    }

    [Fact]
    public void BuildValueMap_FileBase_HasFilePath()
    {
        var ib = new Infobase { Name = "Файловая" };
        ib.Connection.Type = ConnectionType.File;
        ib.Connection.FilePath = @"D:\bases\file_base";

        var map = Map(ib);

        Assert.Equal(@"D:\bases\file_base", map["connection.filePath"]);
        Assert.Equal("Файловая", map["name"]);
    }

    [Fact]
    public void BuildValueMap_NullInfobase_ReturnsEmptyMap()
    {
        var map = Map(null);

        Assert.Empty(map);
    }

    // ------------------- Дата -------------------

    [Fact]
    public void Resolve_Date_DefaultFormat()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("%date%", map, FixedNow);

        Assert.Equal("2026-09-28", result);
    }

    [Fact]
    public void Resolve_DateCustomFormat_UsesGivenFormat()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("%date:yyyyMMdd_HHmm%", map, FixedNow);

        Assert.Equal("20260928_1405", result);
    }

    [Fact]
    public void Resolve_DateInvalidFormat_FallsBackToDefault()
    {
        var map = Map(MakeInfobase());

        // Некорректный формат .NET не должен ронять подстановку.
        var result = ScriptParameterResolver.Resolve("%date:[%", map, FixedNow);

        Assert.Equal("2026-09-28", result);
    }

    // ------------------- Неизвестный ключ -------------------

    [Fact]
    public void Resolve_UnknownKey_LeavesTokenAsIs()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("%unknown.key%", map, FixedNow);

        Assert.Equal("%unknown.key%", result);
    }

    [Fact]
    public void Resolve_UnknownKey_WhenNotLeaveReplacesWithEmpty()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("a-%unknown.key%-b", map, FixedNow, leaveUnknown: false);

        Assert.Equal("a--b", result);
    }

    [Fact]
    public void Resolve_NoTokens_ReturnsTemplateAsIs()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("просто текст", map, FixedNow);

        Assert.Equal("просто текст", result);
    }

    // ------------------- Пароль (issue #308) -------------------

    [Fact]
    public void Resolve_ConnectionPassword_SubstitutesPassword()
    {
        var map = Map(MakeInfobase());

        // Подставляются обе формы токена: connection.password и плоский password.
        var result = ScriptParameterResolver.Resolve(
            "%connection.password%|%password%", map, FixedNow);

        Assert.Equal("secret-pass|secret-pass", result);
    }

    [Fact]
    public void BuildValueMap_HasPasswordKeys()
    {
        var map = Map(MakeInfobase());

        Assert.True(map.ContainsKey("connection.password"));
        Assert.True(map.ContainsKey("password"));
        Assert.Equal("secret-pass", map["connection.password"]);
        Assert.Equal("secret-pass", map["password"]);
    }

    [Fact]
    public void BuildValueMap_NullPassword_SubstitutesEmptyString()
    {
        var ib = MakeInfobase();
        ib.Connection.Password = null!;

        var result = ScriptParameterResolver.Resolve("%connection.password%", Map(ib), FixedNow);

        Assert.Equal("", result);
    }

    [Fact]
    public void BuildValueMap_ReflectionInvariant_EveryConnectionPropertyHasKey()
    {
        // Инвариант (issue #308): для КАЖДОГО публичного свойства ConnectionSettings
        // в карте существует ключ connection.<имя> — «будущие» свойства подхватываются
        // динамическим проходом рефлексией автоматически.
        var map = Map(MakeInfobase());

        var properties = typeof(ConnectionSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Assert.NotEmpty(properties);

        foreach (var prop in properties)
        {
            var key = "connection." + prop.Name.ToLowerInvariant();
            Assert.True(map.ContainsKey(key), $"Карта значений не содержит ключ «{key}» для свойства {prop.Name}");
        }
    }

    [Fact]
    public void BuildValueMap_Reflection_DynamicKeyUsesPropertyValue()
    {
        // Динамические ключи отражают текущие значения свойств (issue #308).
        var ib = MakeInfobase();
        ib.Connection.BlockScheduledJobs = true;

        var map = Map(ib);

        Assert.Equal("True", map["connection.blockScheduledJobs"]);
        Assert.Equal("False", map["connection.useOsAuthentication"]);
        Assert.Equal("Prompt", map["connection.authenticationMode"]);

        // UseOsAuthentication=true переводит режим аутентификации в Windows.
        ib.Connection.UseOsAuthentication = true;
        var map2 = Map(ib);

        Assert.Equal("True", map2["connection.useOsAuthentication"]);
        Assert.Equal("Windows", map2["connection.authenticationMode"]);
    }

    // ------------------- Командная строка -------------------

    [Fact]
    public void BuildCommandLine_PathWithSpaces_QuotesPath()
    {
        var scenario = new ScriptScenario
        {
            FilePath = @"C:\Program Files\Tools\report.bat",
            Parameters = new List<string> { "/S:%connection.server%", "base=%name%" }
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(scenario, map, FixedNow);

        Assert.Equal("\"C:\\Program Files\\Tools\\report.bat\" /S:srv-1c base=Бухгалтерия", result);
    }

    [Fact]
    public void BuildCommandLine_NoSpaceInPath_KeepsPathUnquoted()
    {
        var scenario = new ScriptScenario
        {
            FilePath = "/opt/scripts/report.sh",
            Parameters = new List<string> { "--db", "%connection.database%" }
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(scenario, map, FixedNow);

        Assert.Equal("/opt/scripts/report.sh --db acc_main", result);
    }

    [Fact]
    public void BuildCommandLine_EmptyParameters_ReturnsPathOnly()
    {
        var scenario = new ScriptScenario { FilePath = "tool.exe" };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(scenario, map, FixedNow);

        Assert.Equal("tool.exe", result);
    }

    // ------------------- Папка запуска (issue #308, п.7) -------------------

    [Fact]
    public void BuildCommandLine_WithWorkingDirectory_PrependsCd()
    {
        var scenario = new ScriptScenario
        {
            FilePath = "report.bat",
            WorkingDirectory = @"C:\Tools\scripts",
            Parameters = new List<string> { "/S:%connection.server%" }
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(scenario, map, FixedNow);

        Assert.Equal(@"cd C:\Tools\scripts && report.bat /S:srv-1c", result);
    }

    [Fact]
    public void BuildCommandLine_WorkingDirectoryWithSpaces_QuotesPath()
    {
        var scenario = new ScriptScenario
        {
            FilePath = "report.bat",
            WorkingDirectory = @"C:\Program Files\Tools"
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(scenario, map, FixedNow);

        Assert.Equal("cd \"C:\\Program Files\\Tools\" && report.bat", result);
    }

    [Fact]
    public void BuildCommandLine_EmptyWorkingDirectory_KeepsOldBehavior()
    {
        // Пустая папка запуска — прежнее поведение: без префикса cd.
        var scenario = new ScriptScenario
        {
            FilePath = "tool.exe",
            WorkingDirectory = "",
            Parameters = new List<string> { "%name%" }
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(scenario, map, FixedNow);

        Assert.Equal("tool.exe Бухгалтерия", result);
    }

    [Fact]
    public void BuildCommandLine_WithWorkingDirectory_PowerShell_UsesSemicolonSeparator()
    {
        // PowerShell 5.1 не поддерживает лексему «&&» (issue #308, замечание @7OH):
        // между «cd …» и командой должен быть разделитель «;».
        var scenario = new ScriptScenario
        {
            FilePath = "report.ps1",
            WorkingDirectory = @"C:\Tools\scripts",
            Parameters = new List<string> { "-Server %connection.server%" }
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(
            scenario, map, FixedNow, shell: ScriptShell.PowerShell, isWindows: true);

        Assert.Equal(@"cd C:\Tools\scripts ; report.ps1 -Server srv-1c", result);
        Assert.DoesNotContain("&&", result);
    }

    [Theory]
    [InlineData(ScriptShell.Cmd, true)]   // явный cmd на Windows
    [InlineData(ScriptShell.Cmd, false)]  // явный cmd и на Linux
    [InlineData(ScriptShell.Sh, true)]
    [InlineData(ScriptShell.Sh, false)]
    [InlineData(ScriptShell.Auto, true)]  // Auto на Windows — cmd → «&&»
    [InlineData(ScriptShell.Auto, false)] // Auto на Linux — sh → «&&»
    public void BuildCommandLine_WithWorkingDirectory_NonPowerShell_UsesAmpersand(ScriptShell shell, bool isWindows)
    {
        // Для cmd/sh (включая Auto по обеим платформам) разделитель остаётся «&&».
        var scenario = new ScriptScenario
        {
            FilePath = "report.bat",
            WorkingDirectory = @"C:\Tools\scripts"
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(
            scenario, map, FixedNow, shell: shell, isWindows: isWindows);

        Assert.Equal(@"cd C:\Tools\scripts && report.bat", result);
        Assert.DoesNotContain(";", result);
    }

    [Fact]
    public void BuildCommandLine_EmptyWorkingDirectory_PowerShell_NoSeparatorAppears()
    {
        // Без рабочей папки префикс cd (и разделитель) не появляется ни для какого шелла.
        var scenario = new ScriptScenario
        {
            FilePath = "report.ps1",
            WorkingDirectory = "",
            Parameters = new List<string> { "%name%" },
            Shell = ScriptShell.PowerShell
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(
            scenario, map, FixedNow, shell: ScriptShell.PowerShell, isWindows: true);

        Assert.Equal("report.ps1 Бухгалтерия", result);
        Assert.DoesNotContain(";", result);
        Assert.DoesNotContain("&&", result);
    }

    // ------------------- Интерпретатор (issue #308, п.9) -------------------

    [Theory]
    [InlineData(ScriptShell.Auto, true, "cmd.exe /c report.bat")]
    [InlineData(ScriptShell.Auto, false, "/bin/sh -c report.bat")]
    [InlineData(ScriptShell.Cmd, true, "cmd.exe /c report.bat")]
    [InlineData(ScriptShell.Cmd, false, "cmd.exe /c report.bat")]
    [InlineData(ScriptShell.PowerShell, true, "powershell -NoProfile -Command report.bat")]
    [InlineData(ScriptShell.PowerShell, false, "powershell -NoProfile -Command report.bat")]
    [InlineData(ScriptShell.Sh, true, "/bin/sh -c report.bat")]
    [InlineData(ScriptShell.Sh, false, "/bin/sh -c report.bat")]
    public void BuildShellCommandLine_WrapsBodyForChosenShell(ScriptShell shell, bool isWindows, string expected)
    {
        // Issue #308, п.9: полная строка превью включает обёртку выбранного интерпретатора;
        // «Авто» — по платформе (cmd на Windows, /bin/sh на Linux).
        var scenario = new ScriptScenario
        {
            FilePath = "report.bat",
            Shell = shell
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildShellCommandLine(scenario, map, FixedNow, isWindows);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildShellCommandLine_PowerShell_ResolvesParametersUnderShell()
    {
        // Подстановки выполняются в теле, а не в обёртке (issue #308, п.9).
        var scenario = new ScriptScenario
        {
            FilePath = "report.ps1",
            Parameters = new List<string> { "-Server %connection.server%" },
            Shell = ScriptShell.PowerShell
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildShellCommandLine(scenario, map, FixedNow, isWindows: true);

        Assert.Equal("powershell -NoProfile -Command report.ps1 -Server srv-1c", result);
    }

    [Fact]
    public void BuildShellCommandLine_WithWorkingDirectory_PrependsCdUnderShell()
    {
        // «cd … && тело» оборачивается выбранным интерпретатором целиком (issue #308, п.9).
        var scenario = new ScriptScenario
        {
            FilePath = "report.sh",
            WorkingDirectory = @"/opt/scripts",
            Shell = ScriptShell.Sh
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildShellCommandLine(scenario, map, FixedNow, isWindows: false);

        Assert.Equal("/bin/sh -c cd /opt/scripts && report.sh", result);
    }

    [Fact]
    public void BuildShellCommandLine_WithWorkingDirectory_PowerShell_UsesSemicolon()
    {
        // Полная команда для powershell.exe 5.1 без лексемы «&&»: между «cd …»
        // и телом — «;» (issue #308, замечание @7OH 15:05).
        var scenario = new ScriptScenario
        {
            FilePath = "report.ps1",
            WorkingDirectory = @"C:\Tools\scripts",
            Shell = ScriptShell.PowerShell
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildShellCommandLine(scenario, map, FixedNow, isWindows: true);

        Assert.Equal("powershell -NoProfile -Command cd C:\\Tools\\scripts ; report.ps1", result);
        Assert.DoesNotContain("&&", result);
    }

    [Fact]
    public void BuildShellCommandLine_AutoOnWindows_KeepsCmdAmpersand()
    {
        // «Авто» на Windows — по-прежнему cmd /c … && … (issue #308, ручная проверка п.4).
        var scenario = new ScriptScenario
        {
            FilePath = "report.bat",
            WorkingDirectory = @"C:\Tools\scripts",
            Shell = ScriptShell.Auto
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildShellCommandLine(scenario, map, FixedNow, isWindows: true);

        Assert.Equal("cmd.exe /c cd C:\\Tools\\scripts && report.bat", result);
    }

    // ------------------- Токены {…} (цикл 0.3.9.194, функция 7) -------------------

    [Fact]
    public void Resolve_BraceTokens_ClientServer_SubstitutesAliases()
    {
        var ib = MakeInfobase();
        var map = Map(ib);

        var result = ScriptParameterResolver.Resolve(
            "{ИмяБазы}|{СтрокаПодключения}|{Тип}|{Сервер}|{ИмяНаСервере}|{Пользователь}|{Пароль}|{ВебURL}|{Порт}",
            map, FixedNow);

        Assert.Equal(
            "Бухгалтерия|Srvr=\"srv-1c\";Ref=\"acc_main\"|ClientServer|srv-1c|acc_main||secret-pass||1541",
            result);
    }

    [Fact]
    public void Resolve_BraceTokens_FileBase_SubstitutesCatalogPathAndConnection()
    {
        var ib = new Infobase { Name = "Файловая" };
        ib.Connection.Type = ConnectionType.File;
        ib.Connection.FilePath = @"D:\bases\file_base";

        var result = ScriptParameterResolver.Resolve(
            "{Каталог}|{ПутьИБ}|{СтрокаПодключения}|{Тип}", Map(ib), FixedNow);

        Assert.Equal(@"D:\bases\file_base|D:\bases\file_base|File=""D:\bases\file_base""|File", result);
    }

    [Fact]
    public void Resolve_BraceTokens_WebServer_SubstitutesUrl()
    {
        var ib = new Infobase { Name = "Вебовая" };
        ib.Connection.Type = ConnectionType.WebServer;
        ib.Connection.WebUrl = "http://srv-1c/base";

        var result = ScriptParameterResolver.Resolve(
            "{Тип}|{ВебURL}|{СтрокаПодключения}", Map(ib), FixedNow);

        Assert.Equal(@"WebServer|http://srv-1c/base|WS=""http://srv-1c/base""", result);
    }

    [Fact]
    public void Resolve_BraceTokens_IdAndGroup_SubstituteValues()
    {
        var ib = MakeInfobase();
        ib.Id = "abc-123";
        ib.Group = "Бухгалтерия";

        var result = ScriptParameterResolver.Resolve("{Id}|{ИмяГруппы}", Map(ib), FixedNow);

        Assert.Equal("abc-123|Бухгалтерия", result);
    }

    [Fact]
    public void Resolve_MixedSyntax_PercentAndBraceAliases_ProduceSameValue()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve(
            "%name%|{ИмяБазы}|%connection.password%|{Пароль}", map, FixedNow);

        Assert.Equal("Бухгалтерия|Бухгалтерия|secret-pass|secret-pass", result);
    }

    [Fact]
    public void Resolve_BraceDate_SameAsPercentDate()
    {
        var map = Map(MakeInfobase());

        var defaultResult = ScriptParameterResolver.Resolve("{Дата}", map, FixedNow);
        var customResult = ScriptParameterResolver.Resolve("{Дата:yyyyMMdd}", map, FixedNow);

        Assert.Equal("2026-09-28", defaultResult);
        Assert.Equal("20260928", customResult);
    }

    [Fact]
    public void Resolve_UnknownBraceToken_LeaveUnknownKeepsToken()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("{неизвестный.ключ}|%unknown%", map, FixedNow);

        Assert.Equal("{неизвестный.ключ}|%unknown%", result);
    }

    [Fact]
    public void Resolve_UnknownBraceToken_WhenNotLeaveReplacesWithEmpty()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve(
            "a-{неизвестный.ключ}-b", map, FixedNow, leaveUnknown: false);

        Assert.Equal("a--b", result);
    }

    // ------------------- Экранирование для shell (EscapeForShell) -------------------

    [Fact]
    public void EscapeForShell_Cmd_QuotesAndDoublesInnerQuotes()
    {
        Assert.Equal(
            "\"значение с пробелами\"",
            ScriptParameterResolver.EscapeForShell("значение с пробелами", ScriptShell.Cmd));
        Assert.Equal(
            "\"сказал \"\"привет\"\"\"",
            ScriptParameterResolver.EscapeForShell("сказал \"привет\"", ScriptShell.Cmd));
    }

    [Fact]
    public void EscapeForShell_Sh_QuotesAndEscapesSingleQuote()
    {
        Assert.Equal(
            "'значение с пробелами'",
            ScriptParameterResolver.EscapeForShell("значение с пробелами", ScriptShell.Sh));
        Assert.Equal(
            "'it'\\''s'",
            ScriptParameterResolver.EscapeForShell("it's", ScriptShell.Sh));
    }

    [Fact]
    public void EscapeForShell_PowerShell_QuotesAndDoublesSingleQuote()
    {
        Assert.Equal(
            "'значение с пробелами'",
            ScriptParameterResolver.EscapeForShell("значение с пробелами", ScriptShell.PowerShell));
        Assert.Equal(
            "'it''s'",
            ScriptParameterResolver.EscapeForShell("it's", ScriptShell.PowerShell));
    }

    [Fact]
    public void EscapeForShell_EmptyValue_ReturnsEmptyString()
    {
        Assert.Equal("", ScriptParameterResolver.EscapeForShell("", ScriptShell.Cmd));
        Assert.Equal("", ScriptParameterResolver.EscapeForShell(null, ScriptShell.Sh));
        Assert.Equal("", ScriptParameterResolver.EscapeForShell("", ScriptShell.PowerShell));
    }

    [Theory]
    [InlineData(true, "\"значение с пробелами\"")]
    [InlineData(false, "'значение с пробелами'")]
    public void EscapeForShell_Auto_UsesExplicitPlatform(bool isWindows, string expected)
    {
        var result = ScriptParameterResolver.EscapeForShell(
            "значение с пробелами", ScriptShell.Auto, isWindows);

        Assert.Equal(expected, result);
    }

    // ------------------- Экранирование в Resolve (escapeValues) -------------------

    [Fact]
    public void Resolve_EscapeValuesTrue_Sh_QuotesBaseNameButNotDate()
    {
        var ib = MakeInfobase();
        ib.Name = "Бухгалтерия (тест)";

        var result = ScriptParameterResolver.Resolve(
            "{ИмяБазы} {Дата}", Map(ib), FixedNow, escapeValues: true, shell: ScriptShell.Sh);

        Assert.Equal("'Бухгалтерия (тест)' 2026-09-28", result);
    }

    [Fact]
    public void Resolve_EscapeValuesFalse_NoQuotesAroundValue()
    {
        var ib = MakeInfobase();
        ib.Name = "Бухгалтерия (тест)";

        var result = ScriptParameterResolver.Resolve("{ИмяБазы}", Map(ib), FixedNow);

        Assert.Equal("Бухгалтерия (тест)", result);
    }

    // ------------------- Командная строка действия (BuildAction*) -------------------

    [Fact]
    public void BuildActionCommandLine_ResolvesTokensWithEscapeValues()
    {
        var ib = MakeInfobase();
        ib.Name = "Бухгалтерия (тест)";
        var action = new CustomAction
        {
            Command = "echo {ИмяБазы} {Тип} {Дата}",
            Shell = ScriptShell.Sh,
            EscapeValues = true
        };

        var body = ScriptParameterResolver.BuildActionCommandLine(action, ib, FixedNow);

        // При escapeValues=true экранируются ВСЕ значения словаря (включая {Тип}),
        // токен даты — нет.
        Assert.Equal("echo 'Бухгалтерия (тест)' 'ClientServer' 2026-09-28", body);
    }

    [Fact]
    public void BuildActionCommandLine_WhenEscapeValuesFalse_NoQuotes()
    {
        var ib = MakeInfobase();
        ib.Name = "Бухгалтерия (тест)";
        var action = new CustomAction
        {
            Command = "echo {ИмяБазы}",
            Shell = ScriptShell.Cmd,
            EscapeValues = false
        };

        var body = ScriptParameterResolver.BuildActionCommandLine(action, ib, FixedNow);

        Assert.Equal("echo Бухгалтерия (тест)", body);
    }

    [Fact]
    public void BuildActionShellCommandLine_Sh_WrapsBodyWithSh()
    {
        var ib = MakeInfobase();
        ib.Name = "Бухгалтерия (тест)";
        var action = new CustomAction { Command = "echo {ИмяБазы}", Shell = ScriptShell.Sh, EscapeValues = true };

        var result = ScriptParameterResolver.BuildActionShellCommandLine(action, ib, FixedNow, isWindows: false);

        Assert.Equal("/bin/sh -c echo 'Бухгалтерия (тест)'", result);
    }

    [Fact]
    public void BuildActionShellCommandLine_Cmd_WrapsBodyWithCmd()
    {
        var ib = MakeInfobase();
        ib.Name = "Бухгалтерия (тест)";
        var action = new CustomAction { Command = "echo {ИмяБазы}", Shell = ScriptShell.Cmd, EscapeValues = true };

        var result = ScriptParameterResolver.BuildActionShellCommandLine(action, ib, FixedNow, isWindows: true);

        Assert.Equal("cmd.exe /c echo \"Бухгалтерия (тест)\"", result);
    }

    [Fact]
    public void BuildActionShellCommandLine_PowerShell_WrapsBodyWithPowerShell()
    {
        var ib = MakeInfobase();
        var action = new CustomAction
        {
            Command = "Write-Host {ИмяБазы}",
            Shell = ScriptShell.PowerShell,
            EscapeValues = true
        };

        var result = ScriptParameterResolver.BuildActionShellCommandLine(action, ib, FixedNow, isWindows: true);

        Assert.Equal("powershell -NoProfile -Command Write-Host 'Бухгалтерия'", result);
    }

    [Fact]
    public void BuildActionShellCommandLine_Auto_WrapsByPlatform()
    {
        // Обёртка «Авто» — по платформе (isWindows). Экранирование значений при Auto
        // использует текущую ОС выполнения, поэтому команда без подстановок: проверяется
        // только выбор интерпретатора.
        var ib = MakeInfobase();
        var action = new CustomAction { Command = "echo ok", Shell = ScriptShell.Auto, EscapeValues = true };

        var onWindows = ScriptParameterResolver.BuildActionShellCommandLine(action, ib, FixedNow, isWindows: true);
        var onLinux = ScriptParameterResolver.BuildActionShellCommandLine(action, ib, FixedNow, isWindows: false);

        Assert.Equal("cmd.exe /c echo ok", onWindows);
        Assert.Equal("/bin/sh -c echo ok", onLinux);
    }

    [Fact]
    public void BuildActionShellCommandLine_PreviewMatchesRealBodyUnderWrapper()
    {
        var ib = MakeInfobase();
        ib.Name = "Бухгалтерия (тест)";
        var action = new CustomAction
        {
            Command = "echo {ИмяБазы} {Дата}",
            Shell = ScriptShell.Sh,
            EscapeValues = true
        };

        var body = ScriptParameterResolver.BuildActionCommandLine(action, ib, FixedNow);
        var preview = ScriptParameterResolver.BuildActionShellCommandLine(action, ib, FixedNow, isWindows: false);

        Assert.Equal("/bin/sh -c " + body, preview);
    }

    [Fact]
    public void BuildActionCommandLine_NullAction_ReturnsEmpty()
    {
        Assert.Equal("", ScriptParameterResolver.BuildActionCommandLine(null!, MakeInfobase(), FixedNow));
        Assert.Equal("", ScriptParameterResolver.BuildActionShellCommandLine(null!, MakeInfobase(), FixedNow, true));
    }
}