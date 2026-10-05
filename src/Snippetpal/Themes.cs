using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Snippetpal;

public static class Themes
{
    public static bool IsDark { get; private set; }
    public static void Apply(string mode)
    {
        bool systemDark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0;
        IsDark = mode == "Dark" || (mode != "Light" && systemDark);
        string[] keys = { "WindowBrush", "PanelBrush", "CardBrush", "HoverBrush", "BorderBrush", "TextBrush", "MutedBrush", "AccentBrush", "AccentTextBrush", "AccentSoftBrush", "DangerBrush" };
        string[] dark = { "#171F29", "#20050E18", "#27323F", "#244D6679", "#22FFFFFF", "#F2F6FA", "#B2C1D0", "#B2E8D9", "#173D35", "#264DBD9E", "#FFABA6" };
        string[] light = { "#EAF0F6", "#120F2945", "#F6F9FC", "#304C869F", "#300B3451", "#1A3043", "#476075", "#267A65", "#FFFFFF", "#203FBA94", "#B63838" };
        var colors = IsDark ? dark : light;
        for (int i = 0; i < keys.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); brush.Freeze();
            Application.Current.Resources[keys[i]] = brush;
        }
        SetGradient("GlassWindowBrush", IsDark ? "#85314355" : "#BBF4F8FF", IsDark ? "#68142131" : "#9CDBE8F0");
        SetGradient("GlassCardBrush", IsDark ? "#1CFFFFFF" : "#B8FFFFFF", IsDark ? "#08FFFFFF" : "#66FFFFFF");
        SetGradient("GlassBorderBrush", IsDark ? "#32FFFFFF" : "#F0FFFFFF", IsDark ? "#10FFFFFF" : "#80FFFFFF");
        SetGradient("GlassInputBrush", IsDark ? "#30091522" : "#70FFFFFF", IsDark ? "#10091522" : "#40FFFFFF");
        SetGradient("SheenBrush", IsDark ? "#14FFFFFF" : "#70FFFFFF", "#00FFFFFF");
        var selected = new SolidColorBrush((Color)ColorConverter.ConvertFromString(IsDark ? "#24FFFFFF" : "#CFFFFFFF")); selected.Freeze();
        Application.Current.Resources["TabSelectedBrush"] = selected;
        foreach (Window window in Application.Current.Windows) NativeMethods.ApplyGlass(window);
    }
    private static void SetGradient(string key, string start, string end)
    {
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(start), (Color)ColorConverter.ConvertFromString(end), new Point(0, 0), new Point(0.6, 1));
        brush.Freeze(); Application.Current.Resources[key] = brush;
    }
}
