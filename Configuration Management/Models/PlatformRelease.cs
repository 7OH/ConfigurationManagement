using System.Collections.Generic;

namespace Configuration_Management.Models;

/// <summary>
/// Версия технологической платформы 1С из каталога <c>releases.1c.ru/project/<ник></c>.
/// Список файлов дистрибутива (<see cref="Files"/>) заполняется лениво из ответа
/// <c>version_files?nick=…&ver=…</c>.
/// </summary>
public class PlatformRelease
{
    /// <summary>Номер версии, например «8.3.27.2214».</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Ссылка на страницу файлов релиза (<c>version_files?nick=…&ver=…</c>).</summary>
    public string VersionFilesUrl { get; set; } = string.Empty;

    /// <summary>Файлы дистрибутива релиза (заполняются лениво).</summary>
    public List<PlatformReleaseFile> Files { get; set; } = new();
}