using System.Collections.Generic;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сопоставления имени конфигурации информационной базы с типовой конфигурацией 1С
/// (issue #322): приоритет признаков (точное имя → сегмент URL → вхождение по самому длинному
/// имени), ЗУП определяется как ЗУП, а не как БУХГАЛТЕРИЯ.
/// </summary>
public sealed class ConfigTypeMatcherTests
{
    private static OneCConfigType Config(string code, string name, string urlCode = "") => new()
    {
        Code = code,
        Name = name,
        UrlCode = urlCode,
        Editions = { new OneCConfigEdition { Name = "1.0", Red = "1.0" } },
    };

    [Fact]
    public void ExactName_ZupMatchesZup_NotBukh()
    {
        var configs = new List<OneCConfigType>
        {
            Config("BP", "Бухгалтерия предприятия", "Бухгалтерия предприятия"),
            Config("ZUP", "Зарплата и управление персоналом", "Зарплата и управление персоналом"),
            Config("UT", "Управление торговлей", "Управление торговлей"),
        };

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "Зарплата и управление персоналом");

        Assert.NotNull(match);
        Assert.Equal("ZUP", match!.Code);
        Assert.NotEqual("BP", match.Code);
    }

    [Fact]
    public void BaseNameWithPrefixOrSuffix_ZupStillZup()
    {
        var configs = new List<OneCConfigType>
        {
            Config("BP", "Бухгалтерия предприятия"),
            Config("ZUP", "Зарплата и управление персоналом"),
        };

        // «1С:» префикс и хвост «, редакция 3.1» — вхождение имени типовой в имя базы.
        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "1С:Зарплата и управление персоналом, редакция 3.1");

        Assert.NotNull(match);
        Assert.Equal("ZUP", match!.Code);
    }

    [Fact]
    public void LongestContainedName_Wins_OverShortSubstring()
    {
        // Если имя базы содержит сразу несколько имён типовых, выбирается самое длинное —
        // короткое «Управление» не должно перекрыть «Зарплата и управление персоналом».
        var configs = new List<OneCConfigType>
        {
            Config("SHORT", "Управление"),
            Config("ZUP", "Зарплата и управление персоналом"),
            Config("BP", "Бухгалтерия предприятия"),
        };

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "Конфигурация 1С:Зарплата и управление персоналом");

        Assert.NotNull(match);
        Assert.Equal("ZUP", match!.Code);
    }

    [Fact]
    public void UrlCodeMatch_WhenNamesDiffer()
    {
        // Имя базы совпадает с сегментом URL типовой, хотя имена различаются (приоритет сегмента URL).
        var configs = new List<OneCConfigType>
        {
            Config("MY", "Моя конфигурация", "Бухгалтерия предприятия"),
        };

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "Бухгалтерия предприятия");

        Assert.NotNull(match);
        Assert.Equal("MY", match!.Code);
    }

    [Fact]
    public void EmptyOrNullName_ReturnsNull()
    {
        var configs = new List<OneCConfigType> { Config("BP", "Бухгалтерия предприятия") };

        Assert.Null(ConfigTypeMatcher.FindByInfobaseName(configs, null));
        Assert.Null(ConfigTypeMatcher.FindByInfobaseName(configs, "   "));
        Assert.Null(ConfigTypeMatcher.FindByInfobaseName(configs, string.Empty));
    }

    [Fact]
    public void UnknownName_ReturnsNull()
    {
        var configs = new List<OneCConfigType> { Config("BP", "Бухгалтерия предприятия") };

        Assert.Null(ConfigTypeMatcher.FindByInfobaseName(configs, "Совершенно другая конфигурация"));
    }

    [Fact]
    public void BuiltInSet_ZupVersionedBase_MatchesZupNotBukh()
    {
        // Реальный сценарий из issue #322: база ЗУП 3.1, список типовых — встроенный набор.
        var configs = new List<OneCConfigType>(BuiltInConfigTypes.All);

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "Зарплата и управление персоналом");

        Assert.NotNull(match);
        Assert.Equal("ZUP", match!.Code);
        Assert.Equal("Зарплата и управление персоналом", match.Name);
    }

    [Fact]
    public void CustomOverride_ReplacesBuiltInWithSameCode_Wins()
    {
        // Сценарий 7OH: пользователь отредактировал встроенную ЗУП (0.3.9.265 — правка встроенной
        // создаёт копию OverridesBuiltIn с тем же кодом). Единый загрузчик MergeAll заменяет типовую
        // копией — сопоставление по имени должно выбрать именно пользовательскую запись.
        var builtIn = Config("ZUP", "Зарплата и управление персоналом");
        var customCopy = new OneCConfigType
        {
            Code = "ZUP",
            Name = "Зарплата и управление персоналом",
            OverridesBuiltIn = true,
            Editions = { new OneCConfigEdition { Name = "3.1", Red = "3.1" } },
        };
        var merged = CustomConfigTypesStore.MergeAll(new[] { builtIn }, new[] { customCopy });

        var match = ConfigTypeMatcher.FindByInfobaseName(merged, "Зарплата и управление персоналом");

        Assert.NotNull(match);
        Assert.Equal("ZUP", match!.Code);
        Assert.False(match.IsBuiltIn);
        Assert.True(match.OverridesBuiltIn);
    }

    [Fact]
    public void ExactName_Preferred_OverCustomEntryWithSameUrlSegment()
    {
        // Точный сценарий 7OH: у пользователя нетиповая запись «Моя ЗУП» с сегментом адреса
        // «ЗарплатаИУправлениеПерсоналом», но имя базы точно совпадает с типовой ЗУП (с пробелами).
        // Приоритет НЕ меняется (имя выше сегмента — риск для #323): выбирается типовая по точному
        // имени, а причина совпадения (ExactName) объясняется в окне.
        var configs = new List<OneCConfigType>
        {
            Config("ZUP", "Зарплата и управление персоналом", "Зарплата и управление персоналом"),
            Config("ZUP-MY", "Моя ЗУП", "ЗарплатаИУправлениеПерсоналом"),
        };

        var result = ConfigTypeMatcher.FindMatch(configs, "Зарплата и управление персоналом");

        Assert.NotNull(result);
        Assert.Equal("ZUP", result!.Config.Code);
        Assert.Equal(ConfigMatchKind.ExactName, result.Kind);
    }

    [Fact]
    public void UrlSegmentMatch_Wins_WhenNoExactName()
    {
        // Имя базы не совпало ни с одним именем, но точно совпало с сегментом адреса
        // пользовательской записи «ЗарплатаИУправлениеПерсоналом» — выбирается она,
        // причина — ExactUrlCode.
        var configs = new List<OneCConfigType>
        {
            Config("ZUP", "Зарплата и управление персоналом", "Зарплата и управление персоналом"),
            Config("MY", "Моя конфигурация", "ЗарплатаИУправлениеПерсоналом"),
        };

        var result = ConfigTypeMatcher.FindMatch(configs, "ЗарплатаИУправлениеПерсоналом");

        Assert.NotNull(result);
        Assert.Equal("MY", result!.Config.Code);
        Assert.Equal(ConfigMatchKind.ExactUrlCode, result.Kind);
    }

    [Fact]
    public void MatchKind_ReportsContainmentReason()
    {
        var configs = new List<OneCConfigType> { Config("ZUP", "Зарплата и управление персоналом") };

        var contained = ConfigTypeMatcher.FindMatch(configs, "1С:Зарплата и управление персоналом, редакция 3.1");
        Assert.NotNull(contained);
        Assert.Equal(ConfigMatchKind.NameContainedInBaseName, contained!.Kind);

        var containing = ConfigTypeMatcher.FindMatch(configs, "Зарплата");
        Assert.NotNull(containing);
        Assert.Equal(ConfigMatchKind.BaseNameContainedInConfigName, containing!.Kind);
    }

    [Fact]
    public void FindMatch_ReturnsNull_WhenNoMatch()
    {
        var configs = new List<OneCConfigType> { Config("BP", "Бухгалтерия предприятия") };

        Assert.Null(ConfigTypeMatcher.FindMatch(configs, "Совершенно другая конфигурация"));
        Assert.Null(ConfigTypeMatcher.FindMatch(configs, null));
    }

    [Fact]
    public void BuiltInSet_BukhVariant_MatchesBukh()
    {
        var configs = new List<OneCConfigType>(BuiltInConfigTypes.All);

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "Бухгалтерия предприятия, ред. 3.0");

        Assert.NotNull(match);
        Assert.Equal("BP", match!.Code);
    }

    [Fact]
    public void ConfigNameMatch_ZupInternalName_MatchesZup()
    {
        // Регресс-тест issue #346: имя конфигурации базы из метаданных 1С «ЗарплатаИУправлениеПерсоналом»
        // (без пробелов) не совпадает ни с Name, ни с UrlCode типовой («Зарплата и управление персоналом»),
        // но точно совпадает с внутренним именем ConfigName — сопоставление должно сработать.
        var configs = new List<OneCConfigType>(BuiltInConfigTypes.All);

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "ЗарплатаИУправлениеПерсоналом");

        Assert.NotNull(match);
        Assert.Equal("ZUP", match!.Code);
        Assert.Equal("Зарплата и управление персоналом", match.Name);
    }

    [Fact]
    public void ConfigNameMatch_ReportsExactConfigNameReason()
    {
        var configs = new List<OneCConfigType>(BuiltInConfigTypes.All);

        var result = ConfigTypeMatcher.FindMatch(configs, "ЗарплатаИУправлениеПерсоналом");

        Assert.NotNull(result);
        Assert.Equal("ZUP", result!.Config.Code);
        Assert.Equal(ConfigMatchKind.ExactConfigName, result.Kind);
    }

    [Fact]
    public void ConfigNameExact_Preferred_OverUrlSegmentOnlyEntry()
    {
        // Запись с заполненным ConfigName выигрывает у записи, найденной только по сегменту URL:
        // ConfigName — системное имя метаданных 1С (issue #321/#346), более точный признак, чем
        // устаревший UrlCode. Это осознанное изменение приоритета автосопоставления.
        var configs = new List<OneCConfigType>
        {
            Config("ZUP", "Зарплата и управление персоналом"),
            Config("MY", "Моя конфигурация", "ЗарплатаИУправлениеПерсоналом"),
        };
        configs[0].ConfigName = "ЗарплатаИУправлениеПерсоналом";

        var result = ConfigTypeMatcher.FindMatch(configs, "ЗарплатаИУправлениеПерсоналом");

        Assert.NotNull(result);
        Assert.Equal("ZUP", result!.Config.Code);
        Assert.Equal(ConfigMatchKind.ExactConfigName, result.Kind);
    }

    [Fact]
    public void ExactName_StillPreferred_OverConfigNameOfOtherRecord()
    {
        // Имя базы с пробелами («Зарплата и управление персоналом») точнее совпадает с Name типовой,
        // чем с ConfigName другой записи: приоритет отображаемого имени сохраняется.
        var configs = new List<OneCConfigType>
        {
            Config("ZUP", "Зарплата и управление персоналом"),
            Config("OTHER", "Другая", "прочее"),
        };
        configs[1].ConfigName = "Зарплата и управление персоналом";

        var result = ConfigTypeMatcher.FindMatch(configs, "Зарплата и управление персоналом");

        Assert.NotNull(result);
        Assert.Equal("ZUP", result!.Config.Code);
        Assert.Equal(ConfigMatchKind.ExactName, result.Kind);
    }

    [Fact]
    public void FindEditionByVersion_PrefixMatches()
    {
        // Версия базы «3.1.38.92» → редакция «3.1» (Red хранится целиком, разделять на Ред/Подред
        // не требуется); «3.0.142.32» → редакция «3.0» как при Red «3», так и при Red «3.0».
        var zup = new OneCConfigType
        {
            Code = "ZUP",
            Name = "Зарплата и управление персоналом",
            Editions =
            {
                new OneCConfigEdition { Name = "3.1", Red = "3.1" },
            },
        };

        var edition31 = ConfigTypeMatcher.FindEditionByVersion(zup, "3.1.38.92");
        Assert.NotNull(edition31);
        Assert.Equal("3.1", edition31!.Red);

        var bp = new OneCConfigType
        {
            Code = "BP",
            Name = "Бухгалтерия предприятия",
            Editions =
            {
                new OneCConfigEdition { Name = "3.0", Red = "3", UrlOverride = "https://releases.1c.ru/project/Accounting30" },
                new OneCConfigEdition { Name = "2.0", Red = "2", UrlOverride = "https://releases.1c.ru/project/Accounting20_82" },
            },
        };

        var edition30a = ConfigTypeMatcher.FindEditionByVersion(bp, "3.0.142.32");
        Assert.NotNull(edition30a);
        Assert.Equal("3", edition30a!.Red);

        var bp30red = new OneCConfigType
        {
            Code = "BP",
            Name = "Бухгалтерия предприятия",
            Editions =
            {
                new OneCConfigEdition { Name = "3.0", Red = "3.0", UrlOverride = "https://releases.1c.ru/project/Accounting30" },
                new OneCConfigEdition { Name = "2.0", Red = "2.0", UrlOverride = "https://releases.1c.ru/project/Accounting20_82" },
            },
        };

        var edition30b = ConfigTypeMatcher.FindEditionByVersion(bp30red, "3.0.142.32");
        Assert.NotNull(edition30b);
        Assert.Equal("3.0", edition30b!.Red);
    }

    [Fact]
    public void FindEditionByVersion_NoMatch_ReturnsNull()
    {
        var bp = new OneCConfigType
        {
            Code = "BP",
            Name = "Бухгалтерия предприятия",
            Editions =
            {
                new OneCConfigEdition { Name = "3.0", Red = "3.0" },
                new OneCConfigEdition { Name = "2.0", Red = "2.0" },
            },
        };

        Assert.Null(ConfigTypeMatcher.FindEditionByVersion(bp, "8.3.24.1234"));
        Assert.Null(ConfigTypeMatcher.FindEditionByVersion(bp, "   "));
        Assert.Null(ConfigTypeMatcher.FindEditionByVersion(bp, null));

        var noEditions = new OneCConfigType { Code = "X", Name = "Без редакций" };
        Assert.Null(ConfigTypeMatcher.FindEditionByVersion(noEditions, "1.0.0.1"));

        Assert.Null(ConfigTypeMatcher.FindEditionByVersion(null!, "1.0.0.1"));
    }

    [Fact]
    public void FindEditionByVersion_SubRedDisambiguates()
    {
        // Несколько редакций с одинаковым Red: уточнение по SubRed — следующему сегменту версии
        // («3.1.142.32» → SubRed «142»).
        var config = new OneCConfigType
        {
            Code = "X",
            Name = "Конфигурация",
            Editions =
            {
                new OneCConfigEdition { Name = "3.1", Red = "3.1", SubRed = "1" },
                new OneCConfigEdition { Name = "3.1.142", Red = "3.1", SubRed = "142" },
            },
        };

        var edition = ConfigTypeMatcher.FindEditionByVersion(config, "3.1.142.32");

        Assert.NotNull(edition);
        Assert.Equal("142", edition!.SubRed);
    }
}