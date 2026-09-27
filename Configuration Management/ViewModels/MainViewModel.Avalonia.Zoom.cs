#if LINUX
using System;
using System.Windows.Input;
using Configuration_Management.Controls;
using Configuration_Management.Themes;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Масштаб строк списка баз (issue #303): база области шрифта «Список баз»
/// (иначе «По умолчанию», эталон 13) в сочетании с масштабом Ctrl+колеса мыши
/// и горячими клавишами (Ctrl++ / Ctrl+- / Ctrl+0, как в редакторах).
/// Частичный класс <see cref="MainViewModel"/> для Avalonia/Linux.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _zoomInCommand;
    private ICommand? _zoomOutCommand;
    private ICommand? _zoomResetCommand;

    /// <summary>Масштаб строк списка (issue #303): 1.0 — обычный размер, 0.5–3.0.</summary>
    public double ListZoomFactor
    {
        get => _settings.ListZoomFactor;
        set
        {
            var clamped = Math.Clamp(Math.Round(value * 10) / 10.0, 0.5, 3.0);
            if (Math.Abs(_settings.ListZoomFactor - clamped) < 0.001)
                return;
            _settings.ListZoomFactor = clamped;
            OnPropertyChanged(nameof(ListZoomFactor));
            ApplyListZoom();
            SaveSettingsSilently();
        }
    }

    /// <summary>Горячая клавиша увеличения масштаба строк списка (issue #303).</summary>
    public string HotkeyZoomIn => _settings.HotkeyZoomIn;

    /// <summary>Горячая клавиша уменьшения масштаба строк списка (issue #303).</summary>
    public string HotkeyZoomOut => _settings.HotkeyZoomOut;

    /// <summary>Горячая клавиша сброса масштаба строк списка (issue #303).</summary>
    public string HotkeyZoomReset => _settings.HotkeyZoomReset;

    /// <summary>Команда увеличения масштаба строк списка (Ctrl++ / Ctrl+колесо).</summary>
    public ICommand ZoomInCommand => _zoomInCommand ??= new RelayCommand(() => ListZoomFactor += 0.1);

    /// <summary>Команда уменьшения масштаба строк списка (Ctrl+-).</summary>
    public ICommand ZoomOutCommand => _zoomOutCommand ??= new RelayCommand(() => ListZoomFactor -= 0.1);

    /// <summary>Команда сброса масштаба строк списка (Ctrl+0).</summary>
    public ICommand ZoomResetCommand => _zoomResetCommand ??= new RelayCommand(() => ListZoomFactor = 1.0);

    /// <summary>Меняет масштаб строк списка на один шаг (Ctrl+колесо мыши).</summary>
    public void ZoomListBy(double delta) => ListZoomFactor += delta;

    /// <summary>
    /// Применяет текущий масштаб строк к метрикам UI и перестраивает дерево:
    /// строки строятся кодом с явными размерами, поэтому привязками не обойтись.
    /// </summary>
    public void ApplyListZoom()
    {
        UiMetrics.UserRowScale = ComputeRowScale();
        RebuildTree();
    }

    /// <summary>Итоговый масштаб строк: база области шрифта к эталону 13 × масштаб.</summary>
    private double ComputeRowScale()
    {
        double baseSize = 13.0;
        var fonts = _settings.ElementFonts;
        if (fonts is not null)
        {
            if (fonts.TryGetValue(ThemeManager.FontList, out var list) && list is { FontSize: > 0 })
                baseSize = list.FontSize;
            else if (fonts.TryGetValue(ThemeManager.FontDefault, out var def) && def is { FontSize: > 0 })
                baseSize = def.FontSize;
        }
        return baseSize / 13.0 * _settings.ListZoomFactor;
    }
}
#endif
