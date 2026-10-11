namespace Configuration_Management;

/// <summary>
/// Чистая геометрия всплывающей панели внутри границ главного окна (issue #359):
/// меню выбора окна у индикатора фоновых загрузок открывается над индикатором в нижней
/// строке состояния, поэтому по умолчанию его правый край уходит за правый край окна.
/// Класс зажимает позицию панели в прямоугольнике окна. Платформенно-независимый:
/// Windows/WPF (PlacementMode.Custom + CustomPopupPlacementCallback) и Linux/Avalonia
/// (PlacementMode.Custom + CustomPopupPlacementCallback) используют одну и ту же формулу;
/// покрыт юнит-тестами.
/// </summary>
public static class PopupBoundsMath
{
    /// <summary>Отступ панели от края главного окна, DIP.</summary>
    public const double DefaultMargin = 8;

    /// <summary>Зазор между индикатором и верхним краем панели, DIP.</summary>
    public const double DefaultGap = 4;

    /// <summary>
    /// Максимальный размер панели в одном измерении, чтобы она не выходила за границы
    /// окна: размер окна минус отступ с каждой стороны. Некорректный (≤ 0) размер
    /// границ возвращает 0 — тогда ограничение не задаётся.
    /// </summary>
    public static double MaxPopupSize(double boundsSize, double margin)
        => boundsSize <= 0 ? 0 : Math.Max(0, boundsSize - 2 * margin);

    /// <summary>
    /// Левый верхний угол панели, показываемой НАД целью (индикатором), зажатый в границы
    /// окна. Горизонталь: панель ставится по левому краю цели, но не ближе <paramref name="margin"/>
    /// к левому краю окна и не дальше, чем «правый край окна − отступ − ширина панели» —
    /// так длинное меню не вылезает за правый край главного окна. Вертикаль: панель
    /// поднимается над целью на <paramref name="gap"/>, но не выше верхнего края окна
    /// (и не ниже его нижнего края).
    /// </summary>
    /// <param name="targetLeft">Левый край цели в координатах окна.</param>
    /// <param name="targetTop">Верхний край цели в координатах окна.</param>
    /// <param name="popupWidth">Измеренная ширина панели.</param>
    /// <param name="popupHeight">Измеренная высота панели.</param>
    /// <param name="boundsWidth">Ширина границ (главного окна); ≤ 0 — без клампинга по X.</param>
    /// <param name="boundsHeight">Высота границ (главного окна); ≤ 0 — без клампинга по Y.</param>
    /// <param name="margin">Отступ от края окна.</param>
    /// <param name="gap">Зазор между целью и панелью.</param>
    public static (double Left, double Top) PlaceAboveClamped(
        double targetLeft,
        double targetTop,
        double popupWidth,
        double popupHeight,
        double boundsWidth,
        double boundsHeight,
        double margin = DefaultMargin,
        double gap = DefaultGap)
    {
        double left = targetLeft;
        if (boundsWidth > 0)
        {
            var maxLeft = boundsWidth - margin - popupWidth;
            // Панель шире окна (не должно случаться из-за MaxWidth) — прижимаем к левому краю.
            if (maxLeft < margin)
                maxLeft = margin;
            if (left > maxLeft)
                left = maxLeft;
            if (left < margin)
                left = margin;
        }

        double top = targetTop - gap - popupHeight;
        if (boundsHeight > 0)
        {
            if (top < margin)
                top = margin;
            var maxTop = boundsHeight - margin - popupHeight;
            if (maxTop < margin)
                maxTop = margin;
            if (top > maxTop)
                top = maxTop;
        }

        return (left, top);
    }
}
