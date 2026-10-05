using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snippetpal;

// An offscreen render of the real WPF controls. No window is shown and no input or clipboard is touched.
internal static class PreviewRenderer
{
    public static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var library = new Library(new Storage(Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"))));
            library.Data.Snippets.AddRange(new[]
            {
                new Snippet { Name = "A quick thank you", Text = "Thanks for sending this over! I'll take a look and get back to you soon." },
                new Snippet { Name = "Meeting notes", Text = "Discussed:\n\nNext steps:\n\nFollow up:" },
                new Snippet { Name = "Project folder", Text = @"C:\Projects\my-next-idea" },
                new Snippet { Text = "A snippet without a name shows its text instead. Long snippets are neatly truncated in the list, while the complete text is copied every time." }
            });
            library.Data.Clipboard.Add(new ClipboardEntry { Text = "Some useful text copied from another app." });
            library.Data.Clipboard.Add(new ClipboardEntry { Text = "First line\nSecond line\nThird line" });
            Themes.Apply("Dark"); var window = new MainWindow(library);
            Render((FrameworkElement)window.Content, 500, 620, Path.Combine(output, "snippets-dark.png"));
            window.PreviewTab(1); Render((FrameworkElement)window.Content, 500, 620, Path.Combine(output, "clipboard-dark.png"));
            window.PreviewTab(2); Render((FrameworkElement)window.Content, 440, 360, Path.Combine(output, "captures-empty-compact.png"));
            var captureCard = new Border { Background = new LinearGradientBrush(Color.FromRgb(52, 82, 116), Color.FromRgb(91, 163, 144), 35), Padding = new Thickness(24), Child = new TextBlock { Text = "A small idea,\na little further along.", Foreground = Brushes.White, FontSize = 25, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } };
            string imagePath = Path.Combine(output, "Design exploration.png"); Render(captureCard, 420, 220, imagePath);
            library.Data.Captures.Add(new CaptureEntry { Path = imagePath });
            library.Data.Captures.Add(new CaptureEntry { Path = Path.Combine(output, "Prototype walkthrough.mp4"), IsVideo = true });
            window.PreviewTab(2); Render((FrameworkElement)window.Content, 500, 620, Path.Combine(output, "captures-dark.png"));
            window.PreviewTab(0); Themes.Apply("Light"); Render((FrameworkElement)window.Content, 500, 620, Path.Combine(output, "snippets-light.png"));
            Themes.Apply("Dark"); var editor = new SnippetEditor(library, library.Data.Snippets[1], "");
            Render((FrameworkElement)editor.Content, 470, 550, Path.Combine(output, "editor-dark.png"));
            var settings = new SettingsDialog(library); Render((FrameworkElement)settings.Content, 510, 740, Path.Combine(output, "settings-dark.png"));
            File.WriteAllText(Path.Combine(output, "render-results.txt"), "Rendered 7 views from actual WPF controls without showing a window, moving focus, or accessing the clipboard. Sample content is illustrative; native desktop blur is not represented in offscreen renders.\n");
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "render-results.txt"), error.ToString()); return 1; }
    }
    private static void Render(FrameworkElement visual, int width, int height, string path)
    {
        // Dialog content normally receives the window background through native composition.
        var background = new Border { Background = (Brush)Application.Current.Resources["WindowBrush"] };
        visual.Measure(new Size(width, height)); visual.Arrange(new Rect(0, 0, width, height)); visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        background.Measure(new Size(width, height)); background.Arrange(new Rect(0, 0, width, height)); bitmap.Render(background); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
