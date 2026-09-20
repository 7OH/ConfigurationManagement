using System.Collections.Generic;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Чистая логика слотов закладок (1–9) над упорядоченным списком ключей баз.
/// Не зависит от платформы и UI — поэтому покрыта юнит-тестами
/// (см. ConfigurationManagement.Tests/EtapHotkeysFavoritesTests.cs).
///
/// Модель хранения совпадает с существующей: номер слота = индекс в списке + 1,
/// список держит только занятые слоты без «дырок». Источник истины — список
/// <c>AppSettings.FavoriteHotkeyIds</c>; <see cref="Infobase.FavoriteHotkeyNumber"/>
/// является производным значением и пересчитывается методом SyncFavoriteHotkeys().
/// </summary>
public static class BookmarkSlotHelper
{
    /// <summary>Максимальное число слотов закладок (Alt+1…9 / Ctrl+1…9).</summary>
    public const int MaxSlots = 9;

    /// <summary>
    /// Стабильный ключ базы для слота: идентификатор, а при его отсутствии —
    /// «name:Имя». Тот же ключ, что в обеих платформенных реализациях, поэтому
    /// список слотов переносится между Windows и Linux через общий файл настроек.
    /// </summary>
    public static string FavoriteKey(Infobase ib) =>
        !string.IsNullOrEmpty(ib.Id) ? ib.Id : "name:" + ib.Name;

    /// <summary>
    /// Назначает ключу явный слот 1..9 (Ctrl+Shift+N). Ключ помещается на позицию
    /// «номер − 1», при необходимости сдвигая остальные вправо; при превышении лимита
    /// последний слот освобождается. Возвращает true, если слот назначен.
    /// </summary>
    public static bool AssignSlot(IList<string> keys, string key, int number)
    {
        if (number < 1 || number > MaxSlots || string.IsNullOrEmpty(key))
            return false;

        // Убираем ключ из прежней позиции (перемещение внутри списка).
        keys.Remove(key);

        var targetIndex = Math.Min(number - 1, keys.Count);
        keys.Insert(targetIndex, key);

        while (keys.Count > MaxSlots)
            keys.RemoveAt(keys.Count - 1);

        return true;
    }

    /// <summary>
    /// Назначает ключу первый свободный слот (Ctrl+Shift+P, Ctrl+щелчок).
    /// Возвращает true, если слот назначен; false, если все 9 слотов заняты
    /// или ключ пуст.
    /// </summary>
    public static bool AssignNextFreeSlot(IList<string> keys, string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        if (keys.Contains(key))
            return true;
        if (keys.Count >= MaxSlots)
            return false;
        keys.Add(key);
        return true;
    }

    /// <summary>
    /// Снимает закладку: удаляет ключ из списка слотов (номер уходит, избранность
    /// не трогается — «звезда» остаётся, а слот освобождается).
    /// </summary>
    public static bool RemoveFromSlot(IList<string> keys, string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        return keys.Remove(key);
    }

    /// <summary>Очищает все слоты (Ctrl+Alt+X). Избранность баз сохраняется.</summary>
    public static void ClearAllSlots(IList<string> keys) => keys.Clear();

    /// <summary>Возвращает ключ по номеру слота 1..9 или null, если слот пуст.</summary>
    public static string? FindKeyBySlot(IList<string> keys, int number) =>
        number >= 1 && number <= keys.Count ? keys[number - 1] : null;

    /// <summary>Ключи всех занятых слотов в порядке нумерации (копия, чтобы нельзя было мутировать источник).</summary>
    public static List<string> GetAllKeys(IEnumerable<string> keys) => new(keys);
}