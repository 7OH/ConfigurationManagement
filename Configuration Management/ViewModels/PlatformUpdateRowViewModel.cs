using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка окна «Обновление платформы 1С»: версия из объединения установленных и
/// доступных версий технологической платформы 1С, признак «есть обновление»,
/// новейшая доступная версия, данные релиза каталога (файлы дистрибутива
/// подгружаются лениво), размер выбранного файла дистрибутива и число совместимых
/// информационных баз репозитория. Чистый класс без платформенных зависимостей —
/// используется и Windows/WPF, и Linux/Avalonia.
/// </summary>
public sealed class PlatformUpdateRowViewModel : ViewModelBase
{
    /// <summary>Пустой размер (файлы дистрибутива ещё не загружены либо не выбраны).</summary>
    public const string EmptySizeText = "—";

    private readonly Func<IReadOnlyList<PlatformReleaseFile>, PlatformReleaseFile?> _picker;
    private PlatformRelease? _release;
    private int _compatibleBases;
    private bool _isChecking;
    private bool _isDownloading;
    private double _progress;

    /// <summary>Номер версии, например «8.3.27.2214».</summary>
    public string Version { get; }

    /// <summary>True — версия установлена на этой машине.</summary>
    public bool IsInstalled { get; }

    /// <summary>True — установленная версия, для которой в каталоге есть более новая.</summary>
    public bool HasUpdate { get; }

    /// <summary>Новейшая доступная версия для установленной строки; null — обновления нет.</summary>
    public string? AvailableVersion { get; }

    /// <summary>Данные релиза из каталога портала. Может быть null (версия не найдена
    /// в каталоге); файлы дистрибутива (<see cref="PlatformRelease.Files"/>) подгружаются
    /// лениво вызовом <see cref="IPlatformUpdateService.LoadReleaseFilesAsync"/>.</summary>
    public PlatformRelease? Release => _release;

    /// <summary>Число информационных баз репозитория, совместимых с этой версией
    /// (<see cref="PlatformUpdateMatcher.CountCompatibleBases"/>).</summary>
    public int CompatibleBases
    {
        get => _compatibleBases;
        set => SetProperty(ref _compatibleBases, value);
    }

    /// <summary>True — выполняется сетевая проверка строки.</summary>
    public bool IsChecking
    {
        get => _isChecking;
        set
        {
            if (SetProperty(ref _isChecking, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>True — выполняется загрузка дистрибутива этой версии.</summary>
    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (SetProperty(ref _isDownloading, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Прогресс загрузки строки (0..1).</summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, Math.Clamp(value, 0, 1));
    }

    /// <summary>Размер выбранного файла дистрибутива в человекочитаемом виде;
    /// <see cref="EmptySizeText"/>, если файлы релиза не загружены или подходящего нет.</summary>
    public string SizeText
    {
        get
        {
            if (_release is null || _release.Files.Count == 0)
                return EmptySizeText;

            var picked = _picker(_release.Files);
            return picked is null ? EmptySizeText : Infobase.FormatSize(picked.SizeBytes);
        }
    }

    /// <summary>Локализованный текст статуса строки (ключи «PlatformUpdate.Status.*»):
    /// приоритет у активных состояний (проверка/загрузка), затем «есть обновление»,
    /// «установлена», иначе — «доступна».</summary>
    public string StatusText
    {
        get
        {
            if (IsChecking)
                return LocalizationManager.T("PlatformUpdate.Status.Checking");
            if (IsDownloading)
                return LocalizationManager.T("PlatformUpdate.Status.Downloading");
            if (HasUpdate)
                return LocalizationManager.T("PlatformUpdate.Status.UpdateAvailable");
            if (IsInstalled)
                return LocalizationManager.T("PlatformUpdate.Status.Installed");
            return LocalizationManager.T("PlatformUpdate.Status.Available");
        }
    }

    /// <param name="match">Результат сопоставления установленных и доступных версий.</param>
    /// <param name="release">Данные релиза каталога (может быть null).</param>
    /// <param name="picker">Выбор файла дистрибутива для размера; null — первый файл списка.</param>
    public PlatformUpdateRowViewModel(
        PlatformUpdateMatch match,
        PlatformRelease? release,
        Func<IReadOnlyList<PlatformReleaseFile>, PlatformReleaseFile?>? picker = null)
    {
        ArgumentNullException.ThrowIfNull(match);

        Version = match.Version ?? string.Empty;
        IsInstalled = match.IsInstalled;
        HasUpdate = match.HasUpdate;
        AvailableVersion = match.AvailableVersion;
        _release = release;
        _picker = picker ?? (files => files.FirstOrDefault());
    }

    /// <summary>Применяет данные релиза (после <c>LoadReleaseFilesAsync</c>) и пересчитывает размер.</summary>
    public void ApplyRelease(PlatformRelease release)
    {
        _release = release ?? throw new ArgumentNullException(nameof(release));
        OnPropertyChanged(nameof(SizeText));
    }

    /// <summary>Уведомляет UI об изменении состава файлов релиза (размер мог измениться).</summary>
    public void NotifyFilesChanged() => OnPropertyChanged(nameof(SizeText));
}