using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Services;

/// <summary>
/// Force la barre de titre native Windows (non stylable en XAML) à suivre le thème sombre
/// de l'application via l'API DWM, pour éviter un bandeau blanc au-dessus d'une fenêtre sombre.
/// </summary>
internal static class WindowChromeHelper
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void ApplyDarkTitleBar(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var useDarkMode = 1;

            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDarkMode, sizeof(int));
        };
    }
}
