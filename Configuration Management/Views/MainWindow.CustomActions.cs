#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Интеграция пользовательских действий (функция 7) в контекстное меню дерева
    /// базы/группы (0.3.9.197): наполнение подменю «Пользовательские действия…»
    /// при открытии меню — контекст (одиночная база / мультивыделение / группа),
    /// отбор действий по области (<see cref="CustomActionFilter.SelectActions"/>),
    /// проверка видимости целей (приватные базы скрытого профиля скрывают подменю),
    /// пункт «Настроить действия…» и индикация выполнения. Только Windows/WPF —
    /// файл обёрнут в #if WINDOWS, как остальные MainWindow.*.cs.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Цвет иконок пунктов пользовательских действий (как в разметке меню).</summary>
        private static readonly Brush CustomActionIconBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#06B6D4"));

        /// <summary>
        /// Перестраивает подменю «Пользовательские действия…» для текущего контекста
        /// строки дерева. Вызывается из <see cref="OnBaseContextMenu_Opened"/> в конец
        /// метода (после batch-логики): контекст определяется состоянием VM, действия
        /// отбираются <see cref="CustomActionFilter.SelectActions"/>, цели проверяются на
        /// видимость (та же фильтрация, что и при выполнении — единая политика
        /// приватности). Пустое подменю и его разделитель скрываются явно: общий проход
        /// restricted-режима помечает пункт Tag="User", поэтому финальную видимость
        /// устанавливаем здесь — после общего прохода.
        /// </summary>
        private void PopulateCustomActionsMenu(ContextMenu menu)
        {
            if (_viewModel == null || !menu.Items.Contains(CustomActionsMenu))
            {
                HideCustomActionsBlock();
                return;
            }

            CustomActionContext context;
            string header;
            if (_viewModel.BatchSelectedCount > 1)
            {
                context = CustomActionContext.Batch;
                header = string.Format(
                    LocalizationManager.T("Main.CustomActionsBatchTitle"), _viewModel.BatchSelectedCount);
            }
            else if (_viewModel.SelectedInfobase != null)
            {
                context = CustomActionContext.SingleBase;
                header = LocalizationManager.T("Main.CustomActionsMenu");
            }
            else if (_viewModel.SelectedGroupNode != null)
            {
                context = CustomActionContext.Group;
                header = LocalizationManager.T("Main.CustomActionsMenu");
            }
            else
            {
                HideCustomActionsBlock();
                return;
            }

            var actions = CustomActionFilter.SelectActions(_viewModel.CustomActions, context);
            // Цели контекста из видимых баз: приватные базы скрытого профиля исключаются
            // (GetCustomActionTargets использует ту же фильтрацию, что ExecuteCustomActionAsync).
            if (actions.Count == 0 || _viewModel.GetCustomActionTargets(context).Count == 0)
            {
                HideCustomActionsBlock();
                return;
            }

            // Индикация выполнения: заголовок показывает текст текущего действия,
            // пункты действий недоступны (повторный запуск блокируется и в VM).
            var running = _viewModel.IsCustomActionRunning;
            CustomActionsMenu.Header = running && !string.IsNullOrEmpty(_viewModel.RunningCustomActionText)
                ? _viewModel.RunningCustomActionText
                : header;

            CustomActionsMenu.Items.Clear();
            foreach (var action in actions)
            {
                var item = new MenuItem
                {
                    Header = action.Name,
                    InputGestureText = action.Hotkey,
                    Tag = "User",
                    IsEnabled = !running,
                    Style = TryGetCustomActionMenuItemStyle()
                };
                item.Icon = CreateCustomActionIcon();
                var captured = action;
                item.Click += async (_, _) => await _viewModel.ExecuteCustomActionAsync(captured, context);
                CustomActionsMenu.Items.Add(item);
            }

            CustomActionsMenu.Items.Add(new Separator());

            var settingsItem = new MenuItem
            {
                Header = LocalizationManager.T("Main.CustomActionsSettings"),
                Tag = "User",
                Style = TryGetCustomActionMenuItemStyle()
            };
            settingsItem.Icon = CreateCustomActionSettingsIcon();
            settingsItem.Click += (_, _) => _viewModel.ShowCustomActionsSettingsCommand.Execute(null);
            CustomActionsMenu.Items.Add(settingsItem);

            CustomActionsMenu.Visibility = Visibility.Visible;
            CustomActionsSeparator.Visibility = Visibility.Visible;
        }

        /// <summary>Скрывает подменю и его разделитель (нет контекста, действий или целей).</summary>
        private void HideCustomActionsBlock()
        {
            if (CustomActionsMenu != null)
                CustomActionsMenu.Visibility = Visibility.Collapsed;
            if (CustomActionsSeparator != null)
                CustomActionsSeparator.Visibility = Visibility.Collapsed;
        }

        /// <summary>Стиль пункта контекстного меню из тем; при сбое — стандартный вид.</summary>
        private Style? TryGetCustomActionMenuItemStyle()
        {
            try { return (Style)FindResource("ModernMenuItem"); }
            catch { return null; }
        }

        /// <summary>Иконка пункта действия (CodeBraces, как у «Выполнить скрипт»).</summary>
        private PackIcon CreateCustomActionIcon()
        {
            return new PackIcon
            {
                Kind = PackIconKind.CodeBraces,
                Width = 16,
                Height = 16,
                Foreground = CustomActionIconBrush
            };
        }

        /// <summary>Иконка пункта «Настроить действия…».</summary>
        private PackIcon CreateCustomActionSettingsIcon()
        {
            return new PackIcon
            {
                Kind = PackIconKind.Tune,
                Width = 16,
                Height = 16,
                Foreground = CustomActionIconBrush
            };
        }
    }
}
#endif