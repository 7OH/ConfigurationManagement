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

    /// <summary>
    /// Ник каталога releases.1c.ru, из которого получен релиз (например,
    /// <c>Platform83</c>/<c>Platform85</c>, issue #334). Используется при построении
    /// адреса страницы файлов версии, если <see cref="VersionFilesUrl"/> не задана:
    /// у версий 8.5 ник Platform85, иначе подстановка Platform83 даёт пустой каталог
    /// («setup.exe не найден в архиве»).
    /// </summary>
    public string Nick { get; set; } = string.Empty;

    /// <summary>Ссылка на страницу файлов релиза (<c>version_files?nick=…&ver=…</c>).</summary>
    public string VersionFilesUrl { get; set; } = string.Empty;

    /// <summary>
    /// Версии, из которых можно обновиться НАПРЯМУЮ до этой версии (колонка
    /// «Список версий» таблицы #versionsTable каталога релизов, issue #352).
    /// Пусто — данные о совместимости отсутствуют (прямой путь неизвестен).
    /// </summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>
    /// True — тестовая (предрелизная) сборка платформы (issue #364): помечается по
    /// маркеру в данных каталога («тестовая версия», «test», «beta», «preview» и т.п.
    /// в строке таблицы #versionsTable или в тексте версии). По умолчанию тестовые
    /// сборки скрыты — окна показывают их только при включённой опции
    /// «Учитывать тестовые» и никогда не предлагают как обязательное обновление.
    /// </summary>
    public bool IsTest { get; set; }

    /// <summary>Файлы дистрибутива релиза (заполняются лениво).</summary>
    public List<PlatformReleaseFile> Files { get; set; } = new();
}