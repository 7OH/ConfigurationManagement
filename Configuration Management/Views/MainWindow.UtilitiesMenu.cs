#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management;

/// <summary>
/// WPF-построитель меню «Утилиты» (issue #374): динамическое построение ContextMenu
/// по каталогу <see cref="UtilitiesMenuCatalog"/> + компоновке <see cref="UtilitiesMenuLayout"/>,
/// полученной из <see cref="UtilitiesMenuResolver.Resolve"/>. Заменяет статическое
/// XAML-меню в <c>MainWindow.xaml</c> (обратная совместимость: при выключенной правке
/// структуры меню компоновка — стандартная, и меню выглядит ровно как раньше).
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Строит меню «Утилиты» верхней панели по каталогу+компоновке (issue #374).
    /// Правка структуры выключена (по умолчанию) — стандартная компоновка;
    /// включена — пользовательская из <c>utilities_menu_layout.json</c>. Новые пункты
    /// программы (признак «новинка»), не размещённые в компоновке, автоматически
    /// попадают в подменю «Новинки программы» в конце меню.
    /// </summary>
    private ContextMenu BuildUtilitiesMenu()
    {
        var menu = new ContextMenu
        {
            Style = TryFindResource("ModernContextMenu") as Style,
            DataContext = DataContext,
        };
        if (_viewModel is null)
            return menu;

        var store = AppServices.TryGetService<IUtilitiesMenuLayoutStore>();
        var settings = new AppSettings();
        settings.UtilitiesMenuEditingEnabled = _viewModel.UtilitiesMenuEditingEnabled;
        var layout = UtilitiesMenuResolver.Resolve(settings, store);
        if (layout is null)
            return menu;

        AppendNodes(menu.Items, layout.Root);

        // «Новинки программы» (issue #374): пункты каталога с признаком «новинка»,
        // отсутствующие в компоновке, автоматически попадают в подменю в конце меню,
        // пока пользователь не разместит их в редакторе структуры.
        var newItems = UtilitiesMenuResolver.CollectNewItems(layout);
        if (newItems.Count > 0)
        {
            menu.Items.Add(new Separator());
            var newMenu = new MenuItem
            {
                Header = LocalizationManager.T("Utilities.Submenu.NewItems"),
                Style = TryFindResource("ModernMenuItem") as Style,
            };
            newMenu.Icon = CreateMenuIcon(
                UtilitiesMenuCatalog.GroupWpfIconKind(UtilitiesMenuCatalog.GroupNewItems),
                UtilitiesMenuCatalog.GroupIconColor(UtilitiesMenuCatalog.GroupNewItems));
            foreach (var catalogItem in newItems)
            {
                var node = new UtilitiesMenuNode { Kind = "item", ItemId = catalogItem.Id };
                var item = CreateItem(node);
                if (item is not null)
                    newMenu.Items.Add(item);
            }
            menu.Items.Add(newMenu);
        }

        return menu;
    }

    /// <summary>Рекурсивно добавляет узлы компоновки в целевой список пунктов меню.</summary>
    private void AppendNodes(ItemCollection target, List<UtilitiesMenuNode> nodes)
    {
        if (nodes is null || target is null)
            return;
        foreach (var node in nodes)
        {
            if (node is null || !node.Visible)
                continue;
            if (node.SeparatorBefore && target.Count > 0)
                target.Add(new Separator());
            if (string.Equals(node.Kind, "group", StringComparison.Ordinal))
            {
                var sub = new MenuItem
                {
                    Header = GroupTitle(node),
                    Style = TryFindResource("ModernMenuItem") as Style,
                };
                sub.Icon = GroupIcon(node);
                AppendNodes(sub.Items, node.Children);
                target.Add(sub);
            }
            else
            {
                var item = CreateItem(node);
                if (item is not null)
                    target.Add(item);
            }
        }
    }

    /// <summary>Заголовок группы: пользовательский или локализованный стандартный.</summary>
    private string GroupTitle(UtilitiesMenuNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.CustomTitle))
            return node.CustomTitle!;
        var key = UtilitiesMenuCatalog.GroupTitleKey(node.GroupId ?? string.Empty);
        return string.IsNullOrEmpty(key) ? string.Empty : LocalizationManager.T(key);
    }

    /// <summary>Иконка группы: внешний файл → пользовательская → стандартная каталога.</summary>
    private object? GroupIcon(UtilitiesMenuNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.ExternalIconPath))
            return CreateExternalIcon(node.ExternalIconPath!);
        var iconKey = !string.IsNullOrWhiteSpace(node.IconKey)
            ? node.IconKey!
            : UtilitiesMenuCatalog.GroupWpfIconKind(node.GroupId ?? string.Empty);
        var color = !string.IsNullOrWhiteSpace(node.IconColor)
            ? node.IconColor!
            : UtilitiesMenuCatalog.GroupIconColor(node.GroupId ?? string.Empty);
        return CreateMenuIcon(iconKey, color);
    }

    /// <summary>Создаёт пункт меню по узлу-ссылке на пункт каталога (null — неизвестный пункт).</summary>
    private MenuItem? CreateItem(UtilitiesMenuNode node)
    {
        var catalogItem = UtilitiesMenuCatalog.Find(node.ItemId);
        if (catalogItem is null)
            return null;
        // Пункты только для Avalonia/Linux в WPF-меню не показываются.
        if (catalogItem.LinuxOnly)
            return null;

        var item = new MenuItem
        {
            Header = LocalizationManager.T(catalogItem.TextKey),
            Style = TryFindResource("ModernMenuItem") as Style,
        };
        item.Icon = ItemIcon(node, catalogItem);
        var hotkey = HotkeyFor(catalogItem.HotkeyKey);
        if (!string.IsNullOrWhiteSpace(hotkey))
            item.InputGestureText = hotkey!;
        ApplyCommand(item, catalogItem);
        return item;
    }

    /// <summary>Иконка пункта: внешний файл → пользовательская → из каталога.</summary>
    private object? ItemIcon(UtilitiesMenuNode node, UtilitiesMenuItem catalogItem)
    {
        if (!string.IsNullOrWhiteSpace(node.ExternalIconPath))
            return CreateExternalIcon(node.ExternalIconPath!);
        var iconKey = !string.IsNullOrWhiteSpace(node.IconKey)
            ? node.IconKey!
            : catalogItem.WpfIconKind;
        var color = !string.IsNullOrWhiteSpace(node.IconColor)
            ? node.IconColor!
            : catalogItem.IconColor;
        return CreateMenuIcon(iconKey, color);
    }

    /// <summary>PackIcon 16×16 по ключу иконки; null — без иконки (пустой/неизвестный ключ).</summary>
    private PackIcon? CreateMenuIcon(string iconKey, string colorHex)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
            return null;
        var kind = PackIconKindFromString(iconKey);
        if (kind is null)
            return null;
        return new PackIcon
        {
            Kind = (PackIconKind)kind,
            Width = 16,
            Height = 16,
            Foreground = ParseMenuIconColor(colorHex),
        };
    }

    /// <summary>Изображение из внешнего файла (иконка «извне», назначается в редакторе структуры).</summary>
    private object? CreateExternalIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var uri = new Uri("file:///" + path.Replace("\\", "/"));
            var source = new BitmapImage(uri);
            return new Image { Source = source, Width = 16, Height = 16 };
        }
        catch
        {
            // Нечитаемый/отсутствующий файл иконки не должен ронять построение меню.
            return null;
        }
    }

    /// <summary>Цвет иконки: разбор #RRGGBB; при сбое — серый (как у «Без группы»).</summary>
    private Brush ParseMenuIconColor(string colorHex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(colorHex))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        }
        catch { /* невалидный цвет — серый */ }
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
    }

    /// <summary>Хоткей пункта по имени свойства MainViewModel (из каталога); null — нет хоткея.</summary>
    private string? HotkeyFor(string? hotkeyKey)
    {
        if (string.IsNullOrWhiteSpace(hotkeyKey))
            return null;
        switch (hotkeyKey)
        {
            case "HotkeyCommandPalette": return _viewModel.HotkeyCommandPalette;
            case "HotkeyActualReleases": return _viewModel.HotkeyActualReleases;
            case "HotkeyPlatformUpdate": return _viewModel.HotkeyPlatformUpdate;
            case "HotkeyServerConsole": return _viewModel.HotkeyServerConsole;
            case "HotkeyLockApp": return _viewModel.HotkeyLockApp;
            case "HotkeyRunBackup": return _viewModel.HotkeyRunBackup;
            case "HotkeySessionLock": return _viewModel.HotkeySessionLock;
            default: return null;
        }
    }

    /// <summary>Привязывает команду/действие к пункту меню по виду команды каталога.</summary>
    private void ApplyCommand(MenuItem item, UtilitiesMenuItem catalogItem)
    {
        switch (catalogItem.CommandKind)
        {
            case UtilitiesMenuCommandKind.CommandPalette:
                item.Command = _viewModel.CommandPaletteCommand; break;
            case UtilitiesMenuCommandKind.ShowActualReleases:
                item.Command = _viewModel.ShowActualReleasesCommand; break;
            case UtilitiesMenuCommandKind.ShowPlatformUpdate:
                item.Command = _viewModel.ShowPlatformUpdateCommand; break;
            case UtilitiesMenuCommandKind.ShowPlatformDownload:
                item.Command = _viewModel.ShowPlatformDownloadCommand; break;
            case UtilitiesMenuCommandKind.ServerMonitor:
                item.Command = _viewModel.ServerMonitorCommand; break;
            case UtilitiesMenuCommandKind.OpenServerConsole:
                item.Command = _viewModel.OpenServerConsoleCommand; break;
            case UtilitiesMenuCommandKind.LockApp:
                item.Command = _viewModel.LockAppCommand; break;
            case UtilitiesMenuCommandKind.UnlockPrivateBases:
                item.Command = _viewModel.UnlockPrivateBasesCommand; break;
            case UtilitiesMenuCommandKind.ChangeAppLock:
                item.Command = _viewModel.ChangeAppLockCommand; break;
            case UtilitiesMenuCommandKind.RunBackupScenario:
                item.Command = _viewModel.RunBackupScenarioCommand; break;
            case UtilitiesMenuCommandKind.MaintenanceCenter:
                item.Command = _viewModel.MaintenanceCenterCommand; break;
            case UtilitiesMenuCommandKind.OpenConfigTypesEdit:
                item.Click += (_, _) => _viewModel.OpenConfigTypesEdit(); break;
            case UtilitiesMenuCommandKind.OpenItsAccounts:
                item.Click += (_, _) => _viewModel.OpenItsAccounts(); break;
            case UtilitiesMenuCommandKind.OpenScheduledTasks:
                item.Click += (_, _) => OnScheduledTasksMenuClick(item, null); break;
            case UtilitiesMenuCommandKind.ShowBackupScenarios:
                item.Command = _viewModel.ShowBackupScenariosCommand; break;
            case UtilitiesMenuCommandKind.ShowScriptsSettings:
                item.Command = _viewModel.ShowScriptsSettingsCommand; break;
            case UtilitiesMenuCommandKind.UsageStatistics:
                item.Command = _viewModel.UsageStatisticsCommand; break;
            case UtilitiesMenuCommandKind.ConfigDiff:
                item.Command = _viewModel.ConfigDiffCommand; break;
            case UtilitiesMenuCommandKind.MetadataExplorer:
                item.Command = _viewModel.MetadataExplorerCommand; break;
            case UtilitiesMenuCommandKind.RepositoryBatchUpdate:
                item.Command = _viewModel.RepositoryBatchUpdateCommand; break;
            case UtilitiesMenuCommandKind.RepositoryBrowser:
                item.Command = _viewModel.RepositoryBrowserCommand; break;
            case UtilitiesMenuCommandKind.ExportBasesCsv:
                item.Command = _viewModel.ExportBasesCsvCommand; break;
            case UtilitiesMenuCommandKind.ExportBasesHtml:
                item.Command = _viewModel.ExportBasesHtmlCommand; break;
            case UtilitiesMenuCommandKind.ExportBasesJson:
                item.Command = _viewModel.ExportBasesJsonCommand; break;
            case UtilitiesMenuCommandKind.ImportBasesJson:
                item.Command = _viewModel.ImportBasesJsonCommand; break;
            case UtilitiesMenuCommandKind.ImportClusterInfobases:
                item.Command = _viewModel.ImportClusterInfobasesCommand; break;
            case UtilitiesMenuCommandKind.ProcessInspector:
                item.Command = _viewModel.ProcessInspectorCommand; break;
            case UtilitiesMenuCommandKind.KillOneCProcesses:
                item.Command = _viewModel.KillOneCProcessesCommand; break;
            case UtilitiesMenuCommandKind.ConnectionReplace:
                item.Click += (_, _) => OnUtilitiesConnectionReplace_Click(item, null); break;
            case UtilitiesMenuCommandKind.UndoConnectionReplace:
                item.IsEnabled = _viewModel.CanUndoConnectionReplace;
                item.Click += (_, _) => OnUtilitiesUndoConnectionReplace_Click(item, null); break;
            case UtilitiesMenuCommandKind.RemoveMissingFileBases:
                item.Command = _viewModel.RemoveMissingFileBasesCommand; break;
            case UtilitiesMenuCommandKind.CheckForUpdates:
                item.Click += (_, _) => OnCheckForUpdatesMenuClick(item, null); break;
            case UtilitiesMenuCommandKind.SessionLock:
                item.Command = _viewModel.ShowSessionLockCommand; break;
        }
    }

    /// <summary>PackIconKind по строковому ключу каталога; null — неизвестный ключ (без иконки).</summary>
    private static PackIconKind? PackIconKindFromString(string key)
    {
        switch (key)
        {
            case "Magnify": return PackIconKind.Magnify;
            case "CloudDownload": return PackIconKind.CloudDownload;
            case "PackageVariant": return PackIconKind.PackageVariant;
            case "Download": return PackIconKind.Download;
            case "ServerNetwork": return PackIconKind.ServerNetwork;
            case "Server": return PackIconKind.Server;
            case "Lock": return PackIconKind.Lock;
            case "LockOpenVariant": return PackIconKind.LockOpenVariant;
            case "Key": return PackIconKind.Key;
            case "DatabaseExport": return PackIconKind.DatabaseExport;
            case "MonitorDashboard": return PackIconKind.MonitorDashboard;
            case "AccountKey": return PackIconKind.AccountKey;
            case "CalendarClock": return PackIconKind.CalendarClock;
            case "Cog": return PackIconKind.Cog;
            case "CodeBraces": return PackIconKind.CodeBraces;
            case "History": return PackIconKind.History;
            case "Compare": return PackIconKind.Compare;
            case "Sitemap": return PackIconKind.Sitemap;
            case "SourceBranch": return PackIconKind.SourceBranch;
            case "FileDelimited": return PackIconKind.FileDelimited;
            case "FileDocument": return PackIconKind.FileDocument;
            case "FileExport": return PackIconKind.FileExport;
            case "DatabaseImport": return PackIconKind.DatabaseImport;
            case "ApplicationCog": return PackIconKind.ApplicationCog;
            case "CloseNetwork": return PackIconKind.CloseNetwork;
            case "FindReplace": return PackIconKind.FindReplace;
            case "UndoVariant": return PackIconKind.UndoVariant;
            case "FolderRemove": return PackIconKind.FolderRemove;
            case "Update": return PackIconKind.Update;
            case "Tune": return PackIconKind.Tune;
            case "Information": return PackIconKind.Information;
            case "NewBox": return PackIconKind.NewBox;
            default: return null;
        }
    }
}
#endif
