using System.IO;
using System.Text;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Строка тела секции файла ibases.v8i в исходном виде (без заголовка «[Имя]»).
/// Для строк вида «Ключ=Значение» дополнительно хранится разобранный ключ, чтобы
/// запись могла обновлять значения управляемых ключей НА СВОИХ МЕСТАХ. Пустые строки
/// и строки без «=» (комментарии и т.п.) сохраняются дословно (<see cref="Key"/> == null).
/// </summary>
internal sealed class IbaseSectionLine
{
    /// <summary>Исходный текст строки без перевода строки.</summary>
    public string Raw { get; init; } = string.Empty;

    /// <summary>Имя ключа (до «=») с обрезанными краевыми пробелами; null для пустых/непарных строк.</summary>
    public string? Key { get; init; }

    /// <summary>Значение (после «=») с обрезанными краевыми пробелами; null для строк без «=».</summary>
    public string? Value { get; init; }
}

/// <summary>
/// Внутреннее представление записи базы (или группы-секции) из файла ibases.v8i.
/// Общая реализация для экспортёра и импортёра, чтобы оба пути работали одинаково
/// и не теряли ключи при пересохранении (issue #277).
/// Ключи секции хранятся в <see cref="Lines"/> в ИСХОДНОМ ПОРЯДКЕ (включая пустые
/// строки и неизвестные/пользовательские ключи). При записи значения управляемых
/// ключей (ID, Enable, Folder, Connect, App, DefaultApp, Version, AdditionalParameters)
/// обновляются на своих местах, а отсутствующие добавляются в каноническом порядке
/// в конец секции. Все прочие строки (Locale, External, ClientConnectionSpeed,
/// OrderInList/OrderInTree, WA, DisableLocalSpeechToText, пользовательские) переносятся
/// дословно, без потерь и без изменения порядка.
/// </summary>
internal sealed class IbaseEntry
{
    /// <summary>Имя секции (заголовок «[Имя]»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Строка подключения (Connect).</summary>
    public string Connect { get; set; } = string.Empty;

    /// <summary>Путь родительской группы в формате стартера (Folder).</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Признак включённой записи (Enable).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>ID базы 1С (GUID).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Режим запуска из файла ibases.v8i (Auto, ThinClient, ThickClient, WebClient).</summary>
    public string App { get; set; } = string.Empty;

    /// <summary>Режим запуска по умолчанию из файла ibases.v8i (DefaultApp).</summary>
    public string DefaultApp { get; set; } = string.Empty;

    /// <summary>Версия платформы 1С.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Дополнительные параметры подключения (AdditionalParameters).</summary>
    public string AdditionalParameters { get; set; } = string.Empty;

    /// <summary>Локаль базы (сохраняется дословно, не участвует в обновлении).</summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>Признак внешней базы (сохраняется дословно, не участвует в обновлении).</summary>
    public bool External { get; set; }

    /// <summary>Скорость соединения клиента (сохраняется дословно, не участвует в обновлении).</summary>
    public string ClientConnectionSpeed { get; set; } = string.Empty;

    /// <summary>
    /// Строки тела секции в исходном порядке (без заголовка «[Имя]»), включая пустые
    /// строки и неизвестные ключи. Хвостовые пустые строки (межсекционные разделители)
    /// при разборе удаляются — при записи между секциями вставляется ровно одна пустая
    /// строка.
    /// </summary>
    public List<IbaseSectionLine> Lines { get; } = new();

    /// <summary>
    /// Признак того, что запись является группой, а не базой.
    /// Группа — это секция без строки подключения (Connect).
    /// </summary>
    public bool IsGroup => string.IsNullOrWhiteSpace(Connect);

    /// <summary>
    /// Разбирает файл ibases.v8i на список записей. Используется и экспортёром
    /// (для чтения существующего файла перед перезаписью), и импортёром — единая
    /// реализация гарантирует, что ни один из путей не теряет ключи (issue #277).
    /// </summary>
    public static List<IbaseEntry> Parse(string filePath)
    {
        var entries = new List<IbaseEntry>();
        IbaseEntry? current = null;

        foreach (var rawLine in File.ReadAllLines(filePath, Encoding.Default))
        {
            var line = rawLine.Trim();

            if (line.Length == 0)
            {
                // Пустая строка внутри секции сохраняется как есть; пустые строки
                // между секциями при записи восстанавливаются разделителем.
                if (current is not null)
                    current.Lines.Add(new IbaseSectionLine { Raw = rawLine });
                continue;
            }

            // Секция базы: [Имя базы]
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                current = new IbaseEntry { Name = line.Substring(1, line.Length - 2).Trim() };
                entries.Add(current);
                continue;
            }

            if (current is null)
                continue;

            var eqIndex = line.IndexOf('=');
            if (eqIndex < 0)
            {
                // Строка без «=» (комментарий и т.п.) — сохраняем дословно.
                current.Lines.Add(new IbaseSectionLine { Raw = rawLine });
                continue;
            }

            var key = line.Substring(0, eqIndex).Trim();
            var value = line.Substring(eqIndex + 1).Trim();

            switch (key)
            {
                case "Connect":
                    current.Connect = value;
                    break;
                case "Folder":
                    current.Group = value;
                    break;
                case "Enable":
                    current.Enabled = ParseBool(value);
                    break;
                case "ID":
                    current.Id = value;
                    break;
                case "App":
                    current.App = value;
                    break;
                case "DefaultApp":
                    current.DefaultApp = value;
                    break;
                case "Version":
                    current.Version = value;
                    break;
                case "AdditionalParameters":
                    current.AdditionalParameters = value;
                    break;
                case "Locale":
                    current.Locale = value;
                    break;
                case "External":
                    current.External = ParseBool(value);
                    break;
                case "ClientConnectionSpeed":
                    current.ClientConnectionSpeed = value;
                    break;
            }

            current.Lines.Add(new IbaseSectionLine { Raw = rawLine, Key = key, Value = value });
        }

        // Хвостовые пустые строки секции — это межсекционные разделители; при записи
        // между секциями вставляется ровно одна пустая строка, поэтому убираем их,
        // чтобы не задваивать разделение. Непустые строки без «=» (комментарии в конце
        // секции) не трогаем.
        foreach (var entry in entries)
        {
            while (entry.Lines.Count > 0
                   && entry.Lines[^1].Key is null
                   && string.IsNullOrWhiteSpace(entry.Lines[^1].Raw))
            {
                entry.Lines.RemoveAt(entry.Lines.Count - 1);
            }
        }

        return entries;
    }

    /// <summary>
    /// Записывает тело секции (без заголовка «[Имя]») в StringBuilder, сохраняя исходный
    /// порядок строк: значения управляемых ключей обновляются на своих местах, удаляются
    /// строки ключей, чьи значения стали пустыми (и Enable при включённой записи),
    /// отсутствующие управляемые ключи добавляются в каноническом порядке в конец.
    /// Все прочие строки (пустые, неизвестные, пользовательские ключи) переносятся
    /// дословно (issue #277).
    /// </summary>
    public void WriteBodyTo(StringBuilder sb)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Проход 1: переписываем существующие строки на месте, сохраняя порядок.
        foreach (var line in Lines)
        {
            if (line.Key is null)
            {
                sb.AppendLine(line.Raw);
                continue;
            }

            if (!TryGetManagedValue(line.Key, out var value, out var omit))
            {
                // Неуправляемый ключ (в т.ч. Locale/External/ClientConnectionSpeed и
                // пользовательские ключи) — сохраняем строку дословно.
                sb.AppendLine(line.Raw);
                continue;
            }

            present.Add(line.Key);

            if (omit)
                continue; // Значение очищено (или Enable при включённой записи) — строку удаляем.

            sb.Append(line.Key).Append('=').AppendLine(value);
        }

        // Проход 2: добавляем отсутствующие управляемые ключи в каноническом порядке —
        // для новых записей и ключей, которых не было в исходной секции.
        foreach (var key in CanonicalManagedKeys)
        {
            if (present.Contains(key))
                continue;
            if (!TryGetManagedValue(key, out var value, out var omit) || omit || string.IsNullOrEmpty(value))
                continue;
            sb.Append(key).Append('=').AppendLine(value);
        }
    }

    /// <summary>Канонический порядок управляемых ключей для новых записей.</summary>
    private static readonly string[] CanonicalManagedKeys =
    {
        "ID", "Enable", "Folder", "Connect", "App", "DefaultApp", "Version", "AdditionalParameters"
    };

    /// <summary>
    /// Возвращает текущее значение управляемого ключа секции (регистронезависимо) и признак
    /// того, что строку ключа нужно удалить. Для неуправляемых ключей возвращает false.
    /// Ключ Enable опускается при включённой записи (отсутствие Enable = запись включена),
    /// а при отключённой — всегда выводится как Enable=0.
    /// </summary>
    private bool TryGetManagedValue(string key, out string? value, out bool omit)
    {
        switch (key.ToLowerInvariant())
        {
            case "id":
                value = Id ?? string.Empty;
                omit = string.IsNullOrEmpty(value);
                return true;
            case "enable":
                value = Enabled ? "1" : "0";
                omit = Enabled;
                return true;
            case "folder":
                value = Group ?? string.Empty;
                omit = string.IsNullOrEmpty(value);
                return true;
            case "connect":
                value = Connect ?? string.Empty;
                omit = string.IsNullOrEmpty(value);
                return true;
            case "app":
                value = App ?? string.Empty;
                omit = string.IsNullOrEmpty(value);
                return true;
            case "defaultapp":
                value = DefaultApp ?? string.Empty;
                omit = string.IsNullOrEmpty(value);
                return true;
            case "version":
                value = Version ?? string.Empty;
                omit = string.IsNullOrEmpty(value);
                return true;
            case "additionalparameters":
                value = AdditionalParameters ?? string.Empty;
                omit = string.IsNullOrEmpty(value);
                return true;
            default:
                value = null;
                omit = false;
                return false;
        }
    }

    private static bool ParseBool(string value)
    {
        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => bool.TryParse(value, out var b) && b
        };
    }

    /// <summary>
    /// Преобразует запись в модель Infobase, разбирая строку подключения.
    /// Версия очищается от суффикса разрядности «(32)/(64)», а разрядность
    /// сохраняется в отдельное поле Architecture.
    /// </summary>
    public Infobase ToInfobase()
    {
        var connection = ParseConnection(Connect);

        var version = Version;
        var architecture = string.Empty;
        var end = Version.LastIndexOf(')');
        var start = Version.LastIndexOf('(');
        if (end >= 0 && start >= 0 && start < end)
        {
            var arch = Version.Substring(start + 1, end - start - 1).Trim();
            if (arch == "32" || arch == "64")
            {
                architecture = arch;
                var clean = Version.Substring(0, start).Trim();
                if (!string.IsNullOrWhiteSpace(clean))
                    version = clean;
            }
        }

        return new Infobase
        {
            Name = Name,
            Group = NormalizeGroupPath(Group),
            Connection = connection,
            PlatformVersion = version,
            Architecture = architecture,
            LaunchMode = MapLaunchMode(App, DefaultApp),
            LaunchParameters = AdditionalParameters,
            Description = string.Empty,
            Id = Id
        };
    }

    /// <summary>
    /// Преобразует значения ключей App и DefaultApp из ibases.v8i в режим запуска приложения.
    /// Приоритет отдаётся явно заданному значению App. Если App не задан или равен Auto,
    /// используется режим запуска по умолчанию (DefaultApp). Признак WA (доступность
    /// веб-клиента) не влияет на режим запуска.
    /// </summary>
    private static string MapLaunchMode(string app, string defaultApp)
    {
        // Явно заданный режим запуска имеет приоритет.
        var mapped = MapSingleLaunchMode(app);
        if (mapped != null)
            return mapped;

        // App не задан или равен Auto — используем режим запуска по умолчанию (DefaultApp).
        mapped = MapSingleLaunchMode(defaultApp);
        if (mapped != null)
            return mapped;

        return "Автоматический";
    }

    /// <summary>
    /// Сопоставляет одно значение ключа App/DefaultApp из ibases.v8i каноническому
    /// русскому режиму запуска. Возвращает null, если значение не распознано
    /// (пусто, Auto или иное) — в этом случае применяется режим по умолчанию.
    /// Канонические значения используются для хранения и сравнения и НЕ локализуются.
    /// </summary>
    private static string? MapSingleLaunchMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "thinclient" => "Тонкий клиент",
            "thickclient" => "Толстый клиент",
            "webclient" => "Веб-клиент",
            _ => null
        };
    }

    /// <summary>
    /// Разбирает строку подключения 1С вида:
    /// File="C:\path"  или  Srvr="server";Ref="base";Usr="user";Pwd="pass"
    /// </summary>
    private static ConnectionSettings ParseConnection(string connect)
    {
        var settings = new ConnectionSettings();

        if (string.IsNullOrWhiteSpace(connect))
            return settings;

        // Файловый режим.
        var fileMatch = ExtractQuoted(connect, "File");
        if (fileMatch != null)
        {
            settings.Type = ConnectionType.File;
            settings.FilePath = fileMatch;
            return settings;
        }

        // Клиент-серверный / веб-режим.
        var wsMatch = ExtractQuoted(connect, "WS");
        if (wsMatch != null)
        {
            settings.Type = ConnectionType.WebServer;
            settings.WebUrl = wsMatch;
            return settings;
        }

        settings.Type = ConnectionType.ClientServer;
        // Srvr может быть «host» или «host:port» — порт выносим в отдельное поле.
        ConnectionSettings.ParseServerAndPort(ExtractQuoted(connect, "Srvr"), settings);
        settings.DatabaseName = ExtractQuoted(connect, "Ref") ?? string.Empty;
        settings.User = ExtractQuoted(connect, "Usr") ?? string.Empty;
        settings.Password = ExtractQuoted(connect, "Pwd") ?? string.Empty;
        // Не сбрасываем режим аутентификации в Windows только из-за пустого Usr:
        // в ibases.v8i логин часто отсутствует, а вход запрашивается платформой.
        if (!string.IsNullOrEmpty(settings.User))
            settings.AuthenticationMode = AuthenticationMode.Credentials;
        else
            settings.AuthenticationMode = AuthenticationMode.Prompt;

        return settings;
    }

    /// <summary>
    /// Извлекает значение параметра из строки подключения.
    /// Например, для "Srvr=\"server\"" вернёт "server".
    /// Поддерживает пробелы вокруг знака "=" и значения без кавычек.
    /// </summary>
    private static string? ExtractQuoted(string source, string key)
    {
        // Ищем ключ с возможными пробелами вокруг знака "=".
        var marker = key + "=";
        var idx = source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            // Пробуем вариант с пробелом перед "=" (например, "Srv = \"server\"").
            var spacedMarker = key + " =";
            idx = source.IndexOf(spacedMarker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;
            idx += spacedMarker.Length - 1; // указываем на "="
        }
        else
        {
            idx += marker.Length - 1; // указываем на "="
        }

        var start = idx + 1; // сразу после "="
        if (start >= source.Length)
            return null;

        // Пропускаем пробелы.
        while (start < source.Length && source[start] == ' ')
            start++;

        if (start >= source.Length)
            return null;

        // Значение в кавычках. Удвоенная кавычка («""») внутри значения — это экранированная
        // кавычка (симметрично записи экспортёром); одиночная кавычка закрывает значение.
        if (source[start] == '"')
        {
            var sb = new System.Text.StringBuilder();
            var i = start + 1;
            while (i < source.Length)
            {
                if (source[i] == '"')
                {
                    // Удвоенная кавычка — экранированная кавычка внутри значения.
                    if (i + 1 < source.Length && source[i + 1] == '"')
                    {
                        sb.Append('"');
                        i += 2;
                        continue;
                    }
                    // Одиночная кавычка закрывает значение.
                    break;
                }
                sb.Append(source[i]);
                i++;
            }

            // Дошли до конца строки, не встретив закрывающей кавычки.
            if (i >= source.Length)
                return null;

            return sb.ToString();
        }

        // Значение без кавычек — до точки с запятой или конца строки.
        var valueEnd = source.IndexOf(';', start);
        if (valueEnd < 0)
            valueEnd = source.Length;

        return source.Substring(start, valueEnd - start).Trim();
    }

    /// <summary>Нормализует путь группы: разделители «/» и «\» → внутренний « / », пробелы убираются.</summary>
    private static string NormalizeGroupPath(string group)
    {
        var segments = SplitGroupPath(group);
        return string.Join(GroupHierarchyHelper.PathSeparator, segments);
    }

    /// <summary>Разбивает путь группы на сегменты по разделителям "/" и "\".</summary>
    private static List<string> SplitGroupPath(string path)
    {
        return path
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }
}