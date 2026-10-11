using System;
using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Имя сохранения одиночного дистрибутива обновления конфигурации (issue #352).
/// Вынесено из окон проверки обновлений (WPF и Avalonia), чтобы оба таргета строили имя
/// одинаково и логика была покрыта тестами.
/// </summary>
public static class UpdateDownloadNaming
{
    /// <summary>
    /// Строит имя сохранения одиночного скачивания (issue #352, 0.3.12.12):
    /// оригинальное имя файла дистрибутива — как на сайте портала
    /// (<c>Trade_11_5_27_98_setup1c.zip</c>), БЕЗ префикса «&lt;Имя&gt;_&lt;версия&gt;_»
    /// (пожелание 7OH от 2026-10-11). «Полный дистрибутив» и «Дистрибутив обновления»
    /// различаются самими оригинальными именами файлов (<c>…_setup1c.zip</c> vs
    /// <c>…_updsetup.cf</c>) и не перезаписывают друг друга.
    /// Без варианта (fallback, когда оригинальное имя файла неизвестно) — прежнее
    /// «&lt;Имя&gt;_&lt;версия&gt;.zip».
    /// </summary>
    /// <param name="infobaseName">Имя информационной базы (только для fallback).</param>
    /// <param name="latestVersion">Последняя версия (пусто → «update»; только для fallback).</param>
    /// <param name="choiceFileName">Оригинальное имя выбранного файла дистрибутива (nullable).</param>
    /// <param name="sanitize">Функция очистки имени от недопустимых символов.</param>
    internal static string BuildDownloadFileName(
        string infobaseName,
        string latestVersion,
        string? choiceFileName,
        Func<string, string> sanitize)
    {
        if (!string.IsNullOrWhiteSpace(choiceFileName))
        {
            var stem = sanitize(Path.GetFileNameWithoutExtension(choiceFileName));
            var extension = Path.GetExtension(choiceFileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".zip";
            if (!string.IsNullOrWhiteSpace(stem))
                return $"{stem}{extension}";
        }

        var baseName = sanitize(infobaseName);
        var latest = string.IsNullOrWhiteSpace(latestVersion) ? "update" : latestVersion;
        return $"{baseName}_{latest}.zip";
    }

    /// <summary>
    /// Путь без молчаливой перезаписи (issue #352, 0.3.12.12): если файл с таким именем
    /// уже существует в каталоге назначения, перед расширением добавляется порядковый
    /// номер в скобках — <c>Trade_…_setup1c (2).zip</c> (классический паттерн «файл уже
    /// существует»). Свободный путь возвращается без изменений. Internal — для юнит-тестов.
    /// </summary>
    internal static string MakeUniquePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2; i <= 999; i++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return path;
    }
}
