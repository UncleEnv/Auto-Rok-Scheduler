using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoRokScheduler.Models;

namespace AutoRokScheduler.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        BrowserCombo.SelectedIndex = settings.DefaultBrowser == BrowserKind.Chrome ? 1 : 0;
        HeadlessToggle.IsChecked = settings.Headless;
        CloseAfterToggle.IsChecked = settings.CloseBrowserAfterAction;
        CatchUpToggle.IsChecked = settings.CatchUpMissed;
        PollBox.Text = settings.PollSeconds.ToString();
        TimeoutBox.Text = settings.ActionTimeoutSeconds.ToString();
        WinWidthBox.Text = settings.WindowWidth.ToString();
        WinHeightBox.Text = settings.WindowHeight.ToString();
        WinLeftBox.Text = settings.WindowLeft.ToString();
        WinTopBox.Text = settings.WindowTop.ToString();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryInt(PollBox.Text, out var poll, min: 5)) { Error("Poll seconds must be a number ≥ 5."); return; }
        if (!TryInt(TimeoutBox.Text, out var timeout, min: 15)) { Error("Action timeout must be a number ≥ 15."); return; }
        if (!TryInt(WinWidthBox.Text, out var w, min: 300)) { Error("Window width must be ≥ 300."); return; }
        if (!TryInt(WinHeightBox.Text, out var h, min: 300)) { Error("Window height must be ≥ 300."); return; }
        if (!TryInt(WinLeftBox.Text, out var left, min: 0)) { Error("Window left must be ≥ 0."); return; }
        if (!TryInt(WinTopBox.Text, out var top, min: 0)) { Error("Window top must be ≥ 0."); return; }

        _settings.DefaultBrowser = (BrowserCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Chrome"
            ? BrowserKind.Chrome : BrowserKind.Edge;
        _settings.Headless = HeadlessToggle.IsChecked == true;
        _settings.CloseBrowserAfterAction = CloseAfterToggle.IsChecked == true;
        _settings.CatchUpMissed = CatchUpToggle.IsChecked == true;
        _settings.PollSeconds = poll;
        _settings.ActionTimeoutSeconds = timeout;
        _settings.WindowWidth = w;
        _settings.WindowHeight = h;
        _settings.WindowLeft = left;
        _settings.WindowTop = top;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static bool TryInt(string text, out int value, int min)
        => int.TryParse(text?.Trim(), out value) && value >= min;

    private void Error(string msg) => ErrorText.Text = msg;
}
