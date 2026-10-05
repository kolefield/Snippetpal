using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snippetpal;

internal static class SelfTests
{
    public static int Run(string parent, bool testClipboard)
    {
        string root = Path.Combine(parent, "self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var report = new StringBuilder(); int passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); report.AppendLine("PASS " + name); passed++; }
        try
        {
            var storage = new Storage(Path.Combine(root, "data")); var library = new Library(storage);
            string exact = "  hello\r\nworld\t🦊\n  ";
            Check(library.SaveSnippet(null, "  Reply  ", exact), "Save named multiline Unicode snippet");
            Check(library.Data.Snippets[0].Title == "Reply" && library.Data.Snippets[0].Text == exact, "Name trimmed, snippet whitespace preserved");
            Check(!library.SaveSnippet(null, "Invalid", " \r\n"), "Reject empty snippet");
            Check(library.SaveSnippet(null, "", new string('x', 200)), "Save unnamed snippet");
            Check(library.Data.Snippets[0].Title == new string('x', 100) + "…", "Unnamed title is truncated without changing stored text");
            Check(new Storage(storage.Root).Load().Snippets[1].Text == exact, "Reload exact text from disk");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(storage.FilePath)).Contains("Reply"), "Library is encrypted on disk");
            var edit = library.Data.Snippets[0]; Check(library.SaveSnippet(edit, "Updated", "new\ntext"), "Edit existing snippet");
            Check(library.Data.Snippets.Count == 2 && new Storage(storage.Root).Load().Snippets[0].Text == "new\ntext", "Edit persists without creating duplicate");
            Check(TextUtilities.Matches("WORLD", exact), "Search ignores case");

            var reorder = new Library(new Storage(Path.Combine(root, "reorder")));
            var a = new Snippet { Name = "A", Text = exact };
            var b = new Snippet { Name = "B", Text = "match B" };
            var c = new Snippet { Name = "C", Text = "hidden C" };
            var d = new Snippet { Name = "D", Text = "match D" };
            reorder.Data.Snippets.AddRange(new[] { a, b, c, d }); reorder.Save();
            bool Order(params Snippet[] expected) => reorder.Data.Snippets.SequenceEqual(expected);
            Check(reorder.MoveSnippet(a, d, true) && Order(b, c, d, a), "Drag first snippet below last");
            Check(reorder.MoveSnippet(a, b, false) && Order(a, b, c, d), "Drag last snippet above first");
            Check(reorder.MoveSnippet(b, c, true) && Order(a, c, b, d), "Move adjacent snippet down without an index offset");
            Check(reorder.MoveSnippet(b, c, false) && Order(a, b, c, d), "Move adjacent snippet up");
            int reorderSaves = 0; reorder.Changed += () => reorderSaves++;
            Check(reorder.MoveSnippet(b, b, true) && reorder.MoveSnippet(b, c, false) && reorderSaves == 0 && Order(a, b, c, d), "Dropping on self or current position is a no-op");
            Check(!reorder.MoveSnippet(new Snippet(), b, false) && !reorder.MoveSnippet(b, new Snippet(), true) && Order(a, b, c, d), "Reject stale drag source or target without changing the list");
            var matches = reorder.Data.Snippets.Where(x => TextUtilities.Matches("match", x.Text)).ToArray();
            Check(reorder.MoveSnippet(matches[1], matches[0], false) && Order(a, d, b, c), "Reorder filtered matches using anchors while preserving hidden snippets");
            var reordered = new Storage(reorder.Storage.Root).Load().Snippets;
            Check(reordered.Select(x => x.Id).SequenceEqual(new[] { a.Id, d.Id, b.Id, c.Id }) && reordered[0].Text == exact, "Reordered snippets survive reload with IDs and exact text intact");
            bool saveError = false; reorder.Error += _ => saveError = true;
            using (var locked = new FileStream(reorder.Storage.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(!reorder.MoveSnippet(c, a, false) && saveError && Order(a, d, b, c), "Failed reorder save reports error and restores in-memory order");
            Check(new Storage(reorder.Storage.Root).Load().Snippets.Select(x => x.Id).SequenceEqual(new[] { a.Id, d.Id, b.Id, c.Id }), "Failed reorder preserves the previously saved order");

            library.Data.Preferences.HistoryLimit = 25;
            for (int i = 0; i < 30; i++) library.AddClipboard("clipboard " + i);
            Check(library.Data.Clipboard.Count == 25 && library.Data.Clipboard[0].Text == "clipboard 29", "Clipboard history is bounded and newest first");
            library.AddClipboard("clipboard 20"); Check(library.Data.Clipboard.Count == 25 && library.Data.Clipboard[0].Text == "clipboard 20", "Clipboard deduplication moves repeated text to front");
            Check(library.SetHistoryPaused(true) && new Storage(storage.Root).Load().Preferences.HistoryPaused, "Settings/tray pause persists across restart");
            library.AddClipboard("should not appear");
            Check(library.Data.Clipboard.All(x => x.Text != "should not appear"), "Paused history discards new text");
            var duringPause = new CaptureEntry { Path = "paused.png", Created = DateTimeOffset.Now.AddSeconds(-1) };
            Check(library.SetHistoryPaused(false) && !new Storage(storage.Root).Load().Preferences.HistoryPaused && !library.ShouldImport(duringPause), "Resuming via Settings/tray excludes captures from the pause interval");
            var resumeWatermark = library.Data.CaptureWatermark;
            Check(library.SetHistoryPaused(false) && library.Data.CaptureWatermark == resumeWatermark, "Saving unrelated settings while history runs does not skip captures");
            Check(ClipboardService.DisallowsHistory(new byte[4]) && !ClipboardService.DisallowsHistory(BitConverter.GetBytes(1)), "Respect Windows clipboard history exclusion flag");

            var pixels = new byte[] { 0x70, 0xB0, 0x30, 255, 0x10, 0x50, 0x90, 255 };
            var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8); bitmap.Freeze();
            string folder = Path.Combine(root, "Screenshots"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "sample.png"); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
            Check(CaptureService.Fingerprint(bitmap) == CaptureService.Fingerprint(CaptureService.LoadImage(path)), "Image fingerprint survives PNG encoding");
            Check(CaptureService.IsSupported(".PNG") && CaptureService.IsVideo(".MP4") && !CaptureService.IsSupported(".exe"), "Capture file filters");
            var cached = new CaptureEntry { Path = Path.Combine(storage.Cache, "cached.png"), Fingerprint = "duplicate", Owned = true }; File.Copy(path, cached.Path);
            library.AddCaptures(new[] { cached });
            library.AddCaptures(new[] { new CaptureEntry { Path = path, Fingerprint = "duplicate" } });
            Check(library.Data.Captures.Count == 1 && !library.Data.Captures[0].Owned && !File.Exists(cached.Path), "Saved screenshot replaces duplicate clipboard cache");
            library.RemoveCapture(library.Data.Captures[0]);
            Check(File.Exists(path), "Removing capture preserves original file");
            var rejected = new CaptureEntry { Path = path, Fingerprint = "duplicate" };
            Check(!library.ShouldImport(rejected), "Removed capture version is not reimported");
            library.ClearHistory(true); Check(!library.ShouldImport(new CaptureEntry { Path = path, Created = DateTimeOffset.Now.AddDays(-1) }), "Clear capture watermark blocks old files");
            Check(new Storage(storage.Root).Load().CaptureWatermark > DateTimeOffset.MinValue, "Capture clear watermark survives restart");
            library.ClearHistory(false); Check(library.Data.Clipboard.Count == 0 && library.Data.Snippets.Count == 2 && !File.Exists(storage.FilePath + ".bak"), "Clearing clipboard leaves snippets and removes old backup");

            // Exercise native listener and write path in an STA dispatcher; tests use explicit synthetic content.
            var isolated = new Library(new Storage(Path.Combine(root, "native")));
            isolated.Data.Preferences.ScreenshotFolder = folder;
            isolated.Data.Preferences.RecordingFolder = Path.Combine(root, "Recordings"); Directory.CreateDirectory(isolated.Data.Preferences.RecordingFolder);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-5));
            using var captures = new CaptureService(isolated);
            PumpUntil(() => isolated.Data.Captures.Count == 1, "Screenshot folder initial import"); Check(isolated.Data.Captures.Count == 1, "Actual capture folder scan imports PNG");
            // A container-header fixture checks discovery and file bookkeeping, not video decoding/playback.
            var videoPath = Path.Combine(isolated.Data.Preferences.RecordingFolder, "recording-fixture.mp4");
            File.WriteAllBytes(videoPath, new byte[] { 0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0, 105, 115, 111, 109, 109, 112, 52, 50 });
            PumpUntil(() => isolated.Data.Captures.Any(x => x.IsVideo), "Recording folder watcher");
            Check(isolated.Data.Captures.Any(x => x.IsVideo && x.Path == videoPath), "Live folder watcher discovers a newly saved recording file");
            isolated.RemoveCapture(isolated.Data.Captures.Single(x => x.IsVideo));
            var scanAgain = captures.ScanAsync(); PumpUntil(() => scanAgain.IsCompleted, "Rescan after remove"); scanAgain.GetAwaiter().GetResult();
            Check(isolated.Data.Captures.All(x => !x.IsVideo) && File.Exists(videoPath), "Removed recording stays excluded and original is preserved");
            if (testClipboard)
            {
            using var hwnd = new HwndSource(new HwndSourceParameters("Snippetpal self test") { Width = 0, Height = 0, WindowStyle = 0 });
            using var listener = new ClipboardService(hwnd.Handle, isolated, captures);
            var copy = listener.CopyTextAsync(exact); PumpUntil(() => copy.IsCompleted, "Copy exact text"); Check(copy.Result && Clipboard.GetText() == exact, "Native clipboard preserves exact Unicode and whitespace");
            PumpFor(150); Check(isolated.Data.Clipboard.Count == 0, "Own clipboard writes do not duplicate history");
            WriteExternalClipboard("external clipboard test", false); PumpUntil(() => isolated.Data.Clipboard.Count == 1, "External clipboard listener");
            Check(isolated.Data.Clipboard[0].Text == "external clipboard test", "WM_CLIPBOARDUPDATE captures external copied text");
            WriteExternalClipboard("private clipboard test", true); PumpFor(300);
            Check(isolated.Data.Clipboard.Count == 1, "Native listener skips excluded sensitive clipboard content");
            isolated.Data.Preferences.HistoryPaused = true; WriteExternalClipboard("paused clipboard test", false); PumpFor(300);
            Check(isolated.Data.Clipboard.Count == 1, "Native clipboard listener respects pause"); isolated.Data.Preferences.HistoryPaused = false;
            Clipboard.Clear();
            }
            File.WriteAllText(storage.FilePath, "corrupt-data"); library.Save(); library.Save(); File.WriteAllText(storage.FilePath, "corrupt-data");
            var recovery = new Storage(storage.Root); var recovered = recovery.Load();
            Check(recovery.LoadWarning != null && recovered.Snippets.Count == 2, "Corrupt primary recovers backup with visible warning");
            Check(Directory.GetFiles(storage.Root, "*.unreadable-*").Length == 1, "Unreadable original preserved");
            report.AppendLine($"RESULT: {passed} checks passed.");
            File.WriteAllText(Path.Combine(parent, "self-test-results.txt"), report.ToString()); return 0;
        }
        catch (Exception ex)
        {
            report.AppendLine("FAIL " + ex); File.WriteAllText(Path.Combine(parent, "self-test-results.txt"), report.ToString()); return 1;
        }
    }
    private static void WriteExternalClipboard(string text, bool exclude)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { var data = new DataObject(); data.SetText(text); if (exclude) data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[] { 1 })); Clipboard.SetDataObject(data, true); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error != null) throw error;
    }
    private static void PumpUntil(Func<bool> ready, string label)
    {
        var frame = new DispatcherFrame(); var start = DateTime.UtcNow; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (ready() || DateTime.UtcNow - start > TimeSpan.FromSeconds(8)) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); if (!ready()) throw new Exception("Timeout: " + label);
    }
    private static void PumpFor(int milliseconds) { var until = DateTime.UtcNow.AddMilliseconds(milliseconds); PumpUntil(() => DateTime.UtcNow >= until, "Dispatcher pump"); }
}
