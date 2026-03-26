using System.Configuration;
using System.Data;
using System.Runtime.InteropServices;
using System.Windows;

namespace ScreenshotQ
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private static readonly nint DpiAwarenessContextPerMonitorAwareV2 = new(-4);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(nint dpiFlag);

        public App()
        {
            TryEnablePerMonitorDpiAwareness();
        }

        private static void TryEnablePerMonitorDpiAwareness()
        {
            try
            {
                _ = SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
            }
            catch
            {
                // Ignore on unsupported systems; app continues with OS default behavior.
            }
        }
    }

}
