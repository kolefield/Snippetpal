using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using Microsoft.Win32;

namespace Snippetpal;

public class AppDialog : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(24) };
    public AppDialog(string title, double width = 440)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Title = title; Width = width; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 42, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        var scroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var chrome = new Grid(); chrome.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) }); chrome.RowDefinitions.Add(new RowDefinition());
        var caption = new Grid(); caption.ColumnDefinitions.Add(new ColumnDefinition()); caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        caption.Children.Add(new TextBlock { Text = title, FontSize = 12, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "\uE8BB", Margin = new Thickness(0, 4, 5, 4) }; close.SetResourceReference(StyleProperty, "CloseCaptionButton");
        System.Windows.Automation.AutomationProperties.SetName(close, "Close dialog"); close.Click += (_, _) => DialogResult = false;
        WindowChrome.SetIsHitTestVisibleInChrome(close, true); Grid.SetColumn(close, 1); caption.Children.Add(close); chrome.Children.Add(caption);
        Grid.SetRow(scroll, 1); chrome.Children.Add(scroll);
        var frame = new Border { Child = chrome, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) }; frame.SetResourceReference(Border.BorderBrushProperty, "GlassBorderBrush"); frame.SetResourceReference(Border.BackgroundProperty, "GlassWindowBrush");
        Content = frame; MaxHeight = SystemParameters.WorkArea.Height - 60;
        SourceInitialized += (_, _) => NativeMethods.ApplyGlass(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
    protected TextBlock Label(string text, bool muted = false, double size = 13)
    {
        var block = new TextBlock { Text = text, FontSize = size, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        if (muted) block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Body.Children.Add(block); return block;
    }
    protected Button Button(string text, RoutedEventHandler action, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), MinWidth = 82 };
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.Click += action; return button;
    }
    protected void Actions(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        foreach (var button in buttons) panel.Children.Add(button); Body.Children.Add(panel);
    }
}

public sealed class SnippetEditor : AppDialog
{
    public SnippetEditor(Library library, Snippet? existing, string initialText) : base(existing == null ? "New snippet" : "Edit snippet", 470)
    {
        Label(existing == null ? "Keep something handy" : "Make it yours", false, 22).FontWeight = FontWeights.SemiBold;
        Label("A name is optional. Your snippet can be as short or long as you need.", true);
        Label("Name (optional)"); var name = new TextBox { Text = existing?.Name ?? "", MaxLength = 120, Margin = new Thickness(0, 0, 0, 18) }; Body.Children.Add(name);
        System.Windows.Automation.AutomationProperties.SetName(name, "Snippet name (optional)");
        Label("Snippet"); var text = new TextBox { Text = existing?.Text ?? initialText, MinHeight = 180, MaxHeight = 320, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 1_000_000 }; Body.Children.Add(text);
        System.Windows.Automation.AutomationProperties.SetName(text, "Snippet text");
        var hint = Label("", true, 11); hint.Margin = new Thickness(0, 8, 0, 0);
        var save = Button("Save snippet", (_, _) => { if (library.SaveSnippet(existing, name.Text, text.Text)) DialogResult = true; else hint.Text = "Couldn't save. Check the snippet text and data-folder permissions."; }, true);
        void Update() { save.IsEnabled = !string.IsNullOrWhiteSpace(text.Text); hint.Text = $"{text.Text.Length:N0} characters  ·  Ctrl+Enter to save"; }
        text.TextChanged += (_, _) => Update(); Update();
        Actions(Button("Cancel", (_, _) => DialogResult = false), save);
        Loaded += (_, _) => { if (existing == null) name.Focus(); else text.Focus(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && save.IsEnabled) { save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); e.Handled = true; } };
    }
}

public sealed class ConfirmDialog : AppDialog
{
    public ConfirmDialog(string title, string message) : base(title)
    { Label(title, false, 20).FontWeight = FontWeights.SemiBold; Label(message, true); Actions(Button("Cancel", (_, _) => DialogResult = false), Button("Remove", (_, _) => DialogResult = true, true)); }
}

public sealed class SettingsDialog : AppDialog
{
    public SettingsDialog(Library library) : base("Snippetpal settings", 510)
    {
        var preferences = library.Data.Preferences;
        Label("At home on your desktop", false, 22).FontWeight = FontWeights.SemiBold;
        Label("Open any time with Ctrl+Alt+S, or click the tray icon.", true);
        Label("Appearance");
        string theme = preferences.Theme;
        var themePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) }; Body.Children.Add(themePanel);
        foreach (var option in new[] { "System", "Light", "Dark" })
        {
            var radio = new RadioButton { Content = option, GroupName = "Theme", IsChecked = theme == option, Margin = new Thickness(0, 0, 25, 0) };
            radio.SetResourceReference(ForegroundProperty, "TextBrush"); radio.Checked += (_, _) => theme = option; themePanel.Children.Add(radio);
        }
        var tray = new CheckBox { Content = "Keep running in the tray when I close the window", IsChecked = preferences.CloseToTray }; Body.Children.Add(tray);
        Label("History", false, 15).Margin = new Thickness(0, 15, 0, 8);
        var paused = new CheckBox { Content = "Pause clipboard and capture history", IsChecked = preferences.HistoryPaused }; Body.Children.Add(paused);
        System.Windows.Automation.AutomationProperties.SetName(paused, "Pause clipboard and capture history");
        Label("Clipboard text and capture metadata are encrypted for your Windows account. Clipboard images are cached locally as PNG files. There is no cloud sync or analytics.", true, 12);
        Label("Items to keep in each history (25–500)"); var limit = new TextBox { Text = preferences.HistoryLimit.ToString(), MaxLength = 3, Width = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) }; Body.Children.Add(limit);
        System.Windows.Automation.AutomationProperties.SetName(limit, "History limit");
        Label("Screenshot folder"); var screenshots = FolderField(preferences.ScreenshotFolder);
        Label("Recording folder"); var recordings = FolderField(preferences.RecordingFolder);
        Label("Use the same folders as Snipping Tool. Enable its automatic saving, or save recordings here manually. Only saved recordings can appear. Existing files are imported from these folders; Windows' earlier clipboard text is not imported.", true, 11);
        var validation = Label("", true, 11);
        Actions(Button("Data folder", (_, _) => Process.Start(new ProcessStartInfo(library.Storage.Root) { UseShellExecute = true })), Button("Cancel", (_, _) => DialogResult = false), Button("Save", (_, _) =>
        {
            if (!int.TryParse(limit.Text, out int count) || count < 25 || count > 500) { validation.Text = "Enter a history limit between 25 and 500."; return; }
            if (!Path.IsPathFullyQualified(screenshots.Text) || !Path.IsPathFullyQualified(recordings.Text)) { validation.Text = "Choose full paths for both capture folders."; return; }
            preferences.Theme = theme; preferences.CloseToTray = tray.IsChecked == true; preferences.HistoryLimit = count;
            preferences.ScreenshotFolder = screenshots.Text.Trim(); preferences.RecordingFolder = recordings.Text.Trim();
            if (library.SetHistoryPaused(paused.IsChecked == true)) DialogResult = true; else validation.Text = "Settings could not be saved. Check data-folder permissions.";
        }, true));
    }
    private TextBox FolderField(string path)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var field = new TextBox { Text = path, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center }; grid.Children.Add(field);
        System.Windows.Automation.AutomationProperties.SetName(field, path);
        var browse = Button("Browse", (_, _) => { var dialog = new OpenFolderDialog(); if (Directory.Exists(field.Text)) dialog.InitialDirectory = field.Text; if (dialog.ShowDialog(this) == true) field.Text = dialog.FolderName; }); Grid.SetColumn(browse, 1); grid.Children.Add(browse); Body.Children.Add(grid); return field;
    }
}
