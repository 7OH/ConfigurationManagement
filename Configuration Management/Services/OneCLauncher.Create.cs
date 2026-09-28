using System.Text;

namespace Configuration_Management.Services;

public static partial class OneCLauncher
{
    /// <summary>
    /// Строка подключения клиент-серверной ИБ для команды CREATEINFOBASE:
    /// «Srvr="…";Ref="…"» + опциональные DBMS/DBSrvr/DB/DBUID/DBPwd/CrSQLDB/
    /// SchJobDn/disstt. Чистая функция без побочных эффектов: используется обеими
    /// платформами (Windows/WPF — <c>OneCLauncher.Arguments</c>, Linux/Avalonia —
    /// <c>OneCLauncher.Linux.Process</c>) и покрыта юнит-тестами.
    /// Кавычка внутри значения удваивается (грамматика строки подключения 1С).
    /// </summary>
    /// <param name="server">Имя сервера 1С (уже обрезано от пробелов).</param>
    /// <param name="databaseName">Имя базы на сервере 1С (уже обрезано от пробелов).</param>
    /// <param name="dbms">Тип СУБД (MSSQLServer, PostgreSQL, …).</param>
    /// <param name="dbServer">Сервер СУБД.</param>
    /// <param name="dbName">Имя базы данных в СУБД.</param>
    /// <param name="dbUser">Пользователь СУБД.</param>
    /// <param name="dbPassword">Пароль пользователя СУБД.</param>
    /// <param name="createSqlDatabase">Создавать базу данных на сервере СУБД (CrSQLDB="Y").</param>
    /// <param name="blockScheduledJobs">Блокировать фоновые задания (SchJobDn="Y").</param>
    /// <param name="forbidSpeechRecognition">
    /// Запретить локальное распознавание речи (disstt="Y", issue #307): документированный
    /// параметр строки подключения; при создании клиент-серверной базы запрет
    /// устанавливается на сервере 1С (как флажок типового стартера).
    /// </param>
    public static string BuildClientServerCreateConnectionString(
        string server,
        string databaseName,
        string? dbms = null,
        string? dbServer = null,
        string? dbName = null,
        string? dbUser = null,
        string? dbPassword = null,
        bool createSqlDatabase = false,
        bool blockScheduledJobs = false,
        bool forbidSpeechRecognition = false)
    {
        var csb = new StringBuilder(
            $"Srvr=\"{EscapeCreateValue(server)}\";Ref=\"{EscapeCreateValue(databaseName)}\"");
        if (!string.IsNullOrWhiteSpace(dbms))
            csb.Append($";DBMS=\"{EscapeCreateValue(dbms)}\"");
        if (!string.IsNullOrWhiteSpace(dbServer))
            csb.Append($";DBSrvr=\"{EscapeCreateValue(dbServer)}\"");
        if (!string.IsNullOrWhiteSpace(dbName))
            csb.Append($";DB=\"{EscapeCreateValue(dbName)}\"");
        if (!string.IsNullOrWhiteSpace(dbUser))
            csb.Append($";DBUID=\"{EscapeCreateValue(dbUser)}\"");
        if (!string.IsNullOrWhiteSpace(dbPassword))
            csb.Append($";DBPwd=\"{EscapeCreateValue(dbPassword)}\"");
        // Создание базы данных на сервере СУБД задаётся параметром строки подключения,
        // а не ключом командной строки: с «/CreateDatabase» платформа базу не создаёт
        // и падает на попытке подключиться к несуществующей (issue #77).
        if (createSqlDatabase)
            csb.Append(";CrSQLDB=\"Y\"");
        // SchJobDn действует только в CREATEINFOBASE: он задаёт состояние фоновых заданий
        // создаваемой клиент-серверной базы и не должен попадать в обычную строку подключения.
        if (blockScheduledJobs)
            csb.Append(";SchJobDn=\"Y\"");
        // disstt — «Локальное распознавание речи»: Y — запрещено (issue #307).
        if (forbidSpeechRecognition)
            csb.Append(";disstt=\"Y\"");
        return csb.ToString();
    }

    private static string EscapeCreateValue(string value) => value.Replace("\"", "\"\"");
}