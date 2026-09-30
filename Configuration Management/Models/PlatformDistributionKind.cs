namespace Configuration_Management.Models;

/// <summary>Тип дистрибутива платформы 1С для целевой операционной системы.</summary>
public enum PlatformDistributionKind
{
    /// <summary>zip-архив с setup.exe (клиент/тонкий клиент Windows).</summary>
    WindowsSetupZip,

    /// <summary>Пакет .deb (или .tar.gz с deb внутри) для Linux.</summary>
    LinuxDeb,

    /// <summary>Пакет .rpm для Linux.</summary>
    LinuxRpm,

    /// <summary>Универсальный дистрибутив .tar.gz.</summary>
    LinuxTarGz,

    /// <summary>Прочие форматы дистрибутива.</summary>
    Other,
}