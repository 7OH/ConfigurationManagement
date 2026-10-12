namespace Configuration_Management;

/// <summary>
/// Чистые расчёты размеров/позиции окон (регресс «пустого незакрываемого окна»,
/// issues #308/#305). Авторазмер через <c>SizeToContent=Height</c> оказался хрупким:
/// при модальном показе окно могло схлопываться в пустой прямоугольник с «пропавшими»
/// надписями. Вместо него окна вычисляют нужную высоту явно (по измерению контента) и
/// клампят её в <c>[MinHeight, MaxHeight]</c>, а затем поднимают окно, если его низ
/// вышел за нижний край рабочей области. Класс не зависит от UI-платформы —
/// Windows/WPF и Linux/Avalonia используют одну логику; покрыт юнит-тестами.
/// </summary>
public static class WindowSizeMath
{
    /// <summary>
    /// Высота окна по желаемой высоте контента с ограничениями окна.
    /// Значения <c><= 0</c> означают «ограничение не задано».
    /// </summary>
    public static double ClampHeight(double desiredContent, double min, double max)
    {
        var result = desiredContent;
        if (min > 0 && result < min)
            result = min;
        if (max > 0 && result > max)
            result = max;
        return result;
    }

    /// <summary>
    /// Вертикальная позиция окна: если низ окна выходит за нижний край рабочей области,
    /// окно поднимается (но не выше верхнего края). Возвращает новое значение <c>Top</c>.
    /// При некорректной рабочей области возвращает исходное значение.
    /// </summary>
    public static double FitTop(double top, double height, double workTop, double workBottom)
    {
        if (workBottom <= workTop)
            return top;
        if (top + height > workBottom)
            return Math.Max(workTop, workBottom - height);
        return top;
    }

    /// <summary>
    /// Границы maximized-окна (в физических пикселях монитора) так, чтобы окно легло
    /// строго на рабочую область <paramref name="work"/> монитора
    /// <paramref name="monitor"/> (без панели задач/доков). Положение — смещение
    /// верхнего левого угла рабочей области относительно верхнего левого угла
    /// монитора; размер — сама рабочая область. Верно для панели задач с любой
    /// стороны и для мониторов со смещёнными/отрицательными координатами
    /// (issue #368: максимизация второстепенных окон уходила за панель задач).
    /// Прямоугольники задаются как <c>(Left, Top, Right, Bottom)</c> — как Win32 RECT.
    /// </summary>
    public static void ComputeMaximizedBounds(
        (int Left, int Top, int Right, int Bottom) monitor,
        (int Left, int Top, int Right, int Bottom) work,
        out int x, out int y, out int width, out int height)
    {
        x = work.Left - monitor.Left;
        y = work.Top - monitor.Top;
        width = work.Right - work.Left;
        height = work.Bottom - work.Top;
    }
}