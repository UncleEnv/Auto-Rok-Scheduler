using System.Windows;
using System.Windows.Threading;

namespace AutoRokScheduler;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "An unexpected error occurred:\n\n" + e.Exception.Message,
            "Auto-RoK Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // keep the app alive
    }
}
