using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SonyBraviaControl.Infrastructure;

public static class SonyWindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        try
        {
            var darkMode = 1;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref darkMode, sizeof(int));

            // Native Windows chrome: near-black caption, Sony/BRAVIA blue-black border,
            // and soft white title text. This removes the bright system frame around
            // the otherwise dark application.
            var borderColor = ColorRef(18, 43, 72);
            var captionColor = ColorRef(6, 7, 8);
            var textColor = ColorRef(244, 246, 248);

            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Older Windows versions simply keep their native chrome.
        }
        catch (EntryPointNotFoundException)
        {
            // Same fallback for Windows versions without these DWM attributes.
        }
    }

    private static int ColorRef(byte red, byte green, byte blue)
        => red | (green << 8) | (blue << 16);
}
