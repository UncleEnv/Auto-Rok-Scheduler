using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoRokScheduler;

public partial class App : Application
{
    public App()
    {
        // Force WPF's software rasterizer so the UI never touches the GPU / Direct3D 9
        // path. Some machines have a damaged or third-party-replaced system d3d9.dll
        // (missing expected exports — common after "DirectX fix" tools, game overlays,
        // or on some VMs / RDP sessions); the hardware path then crashes on startup with
        // "the ordinal N could not be located in d3d9.dll". Software rendering sidesteps
        // that entirely and is imperceptible for this simple dashboard.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

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
