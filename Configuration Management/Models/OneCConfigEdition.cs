using System.Collections.Generic;

namespace Configuration_Management.Models;

/// <summary>
/// Редакция / каталог релизов типовой конфигурации 1С. Описывает сегменты web-адреса
/// обновлений по правилу 1С <c>Configs/<Конфигурация>/<Ред>/<Подред>/</c>:
/// имя редакции для отображения и значения сегментов <c>Red</c>/<c>SubRed</c>,
/// которые подставляются в URL. При необходимости можно переопределить полный адрес
/// целиком (ручная ссылка, необязательно).
/// </summary>
public class OneCConfigEdition
{
    /// <summary>Отображаемое имя редакции (например «Релиз 3.0»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Сегмент URL «Ред» (например «3.0»). Может быть пустым — тогда сегмент опускается.</summary>
    public string Red { get; set; } = string.Empty;

    /// <summary>Сегмент URL «Подред» (например «142»). Может быть пустым — тогда сегмент опускается.</summary>
    public string SubRed { get; set; } = string.Empty;

    /// <summary>
    /// Полностью переопределённая ссылка на каталог релизов. Если задана — используется
    /// вместо автоматического формирования из сегментов (ручная корректировка).
    /// </summary>
    public string UrlOverride { get; set; } = string.Empty;

    /// <summary>Возвращает true, если задана ручная ссылка каталога релизов.</summary>
    public bool HasUrlOverride => !string.IsNullOrWhiteSpace(UrlOverride);

    /// <summary>Отображаемое имя редакции (для ComboBox и списков).</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Red : Name;
}