using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace Snippetpal;

public sealed class Snippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    [JsonIgnore] public string Title => string.IsNullOrWhiteSpace(Name) ? TextUtilities.Preview(Text, 100) : Name;
    [JsonIgnore] public string Preview => TextUtilities.Preview(Text, 160);
    [JsonIgnore] public bool HasName => !string.IsNullOrWhiteSpace(Name);
    [JsonIgnore] public string Detail => $"{Text.Length:N0} characters";
}

public sealed class ClipboardEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    [JsonIgnore] public string Title => TextUtilities.Preview(Text, 120);
    [JsonIgnore] public string Detail => $"{Created.LocalDateTime:g}  ·  {Text.Length:N0} characters";
}

public sealed class CaptureEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public bool IsVideo { get; set; }
    public bool Owned { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public long Size { get; set; }
    public long ModifiedTicks { get; set; }
    [JsonIgnore] public string Title => Owned ? $"Clipboard image · {Created.LocalDateTime:HH:mm}" : System.IO.Path.GetFileName(Path);
    [JsonIgnore] public string Detail => $"{(IsVideo ? "RECORDING" : "SCREENSHOT")}  ·  {Created.LocalDateTime:g}";
    [JsonIgnore] public string Glyph => IsVideo ? "\uE714" : "\uE91B";
}

public sealed class Preferences
{
    public string Theme { get; set; } = "System";
    public bool AlwaysOnTop { get; set; }
    public bool HistoryPaused { get; set; }
    public bool CloseToTray { get; set; } = true;
    public int HistoryLimit { get; set; } = 100;
    public string ScreenshotFolder { get; set; } = NativeMethods.ScreenshotFolder();
    public string RecordingFolder { get; set; } = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recordings");
}

public sealed class AppData
{
    public int Version { get; set; } = 1;
    public List<Snippet> Snippets { get; set; } = new();
    public List<ClipboardEntry> Clipboard { get; set; } = new();
    public List<CaptureEntry> Captures { get; set; } = new();
    public Preferences Preferences { get; set; } = new();
    // Watermark prevents clearing/removing history from reimporting old source files.
    public DateTimeOffset CaptureWatermark { get; set; } = DateTimeOffset.MinValue;
    public List<string> DismissedCaptureVersions { get; set; } = new();
}

public static class TextUtilities
{
    public static string Preview(string text, int length)
    {
        var single = text.Replace("\r\n", " ↵ ").Replace("\n", " ↵ ").Replace("\r", " ↵ ").Replace("\t", "  ").Trim();
        return single.Length > length ? single[..length] + "…" : single;
    }
    public static bool Matches(string query, params string[] values) => values.Any(v => v.Contains(query, StringComparison.OrdinalIgnoreCase));
}
