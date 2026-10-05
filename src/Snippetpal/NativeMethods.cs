using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Snippetpal;

internal static class NativeMethods
{
    private const int DwmColorDefault = -1;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

    internal static void DarkTitleBar(Window window, bool dark)
    {
        int value = dark ? 1 : 0;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
    }
    internal static void ApplyGlass(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        DarkTitleBar(window, Themes.IsDark);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // The app paints its own outline. Suppress DWM's focus-dependent top-edge border.
            // Preserve the system border when High Contrast is enabled.
            int borderColor = SystemParameters.HighContrast ? DwmColorDefault : DwmColorNone;
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));
        }
        bool supported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && !SystemParameters.HighContrast;
        int backdrop = supported ? 3 : 1; // Desktop acrylic; DWM honors the OS transparency preference.
        bool applied = supported && DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)) == 0;
        var chrome = WindowChrome.GetWindowChrome(window);
        if (chrome != null)
        {
            if (chrome.IsFrozen) { chrome = (WindowChrome)chrome.Clone(); WindowChrome.SetWindowChrome(window, chrome); }
            chrome.GlassFrameThickness = applied ? new Thickness(-1) : new Thickness(0);
        }
        if (applied)
        {
            window.Background = Brushes.Transparent;
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
            int corners = 2; DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
        }
        else
        {
            DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = ((SolidColorBrush)Application.Current.Resources["WindowBrush"]).Color;
        }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // Full-window glass still has a separately tinted native caption underneath it.
            // Remove that fill after WindowChrome updates the frame, so the custom title bar
            // shares the same backdrop as the body. Restore the default for opaque/High Contrast.
            int captionColor = applied && chrome != null ? DwmColorNone : DwmColorDefault;
            DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int)); // DWMWA_CAPTION_COLOR
        }
    }
    internal static string ScreenshotFolder()
    {
        Guid id = new("B7BEDE81-DF94-4682-A7D8-57A52620B86F");
        if (SHGetKnownFolderPath(ref id, 0x4000, IntPtr.Zero, out var pointer) == 0)
        {
            try { return Marshal.PtrToStringUni(pointer)!; } finally { Marshal.FreeCoTaskMem(pointer); }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }
}
