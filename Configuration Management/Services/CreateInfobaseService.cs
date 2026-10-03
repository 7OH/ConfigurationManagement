using System;
using System.Collections.Generic;
using System.IO;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="ICreateInfobaseService"/>. Содержит общую для WPF и Avalonia логику
/// создания ИБ: валидацию ввода, проверку несовместимой версии платформы на сервере (#91),
/// вызов <c>OneCLauncher.CreateInfoBase</c>, сборку результата и запоминание последней версии.
/// </summary>
public sealed class CreateInfobaseService : ICreateInfobaseService
{
    private readonly IInfobaseRepository _repository;

    public CreateInfobaseService(IInfobaseRepository repository)
    {
        _repository = repository;
    }

    public CreateInfobaseResult TryCreate(CreateInfobaseRequest request, bool confirmVersionMismatch)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return new CreateInfobaseResult { Kind = CreateInfobaseResultKind.EnterName };

        string? templatePath = null;
        if (request.FromTemplate)
        {
            templatePath = (request.TemplatePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                return new CreateInfobaseResult { Kind = CreateInfobaseResultKind.EnterTemplateFile };
        }

        var platform = (request.PlatformVersion ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(platform))
            return new CreateInfobaseResult { Kind = CreateInfobaseResultKind.NoPlatform };

        ConnectionSettings connection;
        if (request.IsFile)
        {
            var filePath = (request.FilePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(filePath))
                return new CreateInfobaseResult { Kind = CreateInfobaseResultKind.EnterFilePath };

            var (ok, error) = OneCLauncher.CreateInfoBase(
                platformVersion: platform,
                isFile: true,
                filePath: filePath,
                server: null,
                databaseName: null,
                templatePath: templatePath,
                forbidSpeechRecognition: request.ForbidSpeechRecognition);
            if (!ok)
                return new CreateInfobaseResult
                {
                    Kind = CreateInfobaseResultKind.CreateFailed,
                    ErrorMessage = error
                };

            connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = filePath ?? "",
                ForbidSpeechRecognition = request.ForbidSpeechRecognition
            };
        }
        else
        {
            var server = (request.Server ?? string.Empty).Trim();
            // Порт сервера 1С выбирается вместе с сервером (server:port, issue #305)
            // и попадает в параметры подключения созданной базы. Порт СУБД не затрагивается.
            var serverPortText = (request.ServerPort ?? string.Empty).Trim();
            int.TryParse(serverPortText, out var serverPort);
            if (serverPort is < 1 or > 65535)
                serverPort = 0;
            var refName = (request.DatabaseName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(refName))
                return new CreateInfobaseResult { Kind = CreateInfobaseResultKind.EnterServerAndDb };

            // Вариант 2 (#91): заранее предупреждаем, если выбранная версия платформы
            // отличается (по major.minor) от версий, которыми уже работают
            // клиент-серверные базы на этом же сервере. «Нет» — прерывает создание.
            var existingVersion = GetIncompatibleExistingVersion(platform, server);
            if (existingVersion != null && !confirmVersionMismatch)
            {
                return new CreateInfobaseResult
                {
                    Kind = CreateInfobaseResultKind.VersionMismatch,
                    IncompatibleExistingVersion = existingVersion
                };
            }

            var dbms = (request.Dbms ?? string.Empty).Trim();
            var dbServer = (request.DbServer ?? string.Empty).Trim();
            var dbPort = (request.DbPort ?? string.Empty).Trim();
            var dbName = (request.DbName ?? string.Empty).Trim();
            var dbUser = (request.DbUser ?? string.Empty).Trim();
            var dbPwd = request.DbPassword ?? "";
            var createSqlDatabase = request.CreateSqlDatabase;
            var blockScheduledJobs = request.BlockScheduledJobs;
            var forbidSpeechRecognition = request.ForbidSpeechRecognition;

            // DBSrvr собирается с учётом порта СУБД: для PostgreSQL — «host port=NNNN»
            // (через пробел), для MSSQL Server — «host,NNNN», для остальных — просто host.
            var dbsrvr = BuildDbServerString(dbms, dbServer, dbPort);

            var (ok, error) = OneCLauncher.CreateInfoBase(
                platformVersion: platform,
                isFile: false,
                filePath: null,
                server: server,
                databaseName: refName,
                templatePath: templatePath,
                dbms: dbms,
                dbServer: dbsrvr,
                dbName: dbName,
                dbUser: dbUser,
                dbPassword: dbPwd,
                createSqlDatabase: createSqlDatabase,
                blockScheduledJobs: blockScheduledJobs,
                forbidSpeechRecognition: forbidSpeechRecognition,
                serverPort: serverPort);
            if (!ok)
                return new CreateInfobaseResult
                {
                    Kind = CreateInfobaseResultKind.CreateFailed,
                    ErrorMessage = error
                };

            // Создание прошло успешно — запоминаем сервер СУБД и порт для подстановки (issue #305).
            SaveLastDbServer(dbServer, dbPort);

            connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = server,
                Port = serverPort,
                DatabaseName = refName,
                BlockScheduledJobs = blockScheduledJobs,
                ForbidSpeechRecognition = forbidSpeechRecognition
            };
        }

        // Разрядность, выбранная суффиксом версии «(32)/(64)», сохраняется
        // в отдельное поле Architecture, а PlatformVersion — чистая версия
        // (без встроенной разрядности, чтобы она не попадала в ibases.v8i).
        // Если суффикс не указан — новая база наследует режим «Разрядности по
        // умолчанию» из настроек, а не жёстко 32-битную приоритетную (issue #305:
        // при дефолте X64 база создавалась с разрядностью х86).
        // ВАЖНО: ParseVariant БЕЗ суффикса возвращает architecture="32" по умолчанию,
        // и прежнее условие трактовало бы его как ЯВНО выбранную разрядность — в базу
        // записывалось «8.3.27 [x86]» при фактическом запуске x64 (issue #305).
        // ResolveStoredArchitecture использует ParseVariantOptionalArch: null без
        // суффикса → приоритет по настройке (см. комментарий к helper ниже).
        var storedPlatform = ResolveCleanPlatform(platform);
        var storedArchitecture = ResolveStoredArchitecture(
            platform, _repository.LoadSettings().DefaultArchitecture);

        var created = new Infobase
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Group = string.IsNullOrWhiteSpace(request.GroupPath) ? string.Empty : request.GroupPath,
            PlatformVersion = storedPlatform,
            Architecture = storedArchitecture,
            Connection = connection
        };

        // Создание прошло успешно — запоминаем версию для подстановки по умолчанию.
        SaveLastPlatformVersion(request.IsFile, platform);

        return new CreateInfobaseResult
        {
            Kind = CreateInfobaseResultKind.Success,
            CreatedInfobase = created
        };
    }

    /// <summary>
    /// Чистый helper сборки значения параметра DBSrvr команды CREATEINFOBASE
    /// (issue #305). Формат зависит от СУБД:
    /// <list type="bullet">
    /// <item>PostgreSQL — «host port=NNNN» (порт через пробел, как принимает платформа);</item>
    /// <item>MSSQL Server — «host,NNNN»;</item>
    /// <item>остальные — просто «host».</item>
    /// </list>
    /// Пустой порт не меняет строку; пустой сервер даёт пустую строку.
    /// </summary>
    public static string BuildDbServerString(string? dbms, string? server, string? port)
    {
        var srv = (server ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(srv))
            return string.Empty;

        var p = (port ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p))
            return srv;

        var dbmsType = (dbms ?? string.Empty).Trim();
        if (string.Equals(dbmsType, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
            return $"{srv} port={p}";
        if (string.Equals(dbmsType, "MSSQLServer", StringComparison.OrdinalIgnoreCase))
            return $"{srv},{p}";

        return srv;
    }

    /// <summary>
    /// Форматирует строку сервера 1С для выпадающего списка окна создания ИБ (issue #305):
    /// «server:port», если порт задан, иначе просто «server». Формат соответствует окну
    /// правки свойств базы (ConnectionSettingsWindow), где сервер выбирается вместе с портом.
    /// </summary>
    public static string Format1CServer(string? server, int port)
    {
        var srv = (server ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(srv))
            return string.Empty;
        return port > 0 ? $"{srv}:{port}" : srv;
    }

    /// <summary>
    /// Разбирает строку «server:port» (или «server») из единого поля «Сервер 1С» окна
    /// создания ИБ (issue #305) на имя сервера и порт. Формат — как в окне правки свойств
    /// базы: сервер копируется одним целым вместе с портом. Если справа от последнего
    /// двоеточия не число или порт вне диапазона 1..65535 — вся строка считается именем
    /// сервера, порт = 0.
    /// </summary>
    public static void ParseServerPort(string? value, out string server, out int port)
    {
        var text = (value ?? string.Empty).Trim();
        server = text;
        port = 0;
        if (string.IsNullOrEmpty(text))
            return;

        var idx = text.LastIndexOf(':');
        if (idx <= 0 || idx == text.Length - 1)
            return;
        if (int.TryParse(text[(idx + 1)..], out var parsed) && parsed is >= 1 and <= 65535)
        {
            server = text[..idx].Trim();
            port = parsed;
        }
    }

    /// <summary>
    /// Разбирает строку версии на числовые компоненты (major, minor).
    /// Суффиксы вроде « (64)» снимаются через <see cref="PlatformVersionService.ParseVariant"/>.
    /// </summary>
    private static (int Major, int Minor) GetMajorMinor(string version)
    {
        PlatformVersionService.ParseVariant(version, out var clean, out _);
        var v = string.IsNullOrWhiteSpace(clean) ? version : clean;
        var parts = (v ?? "").Split('.');
        int.TryParse(parts.Length >= 1 ? parts[0] : "", out var major);
        int.TryParse(parts.Length >= 2 ? parts[1] : "", out var minor);
        return (major, minor);
    }

    /// <summary>
    /// Эвристика Варианта 2 (#91): ищет среди уже существующих клиент-серверных баз на том же
    /// сервере базу, версия платформы которой отличается от выбранной по первым двум числам
    /// (major.minor). Возвращает версию такой базы или null, если расхождений нет.
    /// Ошибки чтения списка баз не блокируют создание — возвращаем null.
    /// Версия определяется по ЛОКАЛЬНОМУ списку баз приложения (не с сервера): «версия на
    /// сервере» — это версии баз из вашего списка на том же сервере (issue #305).
    /// </summary>
    private string? GetIncompatibleExistingVersion(string platform, string server)
    {
        var (selectedMajor, selectedMinor) = GetMajorMinor(platform);

        List<Infobase> infobases;
        try
        {
            infobases = _repository.Load();
        }
        catch
        {
            return null;
        }

        foreach (var ib in infobases)
        {
            var conn = ib.Connection;
            if (conn == null || conn.Type != ConnectionType.ClientServer)
                continue;
            // Сравнение серверов устойчивое (issue #305): порт («srv:1541») и регистр
            // не мешают — база в списке может быть задана с портом, а поле окна без него.
            if (!SameServer(conn.Server, server))
                continue;
            if (string.IsNullOrWhiteSpace(ib.PlatformVersion))
                continue;

            var (major, minor) = GetMajorMinor(ib.PlatformVersion);
            if (major != selectedMajor || minor != selectedMinor)
                return ib.PlatformVersion;
        }

        return null;
    }

    /// <summary>
    /// Сравнивает два адреса сервера 1С без учёта порта и регистра (issue #305):
    /// «localhost:1541» ≡ «localhost», «SRV» ≡ «srv». Внутренний — для юнит-тестов.
    /// </summary>
    internal static bool SameServer(string? a, string? b)
    {
        var na = NormalizeServerHost(a);
        var nb = NormalizeServerHost(b);
        return na.Length > 0 && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Снимает порт с адреса сервера («srv:1541» → «srv»); без порта — как есть.</summary>
    private static string NormalizeServerHost(string? server)
    {
        var s = (server ?? string.Empty).Trim();
        var colon = s.LastIndexOf(':');
        if (colon > 0 && colon < s.Length - 1 && int.TryParse(s[(colon + 1)..], out _))
            return s[..colon].Trim();
        return s;
    }

    /// <summary>
    /// Запоминает последнюю успешно использованную версию платформы отдельно для
    /// файловых и клиент-серверных баз. Ошибки сохранения не должны ломать создание ИБ.
    /// </summary>
    private void SaveLastPlatformVersion(bool isFile, string platform)
    {
        try
        {
            var settings = _repository.LoadSettings();
            PlatformVersionService.ParseVariant(platform, out var cleanPlatform, out _);
            var clean = string.IsNullOrWhiteSpace(cleanPlatform) ? platform : cleanPlatform;
            if (isFile)
                settings.LastFileCreatePlatformVersion = clean;
            else
                settings.LastClientServerCreatePlatformVersion = clean;
            _repository.SaveSettings(settings);
        }
        catch
        {
            // Несохранение последней версии не должно прерывать создание ИБ.
        }
    }

    /// <summary>
    /// Приоритетная разрядность новой базы без суффикса в выбранной версии (issue #305):
    /// X64 из настроек → «64-priority», X86/Priority/легаси → «32-priority» (как раньше).
    /// Internal — для юнит-тестов.
    /// </summary>
    internal static string PriorityArchitectureFromDefault(string? defaultArchitecture) =>
        string.Equals(defaultArchitecture, "X64", StringComparison.OrdinalIgnoreCase)
            ? "64-priority"
            : "32-priority";

    /// <summary>
    /// Чистая версия платформы без суффикса разрядности («8.3.27 (64)» → «8.3.27»).
    /// </summary>
    internal static string ResolveCleanPlatform(string platform)
    {
        PlatformVersionService.ParseVariantOptionalArch(platform, out var clean, out _);
        return string.IsNullOrWhiteSpace(clean) ? platform : clean;
    }

    /// <summary>
    /// Разрядность новой базы для поля Architecture (issue #305): явный суффикс
    /// версии «(32)/(64)» сохраняется как есть; БЕЗ суффикса — приоритетный режим
    /// из настройки «Разрядности по умолчанию». ВАЖНО: <see cref="PlatformVersionService.ParseVariant"/>
    /// без суффикса возвращает «32» по умолчанию, поэтому здесь используется
    /// <see cref="PlatformVersionService.ParseVariantOptionalArch"/> — иначе при дефолте X64
    /// в базу записывалась бы разрядность «32» («8.3.27 [x86]») при фактическом запуске x64.
    /// Internal — для юнит-тестов.
    /// </summary>
    internal static string ResolveStoredArchitecture(string platform, string? defaultArchitecture)
    {
        PlatformVersionService.ParseVariantOptionalArch(platform, out _, out var arch);
        return arch is "32" or "64"
            ? arch
            : PriorityArchitectureFromDefault(defaultArchitecture);
    }

    /// <summary>
    /// Запоминает последний успешно использованный сервер СУБД и его порт (issue #305):
    /// они подставляются по умолчанию при следующем открытии окна создания ИБ.
    /// Ошибки сохранения не должны ломать создание ИБ.
    /// </summary>
    private void SaveLastDbServer(string dbServer, string dbPort)
    {
        try
        {
            var settings = _repository.LoadSettings();
            settings.LastCreateDbServer = dbServer;
            settings.LastCreateDbPort = dbPort;
            _repository.SaveSettings(settings);
        }
        catch
        {
            // Несохранение последнего сервера СУБД не должно прерывать создание ИБ.
        }
    }
}