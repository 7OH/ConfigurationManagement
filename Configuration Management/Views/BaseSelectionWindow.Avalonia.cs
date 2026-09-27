#if LINUX
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Configuration_Management.Localization;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно выбора баз (Avalonia/Linux-версия <see cref="BaseSelectionWindow"/>):
    /// список строк с флажками, «Выделить все/Снять все», OK/Отмена.
    /// </summary>
    public partial class BaseSelectionWindow : ModalWindowBase
    {
        private readonly List<BaseSelectionItem> _items;
        private readonly TextBlock _hint;

        public BaseSelectionWindow(string title, string hint, IEnumerable<BaseSelectionItem> items)
        {
            AvaloniaXamlLoader.Load(this);
            Title = title;
            _hint = this.FindControl<TextBlock>("HintText")!;
            _hint.Text = hint;

            _items = items.ToList();
            this.FindControl<ListBox>("ItemsList")!.ItemsSource = _items;

            // «Выделить все / Снять все» — две вторичные кнопки в ряд.
            var bulk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            bulk.Children.Add(BuildSecondaryButton(LocalizationManager.T("Selection.SelectAll"), () => SetAll(true)));
            bulk.Children.Add(BuildSecondaryButton(LocalizationManager.T("Selection.None"), () => SetAll(false)));
            this.FindControl<ContentControl>("BulkHost")!.Content = bulk;

            this.FindControl<ContentControl>("ButtonsHost")!.Content =
                BuildButtons(LocalizationManager.T("Common.Ok"), 130, OnOk_Click);
        }

        /// <summary>Отмеченные строки (в порядке списка).</summary>
        public List<BaseSelectionItem> GetSelected() =>
            _items.Where(i => i.IsChecked).ToList();

        private void SetAll(bool value)
        {
            foreach (var item in _items)
                item.IsChecked = value;
        }

        private void OnOk_Click()
        {
            if (GetSelected().Count == 0)
            {
                AppServices.GetRequiredService<Services.IDialogService>()
                    .ShowInfo(LocalizationManager.T("Selection.NothingSelected"), Title);
                return;
            }
            DialogResult = true;
            Close();
        }

        private Button BuildSecondaryButton(string text, System.Action onClick)
        {
            var button = new Button { Content = text, Padding = new Avalonia.Thickness(12, 6) };
            button.Styled(Themes.ControlThemes.SecondaryButton);
            button.Click += (_, _) => onClick();
            return button;
        }
    }
}
#endif
