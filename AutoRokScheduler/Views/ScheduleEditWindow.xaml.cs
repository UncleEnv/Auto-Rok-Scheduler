using System;
using System.Collections.Generic;
using System.Linq;
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

        // Reflect the saved days. An empty (or full 7-day) set means "Daily":
        // show it as Daily + every chip lit, so re-editing is not blank.
        var set = new HashSet<DayOfWeek>(model.Days ?? Array.Empty<DayOfWeek>());
        var daily = set.Count == 0 || set.Count == 7;
        ChipDaily.IsChecked = daily;
        foreach (var (chip, day) in _chips) chip.IsChecked = daily || set.Contains(day);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    // Clicking "Daily" selects/clears every day. (Programmatic IsChecked changes
    // don't raise Click, so this won't recurse into Day_Click.)
    private void Daily_Click(object sender, RoutedEventArgs e)
    {
        var on = ChipDaily.IsChecked == true;
        foreach (var (chip, _) in _chips) chip.IsChecked = on;
    }

    // Toggling an individual day keeps "Daily" in sync (lit only when all 7 are on).
    private void Day_Click(object sender, RoutedEventArgs e)
        => ChipDaily.IsChecked = _chips.All(c => c.Chip.IsChecked == true);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var hour = HourCombo.SelectedIndex < 0 ? 0 : HourCombo.SelectedIndex;
        var minute = MinuteCombo.SelectedIndex < 0 ? 0 : MinuteCombo.SelectedIndex;
        _model.TimeOfDay = new TimeOnly(hour, minute);
        _model.Action = ActionCombo.SelectedIndex == 1 ? BotAction.Stop : BotAction.Start;

        var days = new List<DayOfWeek>();
        foreach (var (chip, day) in _chips)
            if (chip.IsChecked == true) days.Add(day);

        if (ChipDaily.IsChecked == true || days.Count == 7)
        {
            // Daily → store empty (the app treats empty as every day).
            _model.Days = Array.Empty<DayOfWeek>();
        }
        else if (days.Count == 0)
        {
            Error("Pick at least one day, or choose Daily.");
            return;
        }
        else
        {
            _model.Days = days.ToArray();
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Error(string msg) => ErrorText.Text = msg;
}
