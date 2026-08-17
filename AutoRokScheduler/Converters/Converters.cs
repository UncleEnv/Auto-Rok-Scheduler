using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AutoRokScheduler.Models;
using AutoRokScheduler.ViewModels;

namespace AutoRokScheduler.Converters;

internal static class BrushHelper
{
    public static Brush Res(string key)
        => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>RunState → status colour (running=green, stopped=red, etc.).</summary>
public sealed class RunStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value is RunState rs ? rs : RunState.Unknown;
        return s switch
        {
            RunState.Running => BrushHelper.Res("RunningBrush"),
            RunState.Stopped => BrushHelper.Res("StoppedBrush"),
            RunState.Working => BrushHelper.Res("AccentBrush"),
            RunState.Error => BrushHelper.Res("StoppedBrush"),
            _ => BrushHelper.Res("IdleBrush")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>LogLevel → text colour.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var lvl = value is LogLevel l ? l : LogLevel.Info;
        return lvl switch
        {
            LogLevel.Success => BrushHelper.Res("RunningBrush"),
            LogLevel.Warn => BrushHelper.Res("WarnBrush"),
            LogLevel.Error => BrushHelper.Res("StoppedBrush"),
            _ => BrushHelper.Res("MutedBrush")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>null → Collapsed, non-null → Visible.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value == null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>null → Visible (placeholder), non-null → Collapsed.</summary>
public sealed class NullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value == null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// int count → Visibility. Default: Visible when count &gt; 0.
/// ConverterParameter="zero" inverts it → Visible only when count == 0 (empty-state text).
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var whenZero = string.Equals(parameter as string, "zero", StringComparison.OrdinalIgnoreCase);
        var show = whenZero ? count == 0 : count > 0;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility (true = Visible).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}
