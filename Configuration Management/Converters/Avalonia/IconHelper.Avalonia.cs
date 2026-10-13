#if LINUX
using System.Collections.Generic;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Configuration_Management
{
    /// <summary>
    /// Вспомогательный класс для построения векторных иконок (Path + StreamGeometry)
    /// из ресурсов Icons.axaml. Используется вместо эмодзи в UI, собираемом в коде.
    /// Цвет иконки подписывается на ресурс-кисть темы, поэтому иконки автоматически
    /// перекрашиваются при переключении светлой/тёмной схемы.
    /// </summary>
    public static class IconHelper
    {
        /// <summary>
        /// Разрешает StreamGeometry по ключу из Icons.axaml. Резолв учитывает активный
        /// набор иконок (Задача 5): для Outline ищет ключ с суффиксом ".Outline"
        /// (IconsOutline.axaml), при отсутствии — бесшовный fallback на Fluent-геометрию
        /// (Icons.axaml), затем на запасную «папку» (как в IconKeyToGeometryConverter).
        /// </summary>
        public static Geometry? Geometry(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                key = "IconFolder";

            var set = Themes.ThemeFeatureMap.ParseIconSet(
                Application.Current is { } app0
                    && app0.TryGetResource(Themes.ThemeFeatureMap.IconSetNameKey, null, out var setName)
                        ? setName as string
                        : null);

            foreach (var candidate in Themes.ThemeFeatureMap.IconResolutionCandidates(key, set))
            {
                if (Application.Current is { } app &&
                    app.TryGetResource(candidate, null, out var res) && res is Geometry g)
                    return g;
            }

            return null;
        }

        /// <summary>
        /// Создаёт Path-иконку по ключу геометрии. Кисть Fill подписывается на ресурс
        /// <paramref name="brushKey"/> (например "TextPrimaryColorBrush"), поэтому цвет
        /// следует теме. По умолчанию используется кисть основного текста.
        /// </summary>
        /// <param name="subscriptions">
        /// Необязательный приёмник подписки на ресурс-кисть. Нужен там, где иконка
        /// живёт меньше приложения и пересоздаётся: без освобождения подписки
        /// накапливались бы на каждую пересборку.
        /// </param>
        public static Control MakeIcon(string key, double size = 16,
            string brushKey = "TextPrimaryColorBrush")
        {
            var icon = BuildIcon(key, size, out var path);

            if (Application.Current is not null)
            {
                // Динамический ресурс вместо ручной подписки: его отслеживает сам
                // элемент, и при выходе из дерева отслеживание снимается. Прежняя
                // подписка жила у приложения и держала элемент сильной ссылкой,
                // то есть укореняла всё окно, пока её не освободят вручную.
                Themes.ThemeBrushes.Bind(path, Avalonia.Controls.Shapes.Path.FillProperty, brushKey);
            }
            else
            {
                path.Fill = Brushes.Black;
            }

            return icon;
        }

        /// <summary>
        /// То же, но с готовой кистью: нужно там, где цвет задан числом
        /// (в разметке WPF такие значки покрашены явным Foreground).
        /// </summary>
        public static Control MakeIcon(string key, double size, IBrush brush)
        {
            var icon = BuildIcon(key, size, out var path);
            path.Fill = brush;
            return icon;
        }

        /// <summary>
        /// Контур кладётся в неизменный холст 24 на 24 и масштабируется целиком,
        /// как это делает PackIcon в версии для Windows. Без холста Stretch
        /// растягивал каждый значок по его собственным границам: значки с мелким
        /// контуром выглядели крупнее соседних и вставали по вертикали иначе.
        /// </summary>
        public static Control MakeIcon(string key, double size, out Avalonia.Controls.Shapes.Path path)
            => BuildIcon(key, size, out path);

        private static Control BuildIcon(string key, double size, out Avalonia.Controls.Shapes.Path path)
        {
            path = new Avalonia.Controls.Shapes.Path { Data = Geometry(key) };
            var canvas = new Canvas { Width = IconViewportSize, Height = IconViewportSize };
            canvas.Children.Add(path);
            return new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = canvas
            };
        }

        /// <summary>Координатное поле контуров Icons.axaml, оно же viewport PackIcon.</summary>
        private const double IconViewportSize = 24;

        /// <summary>
        /// Строит содержимое кнопки «иконка + подпись» (горизонтальная панель).
        /// </summary>
        public static Control IconAndText(string iconKey, string text, double iconSize = 16,
            string brushKey = "TextPrimaryColorBrush")
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    MakeIcon(iconKey, iconSize, brushKey),
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                }
            };
        }

        /// <summary>
        /// Ключ иконки колонки списка баз. Единый источник истины для заголовков
        /// колонок и списка колонок на вкладке «Отображение», чтобы иконки в обоих
        /// местах совпадали. Неизвестные ключи (например пользовательские в настройках)
        /// получают иконку списка по умолчанию.
        /// </summary>
        public static string ColumnIconKey(string key) => key switch
        {
            "Version" => "IconInfo",
            "Configuration" => "IconCubeOutline",
            "LaunchMode" => "IconPlay",
            // В разметке у этой колонки значок Server, а не шестерёнка
            // (MainWindow.xaml:670): шестерёнка стоит у «Действий».
            "ServerBase" => "IconServer",
            "LastLaunch" => "IconRecent",
            "Size" => "IconDatabase",
            // «Последняя копия» (0.3.9.86) — значок резервной копии со стрелкой.
            "LastBackup" => "IconBackupRestore",
            "Actions" => "IconSettings",
            _ => "IconFormatTitle"
        };

    }
}
#endif