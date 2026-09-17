#if LINUX
using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Configuration_Management.Services;

/// <summary>
/// Сохранение копии экрана на Linux/Avalonia (функция №30 дорожной карты). Поскольку на
/// Linux без внешних зависимостей сложно захватить весь экран, реализуется снимок главного
/// окна приложения через рендер визуального дерева в <see cref="RenderTargetBitmap"/>.
/// Результат сохраняется в PNG.
/// </summary>
public sealed class ScreenshotService : IScreenshotService
{
    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public string Capture(string directory)
    {
        var dir = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ConfigurationManagement")
            : directory;
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        var window = FindWindow()
            ?? throw new InvalidOperationException("Не найдено окно приложения для снимка экрана.");

        var pixelSize = new PixelSize(
            Math.Max(1, (int)window.ClientSize.Width),
            Math.Max(1, (int)window.ClientSize.Height));
        var dpi = new Vector(window.RenderScaling, window.RenderScaling);

        using var rtb = new RenderTargetBitmap(pixelSize, dpi);
        rtb.Render(window);
        rtb.Save(filePath);
        return filePath;
    }

    /// <summary>Ищет активное или первое видимое окно приложения для снимка.</summary>
    private static Window? FindWindow()
    {
        if (Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        // Сначала активное окно, иначе — первое видимое.
        return desktop.Windows.FirstOrDefault(w => w.IsActive && w.IsVisible)
               ?? desktop.Windows.FirstOrDefault(w => w.IsVisible);
    }
}
#endif