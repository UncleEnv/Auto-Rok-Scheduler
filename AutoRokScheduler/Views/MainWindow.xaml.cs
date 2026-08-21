using System;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AutoRokScheduler.Models;
using AutoRokScheduler.ViewModels;

namespace AutoRokScheduler.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Auto-scroll the log to the newest line.
        _vm.Log.CollectionChanged += OnLogChanged;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        };
        StateChanged += (_, _) => BtnMax.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        Closing += (_, _) => _vm.Shutdown();

        // On startup, log in and read each account's live status so the dashboard
        // shows the real state right away.
        Loaded += async (_, _) => await _vm.RefreshStatusesAsync();

        // Restore saved window size/position.
        var s = _vm.Settings;
        if (s.WindowWidth > 200 && s.WindowHeight > 200)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // InvokeAsync (not BeginInvoke) runs the delegate directly. BeginInvoke goes
        // through DynamicInvoke, which would rewrap any failure as an opaque
        // TargetInvocationException before it reached the error handler.
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.InvokeAsync(() => LogScroll?.ScrollToEnd());
    }

    // ------------------------------------------------------------- title bar

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------- bot actions

    private void Start_Click(object sender, RoutedEventArgs e) => _vm.StartSelected();
    private void Stop_Click(object sender, RoutedEventArgs e) => _vm.StopSelected();
    private void Cancel_Click(object sender, RoutedEventArgs e) => _vm.CancelCurrent();

    // ---------------------------------------------------------- profile CRUD

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var p = new Profile
        {
            Name = SuggestProfileName(),
            Browser = _vm.Settings.DefaultBrowser
        };
        var dlg = new ProfileEditWindow(p, _vm.ExistingProfileKeys(), isNew: true) { Owner = this };
        if (dlg.ShowDialog() == true)
            _vm.AddProfile(p);
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is not { } pvm) return;
        var dlg = new ProfileEditWindow(pvm.Model, _vm.ExistingProfileKeys(), isNew: false) { Owner = this };
        if (dlg.ShowDialog() == true)
            _vm.CommitProfileEdit(pvm);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is not { } pvm) return;
        var ok = MessageBox.Show(
            $"Delete account '{pvm.Name}'?\n\nThis removes its schedules from the app. The browser profile folder on disk is left untouched.",
            "Delete account", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok == MessageBoxResult.Yes)
            _vm.RemoveProfile(pvm);
    }

    private string SuggestProfileName()
    {
        var n = 1;
        string name;
        var existing = _vm.ExistingProfileKeys();
        do { name = $"Account {n++}"; }
        while (Array.Exists(existing, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)));
        return name;
    }

    // --------------------------------------------------------- schedule CRUD

    private void AddSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is not { } pvm) return;
        var entry = new ScheduleEntry { TimeOfDay = new TimeOnly(8, 0), Action = BotAction.Start };
        var dlg = new ScheduleEditWindow(entry) { Owner = this };
        if (dlg.ShowDialog() == true)
            _vm.AddSchedule(pvm, entry);
    }

    private void EditSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is not { } pvm) return;
        if ((sender as FrameworkElement)?.DataContext is not ScheduleViewModel svm) return;
        var dlg = new ScheduleEditWindow(svm.Model) { Owner = this };
        if (dlg.ShowDialog() == true)
            _vm.CommitScheduleEdit(svm);
    }

    private void DeleteSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is not { } pvm) return;
        if ((sender as FrameworkElement)?.DataContext is not ScheduleViewModel svm) return;
        _vm.RemoveSchedule(pvm, svm);
    }

    // ------------------------------------------------------------- settings

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_vm.Settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _vm.RestartTimer();
            _vm.Save();
            _vm.LogSettingsSaved();
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _vm.Log.Clear();

    // ------------------------------------------------- maximize-to-work-area

    private const int WM_GETMINMAXINFO = 0x0024;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref mi);
            var work = mi.rcWork;
            var mon = mi.rcMonitor;
            mmi.ptMaxPosition.X = Math.Abs(work.left - mon.left);
            mmi.ptMaxPosition.Y = Math.Abs(work.top - mon.top);
            mmi.ptMaxSize.X = Math.Abs(work.right - work.left);
            mmi.ptMaxSize.Y = Math.Abs(work.bottom - work.top);
        }
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
