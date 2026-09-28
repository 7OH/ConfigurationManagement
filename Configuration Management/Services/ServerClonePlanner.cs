using System.Text;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>Ошибка валидации запроса клонирования клиент-серверной ИБ.</summary>
public enum ServerCloneValidationError
{
    /// <summary>Запрос корректен.</summary>
    None,

    /// <summary>Не указано имя клона.</summary>
    EnterName,

    /// <summary>Не указан сервер 1С или имя базы на сервере (Ref).</summary>
    EnterServerAndRef,

    /// <summary>Не указана версия платформы 1С.</summary>
    NoPlatform,

    /// <summary>При создании БД на сервере СУБД не указаны СУБД/сервер СУБД.</summary>
    MissingDbmsDetails
}

/// <summary>
/// Чистая логика клонирования клиент-серверной ИБ (функция №10, 0.3.9.100):
/// предложение имени/Ref клона, сборка строки подключения и параметров 1С,
/// валидация запроса, описание этапов. Не зависит от платформы и UI —
/// покрыта юнит-тестами (см. <c>ServerClonePlannerTests</c>).
/// </summary>
public static class ServerClonePlanner
{
    /// <summary>
    /// Предлагаемое имя клона: делегирует <see cref="InfobaseCloneHelper.ProposeCloneName"/>,
    /// чтобы файловый и серверный клоны давали единый формат «<Имя> — Копия».
    /// </summary>
    public static string ProposeCloneName(string sourceName)
        => InfobaseCloneHelper.ProposeCloneName(sourceName);

    /// <summary>
    /// Предлагает имя базы на сервере (Ref) для клона: «<имя>_copy» с уникализацией
    /// («_copy», «_copy2», «_copy3», …) по локально известным Ref (сравнение
    /// регистронезависимое). Пустое имя источника → базовая «Clone» (план: «пустое
    /// имя → „Clone"»), занятая — «Clone2», «Clone3», … Реальный список Ref на
    /// кластере недоступен без rac/COM, поэтому проверяем только известные приложению.
    /// </summary>
    public static string SuggestRefName(string sourceDatabaseName, IEnumerable<string>? existingRefNames)
    {
        var baseName = (sourceDatabaseName ?? string.Empty).Trim();
        var seed = baseName.Length == 0 ? "Clone" : $"{baseName}_copy";

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingRefNames is not null)
        {
            foreach (var name in existingRefNames)
            {
                var trimmed = (name ?? string.Empty).Trim();
                if (trimmed.Length > 0)
                    existing.Add(trimmed);
            }
        }

        var candidate = seed;
        var suffix = 2;
        while (existing.Contains(candidate))
        {
            candidate = $"{seed}{suffix}";
            suffix++;
        }
        return candidate;
    }

    /// <summary>
    /// Строка подключения клона для CREATEINFOBASE: «Srvr="…";Ref="…"» + опциональные
    /// DBMS/DBSrvr/DB/DBUID/DBPwd/CrSQLDB/SchJobDn (по образцу
    /// <c>OneCLauncher.Arguments.CreateInfoBase</c>). Кавычка внутри значения удваивается.
    /// Используется для отображения и тестов; фактическую команду собирает <c>OneCLauncher</c>.
    /// </summary>
    public static string BuildConnectionString(ServerCloneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var csb = new StringBuilder(
            $"Srvr=\"{Escape(request.Server)}\";Ref=\"{Escape(request.DatabaseName)}\"");
        if (!string.IsNullOrWhiteSpace(request.Dbms))
            csb.Append($";DBMS=\"{Escape(request.Dbms)}\"");
        if (!string.IsNullOrWhiteSpace(request.DbServer))
            csb.Append($";DBSrvr=\"{Escape(request.DbServer)}\"");
        if (!string.IsNullOrWhiteSpace(request.DbName))
            csb.Append($";DB=\"{Escape(request.DbName)}\"");
        if (!string.IsNullOrWhiteSpace(request.DbUser))
            csb.Append($";DBUID=\"{Escape(request.DbUser)}\"");
        if (!string.IsNullOrWhiteSpace(request.DbPassword))
            csb.Append($";DBPwd=\"{Escape(request.DbPassword)}\"");
        if (request.CreateSqlDatabase)
            csb.Append(";CrSQLDB=\"Y\"");
        if (request.BlockScheduledJobs)
            csb.Append(";SchJobDn=\"Y\"");
        return csb.ToString();
    }

    /// <summary>
    /// Строка «Srvr» источника для ключа DESIGNER /S: «host[:port]\Ref».
    /// Пустые части опускаются.
    /// </summary>
    public static string BuildSourceSrvr(string serverWithPort, string refName)
    {
        var srv = (serverWithPort ?? string.Empty).Trim();
        var rf = (refName ?? string.Empty).Trim();
        if (srv.Length == 0)
            return rf;
        if (rf.Length == 0)
            return srv;
        return $"{srv}\\{rf}";
    }

    /// <summary>
    /// Валидация запроса клонирования (чистая, зеркалит проверки
    /// <c>CreateInfobaseService.TryCreate</c>): имя, платформа, сервер+Ref,
    /// параметры СУБД при создании БД на сервере.
    /// </summary>
    public static ServerCloneValidationError Validate(ServerCloneRequest? request)
    {
        if (request is null)
            return ServerCloneValidationError.EnterName;

        if (string.IsNullOrWhiteSpace(request.CloneName))
            return ServerCloneValidationError.EnterName;

        if (string.IsNullOrWhiteSpace(request.PlatformVersion))
            return ServerCloneValidationError.NoPlatform;

        if (string.IsNullOrWhiteSpace(request.Server) ||
            string.IsNullOrWhiteSpace(request.DatabaseName))
            return ServerCloneValidationError.EnterServerAndRef;

        // Платформе нужны DBMS и сервер СУБД, чтобы создать базу данных на сервере
        // (иначе команда собирается неполной — issue #77, см. OneCLauncher.Arguments).
        if (request.CreateSqlDatabase &&
            (string.IsNullOrWhiteSpace(request.Dbms) ||
             string.IsNullOrWhiteSpace(request.DbServer)))
            return ServerCloneValidationError.MissingDbmsDetails;

        return ServerCloneValidationError.None;
    }

    /// <summary>
    /// Последовательность этапов операции для окна прогресса (ключи локализации).
    /// Полная копия — «Выгрузка → Создание → Загрузка»; только конфигурация —
    /// «Выгрузка → Создание».
    /// </summary>
    public static IReadOnlyList<string> DescribeSteps(ServerCloneRequest? request)
    {
        if (request?.Mode == ServerCloneMode.ConfigurationOnly)
        {
            return new[]
            {
                "CloneServer.StageDump",
                "CloneServer.StageCreate"
            };
        }

        return new[]
        {
            "CloneServer.StageDump",
            "CloneServer.StageCreate",
            "CloneServer.StageRestore"
        };
    }

    private static string Escape(string? value) =>
        (value ?? string.Empty).Replace("\"", "\"\"");
}