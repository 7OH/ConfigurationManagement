#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management;

/// <summary>
/// Окно «Блокировка сеансов информационной базы» (Avalonia/Linux). Функция №20 StartManager,
/// CTRL+ALT+L: задание времени начала (+5 мин), длительности (30 мин) и текста сообщения
/// с параметрами {ДатаНач}/{ДатаКон}, шаблоны «Технические работы» и «Обновление ИБ»,
/// установка либо снятие блокировки сеансов файловой ИБ без открытия «1С:Предприятия».
/// </summary>
public sealed class SessionLockWindow : ModalWindowBase
{
    private readonly Infobase _infobase;
    private readonly ISessionLockService _service;
    private readonly IDialogService _dialog;
    private readonly List<string> _templates = new();
    private readonly DatePicker _datePicker = new();
    private readonly TextBox _startTimeBox = new();
    private readonly TextBox _durationBox = new();
    private readonly ComboBox _templateBox = new();
    private readonly TextBox _messageTextBox = new();
    private readonly Button _unlockButton = new();
    private readonly Button _lockButton = new();

    /// <summary>true — пользователь запросил снятие блокировки.</summary>
    public bool UnlockRequested { get; private set; }

    /// <summary>Введённые параметры блокировки (после «Заблокировать»).</summary>
    public SessionLockOptions? Options { get; private set; }

    public SessionLockWindow(Infobase infobase)
    {
        _infobase = infobase;
        _service = AppServices.GetRequiredService<ISessionLockService>();
        _dialog = AppServices.GetRequiredService<IDialogService>();

        Title = string.Format(T("SessionLock.TitleForBase"), infobase.Name);
        Width = 520;
        Height = 420;
        MinWidth = 500;
        MinHeight = 400;
        FontSize = 13;
        Content = BuildRoot();
        LoadTemplates();
        ResetToDefaults();
    }

    private static string T(string key) => LocalizationManager.T(key);

    private Control BuildRoot()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };

        var desc = new TextBlock
        {
            Text = string.Format(T("SessionLock.DescriptionForBase"), _infobase.Name),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        ThemeBrushes.Bind(desc, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        stack.Children.Add(desc);

        stack.Children.Add(FieldLabel(T("SessionLock.StartTime")));
        var startRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        _datePicker.SelectedDate = DateTime.Now.AddMinutes(5);
        _startTimeBox.Text = DateTime.Now.AddMinutes(5).ToString("HH:mm");
        startRow.Children.Add(_datePicker);
        Grid.SetColumn(_startTimeBox, 1);
        _startTimeBox.Width = 110;
        _startTimeBox.Margin = new Thickness(8, 0, 0, 0);
        startRow.Children.Add(_startTimeBox);
        stack.Children.Add(startRow);

        stack.Children.Add(FieldLabel(T("SessionLock.Duration")));
        var durationRow = new StackPanel { Orientation = Orientation.Horizontal };
        _durationBox.Text = "30";
        durationRow.Children.Add(_durationBox);
        var unitLabel = new TextBlock
        {
            Text = T("SessionLock.Minutes"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        ThemeBrushes.Bind(unitLabel, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        durationRow.Children.Add(unitLabel);
        stack.Children.Add(durationRow);

        stack.Children.Add(FieldLabel(T("SessionLock.Template")));
        _templateBox.SelectionChanged += (_, _) => ApplyTemplate(_templateBox.SelectedIndex);
        stack.Children.Add(_templateBox);

        stack.Children.Add(FieldLabel(T("SessionLock.Message")));
        _messageTextBox.AcceptsReturn = true;
        _messageTextBox.TextWrapping = TextWrapping.Wrap;
        _messageTextBox.Height = 90;
        stack.Children.Add(_messageTextBox);

        var hint = new TextBlock
        {
            Text = T("SessionLock.PlaceholdersHint"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8)
        };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        stack.Children.Add(hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _unlockButton.Content = T("SessionLock.Unlock");
        _unlockButton.Click += OnUnlock_Click;
        _lockButton.Content = T("SessionLock.Lock");
        _lockButton.Click += OnLock_Click;
        foreach (var b in new Control[] { _unlockButton, _lockButton })
            b.Width = 150;
        _unlockButton.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(_unlockButton);
        buttons.Children.Add(_lockButton);
        stack.Children.Add(buttons);

        return stack;
    }

    private static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private void LoadTemplates()
    {
        _templates.Clear();
        _templates.Add(T("SessionLock.TemplateMaintenance"));
        _templates.Add(T("SessionLock.TemplateUpdate"));
        _templateBox.Items.Clear();
        foreach (var tpl in _templates)
            _templateBox.Items.Add(tpl);
        _templateBox.SelectedIndex = 0;
    }

    private void ResetToDefaults()
    {
        ApplyTemplate(0);
    }

    private void ApplyTemplate(int index)
    {
        if (index < 0 || index >= _templates.Count)
            return;
        if (string.Equals(_templates[index], T("SessionLock.TemplateMaintenance"), StringComparison.Ordinal))
            _messageTextBox.Text = T("SessionLock.MessageMaintenance");
        else if (string.Equals(_templates[index], T("SessionLock.TemplateUpdate"), StringComparison.Ordinal))
            _messageTextBox.Text = T("SessionLock.MessageUpdate");
    }

    private async void OnLock_Click(object? sender, RoutedEventArgs e)
    {
        var options = BuildOptions();
        if (options is null)
            return;

        IsEnabled = false;
        try
        {
            var result = await _service.LockAsync(_infobase, options);
            if (result.Success)
            {
                Options = options;
                Close();
            }
            else
            {
                _dialog.ShowError(result.ErrorMessage ?? T("SessionLock.Failed"), T("SessionLock.Title"));
            }
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void OnUnlock_Click(object? sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        try
        {
            var result = await _service.UnlockAsync(_infobase);
            if (result.Success)
            {
                UnlockRequested = true;
                Close();
            }
            else
            {
                _dialog.ShowError(result.ErrorMessage ?? T("SessionLock.UnlockFailed"), T("SessionLock.Title"));
            }
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private SessionLockOptions? BuildOptions()
    {
        if (!TimeSpan.TryParse(_startTimeBox.Text?.Trim(), out var time))
        {
            _dialog.ShowWarning(T("SessionLock.InvalidStartTime"), T("SessionLock.Title"));
            return null;
        }

        if (!int.TryParse(_durationBox.Text?.Trim(), out var minutes) || minutes <= 0)
        {
            _dialog.ShowWarning(T("SessionLock.InvalidDuration"), T("SessionLock.Title"));
            return null;
        }

        return new SessionLockOptions
        {
            StartTime = BuildStartTime(time),
            Duration = TimeSpan.FromMinutes(minutes),
            Message = _messageTextBox.Text ?? string.Empty
        };
    }

    private DateTime BuildStartTime(TimeSpan time)
    {
        if (_datePicker.SelectedDate is { } offset)
            return offset.Date + time;
        return DateTime.Now.Date.AddMinutes(5);
    }
}
#endif