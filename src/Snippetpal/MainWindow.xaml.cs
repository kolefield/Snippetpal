using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Snippetpal;

public partial class MainWindow : Window
{
    private readonly Library library;
    private CaptureService? captures;
    private ClipboardService? clipboard;
    private Forms.NotifyIcon? tray;
    private Forms.ToolStripMenuItem? trayPauseItem;
    private bool quitting;
    private int tab;
    private HwndSource? source;
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    public MainWindow(Library library)
    {
        this.library = library; InitializeComponent(); Topmost = library.Data.Preferences.AlwaysOnTop;
        library.Changed += Refresh; library.Error += Report;
        statusTimer.Tick += (_, _) => { StatusPanel.Visibility = Visibility.Collapsed; statusTimer.Stop(); };
        dragScrollTimer.Tick += (_, _) => ScrollDuringSnippetDrag();
        SourceInitialized += (_, _) => StartServices(); Closing += OnClosing; Closed += OnClosed;
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        Refresh();
    }
    private void StartServices()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ApplyGlass(this);
        captures = new CaptureService(library); captures.Status += Report;
        try { clipboard = new ClipboardService(hwnd, library, captures); clipboard.Status += Report; }
        catch (Exception ex) { Report(ex.Message); }
        source = HwndSource.FromHwnd(hwnd); source?.AddHook(Hook);
        if (!NativeMethods.RegisterHotKey(hwnd, 1, 0x4000 | 0x0002 | 0x0004, 0x53)) Report("Ctrl+Alt+S is already in use. You can open Snippetpal from its tray icon.");
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Snippetpal.ico")).Stream;
        tray = new Forms.NotifyIcon { Text = "Snippetpal · click to open", Icon = new System.Drawing.Icon(stream), Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowFromTray(); };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Open Snippetpal", null, (_, _) => ShowFromTray());
        trayPauseItem = new Forms.ToolStripMenuItem("Pause history", null, (_, _) => TogglePause(this, new RoutedEventArgs())); menu.Items.Add(trayPauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Quit Snippetpal", null, (_, _) => Quit()); tray.ContextMenuStrip = menu;
        Refresh();
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x0312 && wp.ToInt32() == 1) { ShowFromTray(); handled = true; }
        if (msg == 0x001A) Dispatcher.BeginInvoke(() => Themes.Apply(library.Data.Preferences.Theme));
        return IntPtr.Zero;
    }
    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => Themes.Apply(library.Data.Preferences.Theme));
    public void ShowFromTray() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    public void Quit() { quitting = true; Close(); Application.Current.Shutdown(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!quitting && library.Data.Preferences.CloseToTray) { e.Cancel = true; Hide(); }
        else quitting = true;
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemThemeChanged; statusTimer.Stop(); dragScrollTimer.Stop();
        NativeMethods.UnregisterHotKey(new WindowInteropHelper(this).Handle, 1);
        source?.RemoveHook(Hook); clipboard?.Dispose(); captures?.Dispose(); tray?.Dispose();
        library.Changed -= Refresh; library.Error -= Report;
        if (quitting) Application.Current.Shutdown();
    }
    public void Report(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Report(message)); return; }
        StatusText.Text = message; StatusPanel.Visibility = Visibility.Visible; statusTimer.Stop(); statusTimer.Start();
    }
    private void Refresh()
    {
        if (SearchBox == null) return;
        string query = SearchBox.Text.Trim();
        object[] items = tab switch
        {
            0 => library.Data.Snippets.Where(x => TextUtilities.Matches(query, x.Name, x.Text)).Cast<object>().ToArray(),
            1 => library.Data.Clipboard.Where(x => TextUtilities.Matches(query, x.Text)).Cast<object>().ToArray(),
            _ => library.Data.Captures.Where(x => TextUtilities.Matches(query, x.Title, x.IsVideo ? "recording video" : "screenshot image")).Cast<object>().ToArray()
        };
        // Clipboard/capture events can arrive during an OLE drag. Keep its row containers stable.
        if (draggedSnippet == null) ItemsList.ItemsSource = items;
        var tabs = new[] { SnippetsTab, ClipboardTab, CapturesTab };
        for (int i = 0; i < tabs.Length; i++) { if (i == tab) tabs[i].SetResourceReference(BackgroundProperty, "TabSelectedBrush"); else tabs[i].Background = Brushes.Transparent; tabs[i].FontWeight = i == tab ? FontWeights.SemiBold : FontWeights.Normal; }
        SearchHint.Text = tab == 0 ? "Search snippets…" : tab == 1 ? "Search clipboard history…" : "Search screenshots & recordings…";
        SearchHint.Visibility = query.Length == 0 && SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SnippetsTab.ToolTip = $"{library.Data.Snippets.Count} saved snippets · click to copy";
        ClipboardTab.ToolTip = library.Data.Preferences.HistoryPaused ? "Clipboard history is paused. Resume in Settings." : $"{library.Data.Clipboard.Count} clipboard items";
        CapturesTab.ToolTip = library.Data.Preferences.HistoryPaused ? "Capture history is paused. Resume in Settings." : $"{library.Data.Captures.Count} screenshots and recordings";
        NewButton.Visibility = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = tab == 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.IsEnabled = tab == 1 ? library.Data.Clipboard.Count > 0 : library.Data.Captures.Count > 0;
        ClearButton.ToolTip = tab == 1 ? "Clear clipboard history" : "Clear screenshot and recording history";
        System.Windows.Automation.AutomationProperties.SetName(ClearButton, ClearButton.ToolTip.ToString());
        EmptyPanel.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyAction.Visibility = tab == 0 && query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyGlyph.Text = tab == 0 ? "\uE8C8" : tab == 1 ? "\uE77F" : "\uE91B";
        EmptyTitle.Text = query.Length > 0 ? "No matches" : tab == 0 ? "No snippets yet" : tab == 1 ? "No clipboard history" : "No captures yet";
        EmptyDetail.Text = query.Length > 0 ? "Try another word or clear your search." : tab == 0 ? "Add a snippet, then click it to copy." : library.Data.Preferences.HistoryPaused ? "History is paused. Resume it in Settings." : tab == 1 ? "Text you copy will appear here." : "Win+Shift+S for screenshots; Win+Shift+R for recordings. Set save folders in Settings.";
        if (trayPauseItem != null) trayPauseItem.Text = library.Data.Preferences.HistoryPaused ? "Resume history" : "Pause history";
        if (tray != null) tray.Text = library.Data.Preferences.HistoryPaused ? "Snippetpal · history paused" : "Snippetpal · click to open";
        if (Topmost) PinButton.SetResourceReference(BackgroundProperty, "AccentSoftBrush"); else PinButton.Background = Brushes.Transparent;
        PinButton.ToolTip = Topmost ? "Always on top is on. Click to unpin." : "Keep Snippetpal above other windows";
        System.Windows.Automation.AutomationProperties.SetName(PinButton, Topmost ? "Always on top (on)" : "Always on top (off)");
    }
    private void SelectTab(object sender, RoutedEventArgs e) { tab = int.Parse((string)((Button)sender).Tag); SearchBox.Clear(); Refresh(); }
    internal void PreviewTab(int value) { tab = value; Refresh(); }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (IsInitialized) Refresh(); }
    private void NewSnippet(object sender, RoutedEventArgs e) => ShowEditor(null);
    private void EditSnippet(object sender, RoutedEventArgs e) => ShowEditor((Snippet)((FrameworkElement)sender).DataContext);
    private void ShowEditor(Snippet? snippet, string text = "")
    { new SnippetEditor(library, snippet, text) { Owner = this }.ShowDialog(); }
    private async void CopySnippet(object sender, RoutedEventArgs e)
    { if (clipboard != null && await clipboard.CopyTextAsync(((Snippet)((FrameworkElement)sender).DataContext).Text)) Report("Copied to clipboard"); }
    private async void CopyHistory(object sender, RoutedEventArgs e)
    { if (clipboard != null && await clipboard.CopyTextAsync(((ClipboardEntry)((FrameworkElement)sender).DataContext).Text)) Report("Copied to clipboard"); }
    private void PromoteHistory(object sender, RoutedEventArgs e) => ShowEditor(null, ((ClipboardEntry)((FrameworkElement)sender).DataContext).Text);
    private void DeleteSnippet(object sender, RoutedEventArgs e)
    {
        var item = (Snippet)((FrameworkElement)sender).DataContext;
        if (Confirm("Delete snippet?", $"Remove “{TextUtilities.Preview(item.Title, 70)}” from your snippets?")) { library.Data.Snippets.Remove(item); library.Save(true); }
    }
    private void DeleteHistory(object sender, RoutedEventArgs e)
    { library.Data.Clipboard.Remove((ClipboardEntry)((FrameworkElement)sender).DataContext); library.Save(true); }
    private async void CopyCapture(object sender, RoutedEventArgs e)
    {
        var entry = (CaptureEntry)((FrameworkElement)sender).DataContext;
        if (!File.Exists(entry.Path)) { Report("This capture moved or was deleted. You can remove it from history."); return; }
        if (clipboard != null && await (entry.IsVideo ? clipboard.CopyFileAsync(entry.Path) : clipboard.CopyImageAsync(entry.Path))) Report(entry.IsVideo ? "Recording file copied" : "Image copied to clipboard");
    }
    private void OpenCapture(object sender, RoutedEventArgs e)
    {
        var entry = (CaptureEntry)((FrameworkElement)sender).DataContext;
        try { Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException) { Report("Cannot open this capture. Check that it still exists and has a default viewer."); }
    }
    private void DeleteCapture(object sender, RoutedEventArgs e) => library.RemoveCapture((CaptureEntry)((FrameworkElement)sender).DataContext);
    private void ClearHistory(object sender, RoutedEventArgs e)
    {
        if (Confirm("Clear history?", tab == 1 ? "Remove all saved clipboard text? Your snippets will stay." : "Remove capture history and cached clipboard images? Original screenshot and recording files will stay.")) library.ClearHistory(tab == 2);
    }
    private bool Confirm(string title, string message) => new ConfirmDialog(title, message) { Owner = this }.ShowDialog() == true;
    private void TogglePin(object sender, RoutedEventArgs e) { Topmost = !Topmost; library.Data.Preferences.AlwaysOnTop = Topmost; library.Save(); }
    private void TogglePause(object sender, RoutedEventArgs e)
    {
        if (library.SetHistoryPaused(!library.Data.Preferences.HistoryPaused)) captures?.Reconfigure();
    }
    private void OpenSettings(object sender, RoutedEventArgs e)
    { if (new SettingsDialog(library) { Owner = this }.ShowDialog() == true) { Themes.Apply(library.Data.Preferences.Theme); library.Trim(); library.Save(); captures?.Reconfigure(); } }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { ShowEditor(null); e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        if (e.Key == Key.Escape && SearchBox.Text.Length > 0) { SearchBox.Clear(); e.Handled = true; }
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void MaximizeWindow(object sender, RoutedEventArgs e)
    { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void CloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    private void WindowStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton == null) return;
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
        // WindowChrome handles the native caption, double-click, system menu and dragging.
        WindowFrame.Margin = maximized ? new Thickness(SystemParameters.ResizeFrameVerticalBorderWidth + SystemParameters.FixedFrameVerticalBorderWidth) : new Thickness(0);
    }
}

public sealed class CaptureThumbnailConverter : IValueConverter
{
    private readonly System.Collections.Generic.Dictionary<string, ImageSource> cache = new();
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CaptureEntry entry || entry.IsVideo) return null;
        string key = Library.CaptureVersion(entry);
        if (cache.TryGetValue(key, out var image)) return image;
        try { image = CaptureService.LoadImage(entry.Path, 160); if (cache.Count >= 120) cache.Clear(); cache[key] = image; return image; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
