namespace Configuration_Management.Services;

/// <summary>
/// Сохранение копии экрана в PNG-файл (функция №30 дорожной карты StartManager).
/// На Windows/WPF захватывается вся область виртуального рабочего стола (все мониторы)
/// через <c>System.Drawing.Graphics.CopyFromScreen</c>; на Linux/Avalonia — снимок главного
/// окна приложения через рендер визуального дерева. Тип один, реализация выбирается
/// символами условной компиляции (#if WINDOWS / #if LINUX).
/// </summary>
public interface IScreenshotService
{
    /// <summary>Доступно ли сохранение копии экрана на текущей платформе. Всегда <c>true</c>.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Захватывает копию экрана и сохраняет её в PNG в указанный каталог.
    /// Пустой каталог — используется каталог «Изображения» по умолчанию.
    /// Имя файла формируется по шаблону <c>screenshot_ГГГГММДД_ЧЧММСС.png</c>.
    /// </summary>
    /// <param name="directory">Каталог сохранения (пусто — «Изображения»).</param>
    /// <returns>Полный путь к сохранённому файлу.</returns>
    string Capture(string directory);
}