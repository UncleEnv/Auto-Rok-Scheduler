using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AutoRokScheduler.Models;

namespace AutoRokScheduler.Views;

public partial class ScheduleEditWindow : Window
{
    private readonly ScheduleEntry _model;

    private readonly (ToggleButton Chip, DayOfWeek Day)[] _chips;

    public ScheduleEditWindow(ScheduleEntry model)
    {
        InitializeComponent();
        _model = model;

        _chips = new[]
        {
            (ChipMon, DayOfWeek.Monday),
            (ChipTue, DayOfWeek.Tuesday),
            (ChipWed, DayOfWeek.Wednesday),
            (ChipThu, DayOfWeek.Thursday),
            (ChipFri, DayOfWeek.Friday),
            (ChipSat, DayOfWeek.Saturday),
            (ChipSun, DayOfWeek.Sunday),
        };

        for (var h = 0; h < 24; h++) HourCombo.Items.Add(h.ToString("00"));
        for (var m = 0; m < 60; m++) MinuteCombo.Items.Add(m.ToString("00"));

        HourCombo.SelectedIndex = model.TimeOfDay.Hour;
        MinuteCombo.SelectedIndex = model.TimeOfDay.Minute;
        ActionCombo.SelectedIndex = model.Action == BotAction.Stop ? 1 : 0;

        var set = new HashSet<DayOfWeek>(model.Days ?? Array.Empty<DayOfWeek>());
        foreach (var (chip, day) in _chips) chip.IsChecked = set.Contains(day);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var hour = HourCombo.SelectedIndex < 0 ? 0 : HourCombo.SelectedIndex;
        var minute = MinuteCombo.SelectedIndex < 0 ? 0 : MinuteCombo.SelectedIndex;
        _model.TimeOfDay = new TimeOnly(hour, minute);
        _model.Action = ActionCombo.SelectedIndex == 1 ? BotAction.Stop : BotAction.Start;

        var days = new List<DayOfWeek>();
        foreach (var (chip, day) in _chips)
            if (chip.IsChecked == true) days.Add(day);
        // Empty or all-seven both mean "daily"; store empty for daily.
        _model.Days = (days.Count == 0 || days.Count == 7) ? Array.Empty<DayOfWeek>() : days.ToArray();

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
