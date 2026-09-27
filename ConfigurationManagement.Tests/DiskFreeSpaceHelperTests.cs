using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты помощника «Свободное место на дисках» (<see cref="DiskFreeSpaceHelper"/>,
/// 0.3.9.96): выбор диска по пути, форматирование размера и правило предупреждения.
/// Вместо реальных DriveInfo используется колбэк-резолвер (чистая логика).
/// </summary>
public sealed class DiskFreeSpaceHelperTests
{
    private const long Gb = 1024L * 1024 * 1024;

    // ======================= Выбор диска по пути =======================

    [Fact]
    public void ResolveDriveName_WindowsRoot_ReturnsDriveRoot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(@"C:\", DiskFreeSpaceHelper.ResolveDriveName(@"C:\Program Files\1cv8\base"));
        Assert.Equal(@"D:\", DiskFreeSpaceHelper.ResolveDriveName(@"D:\1C\base\1Cv8.1CD"));
    }

    [Fact]
    public void ResolveDriveName_UncPath_ReturnsShareRoot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Path.GetPathRoot для UNC-пути возвращает «\\server\share» без хвостового слеша.
        Assert.Equal(@"\\server\share", DiskFreeSpaceHelper.ResolveDriveName(@"\\server\share\base"));
    }

    [Fact]
    public void ResolveDriveName_UnixPath_ReturnsRootSlash()
    {
        if (OperatingSystem.IsWindows())
            return; // на Windows корень «/home/...» зависит от интерпретации пути

        Assert.Equal("/", DiskFreeSpaceHelper.ResolveDriveName("/home/user/base"));
    }

    [Fact]
    public void ResolveDriveName_EmptyOrNull_ReturnsNull()
    {
        Assert.Null(DiskFreeSpaceHelper.ResolveDriveName(string.Empty));
        Assert.Null(DiskFreeSpaceHelper.ResolveDriveName("   "));
        Assert.Null(DiskFreeSpaceHelper.ResolveDriveName(null!));
    }

    [Fact]
    public void TryGetInfo_ResolvesByDriveName()
    {
        var resolver = CreateResolver(new Dictionary<string, DiskFreeInfo>
        {
            ["C:\\"] = new(200 * Gb, 50 * Gb)
        });

        var info = DiskFreeSpaceHelper.TryGetInfo(@"C:\bases\buh", resolver);
        Assert.NotNull(info);
        Assert.Equal(200 * Gb, info!.TotalBytes);
        Assert.Equal(50 * Gb, info.FreeBytes);

        // Неизвестный диск — null, а не исключение (тихая деградация).
        Assert.Null(DiskFreeSpaceHelper.TryGetInfo("/home/user/base", resolver));
    }

    [Fact]
    public void TryGetInfo_AnyResolvableRoot_ReturnsDataRegardlessOfPlatform()
    {
        // Корень Unix-пути на разных платформах разный («/» или «\») — резолвер,
        // принимающий любой диск, должен вернуть данные в обоих случаях.
        Func<string, DiskFreeInfo?> anyResolver = _ => new DiskFreeInfo(500L * Gb, 100L * Gb);

        var info = DiskFreeSpaceHelper.TryGetInfo("/home/user/base", anyResolver);
        Assert.NotNull(info);
        Assert.Equal(100L * Gb, info!.FreeBytes);
    }

    [Fact]
    public void TryGetInfo_UnknownDriveOrEmptyPath_ReturnsNull()
    {
        var resolver = CreateResolver(new Dictionary<string, DiskFreeInfo>
        {
            ["C:\\"] = new(100, 50)
        });

        Assert.Null(DiskFreeSpaceHelper.TryGetInfo(string.Empty, resolver));
        Assert.Null(DiskFreeSpaceHelper.TryGetInfo("   ", resolver));
        Assert.Null(DiskFreeSpaceHelper.TryGetInfo(@"Z:\missing", resolver));
    }

    [Fact]
    public void TryGetInfo_ResolverThrows_SilentlyReturnsNull()
    {
        Func<string, DiskFreeInfo?> throwing = _ => throw new IOException("диск недоступен");

        Assert.Null(DiskFreeSpaceHelper.TryGetInfo(@"C:\base", throwing));
    }

    // ======================= Форматирование размера =======================

    [Fact]
    public void FormatBytes_Gigabytes_OneDecimalWithComma()
    {
        Assert.Equal("23,4 ГБ", DiskFreeSpaceHelper.FormatBytes(25_145_738_854));
        Assert.Equal("1,4 ГБ", DiskFreeSpaceHelper.FormatBytes(1_500_000_000));
        // Ровно 10 ГБ — целое число без десятичной части.
        Assert.Equal("10 ГБ", DiskFreeSpaceHelper.FormatBytes(10 * Gb));
    }

    [Fact]
    public void FormatBytes_Megabytes_RoundedToInteger()
    {
        Assert.Equal("512 МБ", DiskFreeSpaceHelper.FormatBytes(512L * 1024 * 1024));
        Assert.Equal("128 МБ", DiskFreeSpaceHelper.FormatBytes(128L * 1024 * 1024));
    }

    [Fact]
    public void FormatBytes_TinyAndNegative_Handled()
    {
        Assert.Equal("0 Б", DiskFreeSpaceHelper.FormatBytes(0));
        Assert.Equal("1 КБ", DiskFreeSpaceHelper.FormatBytes(1024));
        Assert.Equal("1 ТБ", DiskFreeSpaceHelper.FormatBytes(1024L * Gb));
        Assert.Equal("—", DiskFreeSpaceHelper.FormatBytes(-1));
    }

    [Fact]
    public void FormatDisplay_DriveNameFreeAndPercent()
    {
        var info = new DiskFreeInfo(209_715_200_000, 25_145_738_854);

        Assert.Equal("C:\\ — 23,4 ГБ (12%)", DiskFreeSpaceHelper.FormatDisplay(@"C:\", info));
    }

    [Fact]
    public void FormatDisplay_InvalidData_ReturnsDash()
    {
        Assert.Equal("—", DiskFreeSpaceHelper.FormatDisplay(string.Empty, new DiskFreeInfo(100, 50)));
        Assert.Equal("—", DiskFreeSpaceHelper.FormatDisplay(@"C:\", new DiskFreeInfo(0, 0)));
    }

    // ======================= Правило предупреждения =======================

    [Fact]
    public void IsWarning_ExactlyAtThreshold_NotAProblem()
    {
        Assert.False(DiskFreeSpaceHelper.IsWarning(10 * Gb, 10));
    }

    [Fact]
    public void IsWarning_BelowThreshold_IsProblem()
    {
        Assert.True(DiskFreeSpaceHelper.IsWarning(10 * Gb - 1, 10));
        Assert.True(DiskFreeSpaceHelper.IsWarning(5 * Gb, 10));
        Assert.True(DiskFreeSpaceHelper.IsWarning(0, 10));
    }

    [Fact]
    public void IsWarning_ZeroThreshold_Disabled()
    {
        Assert.False(DiskFreeSpaceHelper.IsWarning(1, 0));
        Assert.False(DiskFreeSpaceHelper.IsWarning(0, 0));
        Assert.False(DiskFreeSpaceHelper.IsWarning(null, 0));
    }

    [Fact]
    public void IsWarning_NoDataOrNegative_NeverWarns()
    {
        Assert.False(DiskFreeSpaceHelper.IsWarning(null, 10));
        Assert.False(DiskFreeSpaceHelper.IsWarning(-5, 10));
    }

    /// <summary>Резолвер-фейк: словарь имя диска → снимок.</summary>
    private static Func<string, DiskFreeInfo?> CreateResolver(Dictionary<string, DiskFreeInfo> map) =>
        name => map.TryGetValue(name, out var info) ? info : null;
}