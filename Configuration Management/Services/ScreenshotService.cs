#if WINDOWS
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Configuration_Management.Services;

/// <summary>
/// Сохранение копии экрана на Windows (функция №30 дорожной карты): захват всей области
/// виртуального рабочего стола (всех мониторов) через <see cref="Graphics.CopyFromScreen"/>
/// и сохранение в PNG. Не требует прав администратора.
/// </summary>
public sealed class ScreenshotService : IScreenshotService
{
    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public string Capture(string directory)
    {
        var dir = string.IsNullOrWhiteSpace(directory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            : directory;
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        // Вся область виртуального рабочего стола (объединение всех мониторов).
        var bounds = SystemInformation.VirtualScreen;
        var width = Math.Max(1, bounds.Width);
        var height = Math.Max(1, bounds.Height);

        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bitmap.Size);
        }
        bitmap.Save(filePath, ImageFormat.Png);
        return filePath;
    }
}
#endif