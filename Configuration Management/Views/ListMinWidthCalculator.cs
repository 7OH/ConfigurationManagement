using System;
using System.Collections.Generic;

namespace Configuration_Management
{
    /// <summary>
    /// Расчёт минимальной ширины области списка баз: сумма ширин ВСЕХ видимых колонок
    /// (ведущие кнопки + «Название» + колонки значений, включая «Действия») плюс
    /// боковые отступы. Общий для WPF и Avalonia, покрыт юнит-тестами (issue #309 —
    /// горизонтальный скролл не доходит до последней колонки).
    /// </summary>
    public static class ListMinWidthCalculator
    {
        /// <summary>Спецификация колонки для расчёта: ширина и видимость.</summary>
        public readonly record struct Column(double Width, bool Visible);

        /// <summary>
        /// Сумма ширин видимых колонок. Скрытые и нулевые колонки места не занимают.
        /// </summary>
        public static double SumVisible(IEnumerable<Column> columns)
        {
            if (columns is null)
                throw new ArgumentNullException(nameof(columns));

            double total = 0;
            foreach (var c in columns)
            {
                if (c.Visible && c.Width > 0)
                    total += c.Width;
            }
            return total;
        }

        /// <summary>
        /// Полная минимальная ширина списка: ведущие колонки, минимум «Названия»
        /// (звёздная колонка считается по <paramref name="nameFallback"/>), колонки
        /// значений с «Действиями» и боковые отступы контента.
        /// </summary>
        public static double Compute(
            double nameWidth,
            double nameFallback,
            IEnumerable<Column> leading,
            double padding,
            IEnumerable<Column> values)
        {
            var name = nameWidth > 0 ? nameWidth : nameFallback;
            return name + SumVisible(leading) + padding * 2 + SumVisible(values);
        }

        /// <summary>
        /// Полная минимальная ширина списка с учётом фактической желаемой ширины контента
        /// строк дерева (issue #309). Сумма ширин колонок — нижний предел: реальная строка
        /// может быть шире, когда звёздная колонка «Название» (или заголовок группы)
        /// занимает по содержимому больше расчётного минимума (длинные имена баз), а
        /// прокрутка обязана дотягивать до конца фактического контента, иначе последняя
        /// колонка остаётся лишь частично видимой при максимальном скролле. Если контент
        /// строк уже покрывается суммой колонок, результат не увеличивается — лишняя
        /// горизонтальная полоса не появляется (анти-регресс #255).
        /// </summary>
        public static double Compute(
            double nameWidth,
            double nameFallback,
            IEnumerable<Column> leading,
            double padding,
            IEnumerable<Column> values,
            double actualContentWidth)
        {
            var computed = Compute(nameWidth, nameFallback, leading, padding, values);
            return actualContentWidth > computed ? actualContentWidth : computed;
        }
    }
}