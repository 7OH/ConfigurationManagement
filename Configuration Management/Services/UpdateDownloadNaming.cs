using System;
using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Имя сохранения одиночного дистрибутива обновления конфигурации (issue #352, 0.3.12.6).
/// Вынесено из окон проверки обновлений (WPF и Avalonia), чтобы оба таргета строили имя
/// одинаково и логика была покрыта тестами.
/// </summary>
public static class UpdateDownloadNaming
{
    /// <summary>
    /// Строит имя сохранения одиночного скачивания (issue #352, 0.3.12.6):
    /// «<Имя>_<версия>_<оригинальноеИмя>». Оригинальное имя файла дистрибутива сохраняется
    /// в имени, чтобы «полный дистрибутив» и «дистрибутив обновления» различались и не
    /// перезаписывали друг друга (как у скачивания платформы).
    /// Без варианта (fallback по цепочке) — прежнее «<Имя>_<версия>.zip».
    /// </summary>
    /// <param name="infobaseName">Имя информационной базы.</param>
    /// <param name="latestVersion">Последняя версия (пусто → «update»).</param>
    /// <param name="choiceFileName">Оригинальное имя выбранного файла дистрибутива (nullable).</param>
    /// <param name="sanitize">Функция очистки имени от недопустимых символов.</param>
    internal static string BuildDownloadFileName(
        string infobaseName,
        string latestVersion,
        string? choiceFileName,
        Func<string, string> sanitize)
    {
        var baseName = sanitize(infobaseName);
        var latest = string.IsNullOrWhiteSpace(latestVersion) ? "update" : latestVersion;

        if (!string.IsNullOrWhiteSpace(choiceFileName))
        {
            var stem = sanitize(Path.GetFileNameWithoutExtension(choiceFileName));
            var extension = Path.GetExtension(choiceFileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".zip";
            if (string.IsNullOrWhiteSpace(stem))
                return $"{baseName}_{latest}{extension}";
            return $"{baseName}_{latest}_{stem}{extension}";
        }

        return $"{baseName}_{latest}.zip";
    }
}
