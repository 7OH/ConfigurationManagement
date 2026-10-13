#if WINDOWS
using System;
using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace Configuration_Management.Converters;

/// <summary>
/// Преобразует строковый ключ иконки каталога меню «Утилиты» в <see cref="PackIconKind"/>
/// (для превью иконок в редакторе структуры, issue #374). Неизвестный/пустой ключ — null
/// (иконка не отображается). Маппинг совпадает с <c>MainWindow.UtilitiesMenu.PackIconKindFromString</c>.
/// </summary>
public class PackIconKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key))
            return null;
        return PackIconKindFromString(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>PackIconKind по строковому ключу каталога; null — неизвестный ключ (без иконки).</summary>
    public static PackIconKind? PackIconKindFromString(string key)
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