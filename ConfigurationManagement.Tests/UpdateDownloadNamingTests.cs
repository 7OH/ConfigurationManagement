using System;
using System.IO;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты имени сохранения одиночного дистрибутива обновления (issue #352).
/// С 0.3.12.12 файлы сохраняются под ОРИГИНАЛЬНЫМИ именами дистрибутивов
/// (<c>Trade_11_5_27_98_setup1c.zip</c>) — без префикса «&lt;Имя&gt;_&lt;версия&gt;_»
/// (пожелание 7OH от 2026-10-11); при существовании файла добавляется номер
/// в скобках, перезапись не выполняется молча.
/// </summary>
public sealed class UpdateDownloadNamingTests : IDisposable
{
    private static string Identity(string s) => s;

    private readonly string _dir;

    public UpdateDownloadNamingTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"cm_naming_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Не критично для теста.
        }
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    // ======================= BuildDownloadFileName =======================

    [Fact]
    public void BuildDownloadFileName_WithChoice_UsesOriginalNameWithoutVersionPrefix()
    {
        // issue #352 (0.3.12.12): как на сайте — Trade_11_5_27_98_setup1c.zip,
        // без «УТ_11.5.27.98_» в начале (пожелание 7OH от 2026-10-11).
        var name = UpdateDownloadNaming.BuildDownloadFileName(
            "УТ", "11.5.27.98", "Trade_11_5_27_98_setup1c.zip", Identity);

        Assert.Equal("Trade_11_5_27_98_setup1c.zip", name);
        Assert.StartsWith("Trade_11_5_27_98_setup1c", name, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDownloadFileName_FullDistribution_DiffersFromUpdateDistribution()
    {
        // «Полный дистрибутив» и «Дистрибутив обновления» различаются ОРИГИНАЛЬНЫМИ
        // именами файлов на сайте — перезаписи друг друга нет.
        var full = UpdateDownloadNaming.BuildDownloadFileName(
            "УТ", "11.5.27.98", "Trade_11_5_27_98_setup1c.zip", Identity);
        var upd = UpdateDownloadNaming.BuildDownloadFileName(
            "УТ", "11.5.27.98", "Trade_11_5_27_98_updsetup.cf", Identity);

        Assert.Equal("Trade_11_5_27_98_setup1c.zip", full);
        Assert.Equal("Trade_11_5_27_98_updsetup.cf", upd);
        Assert.NotEqual(full, upd);
    }

    [Fact]
    public void BuildDownloadFileName_NoChoice_FallsBackToVersionedZip()
    {
        // Fallback (оригинальное имя файла неизвестно) — прежнее «<Имя>_<версия>.zip».
        var name = UpdateDownloadNaming.BuildDownloadFileName("УТ", "11.5.26.118", null, Identity);

        Assert.Equal("УТ_11.5.26.118.zip", name);
    }

    [Fact]
    public void BuildDownloadFileName_EmptyVersion_WithChoice_StillOriginalName()
    {
        var name = UpdateDownloadNaming.BuildDownloadFileName("УТ", "", "file.cf", Identity);

        Assert.Equal("file.cf", name);
    }

    [Fact]
    public void BuildDownloadFileName_EmptyVersion_NoChoice_UsesUpdatePlaceholder()
    {
        var name = UpdateDownloadNaming.BuildDownloadFileName("УТ", "", null, Identity);

        Assert.Equal("УТ_update.zip", name);
    }

    [Fact]
    public void BuildDownloadFileName_WithChoice_SanitizesFileNameStem()
    {
        var name = UpdateDownloadNaming.BuildDownloadFileName(
            "УТ", "11.5.27.98", "Trade:11|5.cf", s => s.Replace(':', '_').Replace('|', '_'));

        Assert.Equal("Trade_11_5.cf", name);
    }

    [Fact]
    public void BuildDownloadFileName_WithChoice_NoExtension_DefaultsToZip()
    {
        var name = UpdateDownloadNaming.BuildDownloadFileName(
            "УТ", "11.5.27.98", "Trade_11_5_27_98_setup1c", Identity);

        Assert.Equal("Trade_11_5_27_98_setup1c.zip", name);
    }

    // ======================= MakeUniquePath (коллизии имён) =======================

    [Fact]
    public void MakeUniquePath_NoExistingFile_ReturnsOriginalPath()
    {
        var target = PathOf("Trade_11_5_27_98_setup1c.zip");

        Assert.Equal(target, UpdateDownloadNaming.MakeUniquePath(target));
    }

    [Fact]
    public void MakeUniquePath_ExistingFile_AddsOrdinalNumberInParentheses()
    {
        // Файл уже существует — НЕ перезаписываем молча: «file (2).zip».
        var target = PathOf("Trade_11_5_27_98_setup1c.zip");
        File.WriteAllText(target, "уже скачано ранее");

        Assert.Equal(
            PathOf("Trade_11_5_27_98_setup1c (2).zip"),
            UpdateDownloadNaming.MakeUniquePath(target));
    }

    [Fact]
    public void MakeUniquePath_MultipleExistingFiles_UsesNextNumber()
    {
        File.WriteAllText(PathOf("Trade_11_5_27_98_setup1c.zip"), "1");
        File.WriteAllText(PathOf("Trade_11_5_27_98_setup1c (2).zip"), "2");
        File.WriteAllText(PathOf("Trade_11_5_27_98_setup1c (3).zip"), "3");

        Assert.Equal(
            PathOf("Trade_11_5_27_98_setup1c (4).zip"),
            UpdateDownloadNaming.MakeUniquePath(PathOf("Trade_11_5_27_98_setup1c.zip")));
    }

    // ======================= Интеграция с путём сохранения без диалога =======================

    [Fact]
    public void ResolveSaveTarget_ExistingFile_DoesNotOverwrite_UsesNumberedName()
    {
        // issue #352 (0.3.12.12): сохранение в настроенную папку без диалога —
        // существующий файл не перезаписывается, берётся имя «(2)».
        File.WriteAllText(PathOf("Trade_11_5_27_98_setup1c.zip"), "уже скачано ранее");

        var target = UpdateChainDownloadPlanner.ResolveSaveTarget(
            _dir, "Trade_11_5_27_98_setup1c.zip");

        Assert.Equal(PathOf("Trade_11_5_27_98_setup1c (2).zip"), target);
    }
}
