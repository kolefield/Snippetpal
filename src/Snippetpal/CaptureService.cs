using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snippetpal;

public sealed class CaptureService : IDisposable
{
    private readonly Library library;
    private readonly DispatcherTimer timer;
    private readonly List<FileSystemWatcher> watchers = new();
    private bool scanning, disposed;
    private int generation;
    public event Action<string>? Status;
    public CaptureService(Library library)
    {
        this.library = library;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += async (_, _) => await ScanAsync();
        Reconfigure(); timer.Start();
    }
    public void Reconfigure()
    {
        generation++;
        foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear();
        foreach (string folder in new[] { library.Data.Preferences.ScreenshotFolder, library.Data.Preferences.RecordingFolder }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
                watcher.Created += OnFile; watcher.Changed += OnFile; watcher.Renamed += OnFile;
                watchers.Add(watcher);
            }
            catch (IOException) { Status?.Invoke("A capture folder is unavailable. It will be checked again shortly."); }
        }
        _ = ScanAsync();
    }
    private async void OnFile(object sender, FileSystemEventArgs args)
    {
        // A short settling delay plus periodic retry handles Snipping Tool's multi-stage writes.
        await Task.Delay(1500);
        if (!disposed) await timer.Dispatcher.InvokeAsync(async () => await ScanAsync()).Task.Unwrap();
    }
    public async Task ScanAsync()
    {
        if (scanning || disposed || library.Data.Preferences.HistoryPaused) return;
        scanning = true; int scanGeneration = generation;
        string screenshots = library.Data.Preferences.ScreenshotFolder, recordings = library.Data.Preferences.RecordingFolder;
        var known = library.Data.Captures.ToDictionary(x => x.Path, x => (x.ModifiedTicks, x.Size), StringComparer.OrdinalIgnoreCase);
        DateTimeOffset watermark = library.Data.CaptureWatermark;
        var dismissed = library.Data.DismissedCaptureVersions.ToHashSet();
        try
        {
            var result = await Task.Run(() => ScanFiles(screenshots, recordings, known, watermark, dismissed));
            if (!disposed && scanGeneration == generation && !library.Data.Preferences.HistoryPaused) library.AddCaptures(result.Entries);
            if (result.Error != null) Status?.Invoke(result.Error);
        }
        finally { scanning = false; }
    }
    private static (List<CaptureEntry> Entries, string? Error) ScanFiles(string screenshots, string recordings, Dictionary<string, (long Ticks, long Size)> known, DateTimeOffset watermark, HashSet<string> dismissed)
    {
        var results = new List<CaptureEntry>(); string? error = null;
        foreach (var folder in new[] { screenshots, recordings }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var candidates = new DirectoryInfo(folder).EnumerateFiles().Where(x => IsSupported(x.Extension))
                    .OrderByDescending(x => x.LastWriteTimeUtc).Take(500);
                foreach (var file in candidates)
                {
                    try
                    {
                        if (file.Length == 0 || DateTime.UtcNow - file.LastWriteTimeUtc < TimeSpan.FromSeconds(1)) continue;
                        if (known.TryGetValue(file.FullName, out var version) && version == (file.LastWriteTimeUtc.Ticks, file.Length)) continue;
                        var entry = new CaptureEntry { Path = file.FullName, Created = file.LastWriteTimeUtc, Size = file.Length, ModifiedTicks = file.LastWriteTimeUtc.Ticks, IsVideo = IsVideo(file.Extension) };
                        if (entry.Created <= watermark || dismissed.Contains(Library.CaptureVersion(entry))) continue;
                        using (var check = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
                        if (!entry.IsVideo)
                        {
                            if (file.Length > 32 * 1024 * 1024) continue;
                            entry.Fingerprint = Fingerprint(LoadImage(file.FullName));
                        }
                        else entry.Fingerprint = Library.CaptureVersion(entry);
                        results.Add(entry);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException) { /* Retry incomplete or unavailable files on the next scan. */ }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { error = "A capture folder cannot be read. Check its location in Settings."; }
        }
        return (results, error);
    }
    public static bool IsVideo(string extension) => new[] { ".mp4", ".mov", ".wmv", ".webm" }.Contains(extension, StringComparer.OrdinalIgnoreCase);
    public static bool IsSupported(string extension) => IsVideo(extension) || new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }.Contains(extension, StringComparer.OrdinalIgnoreCase);
    public static BitmapSource LoadImage(string path, int decodeWidth = 0)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    public static string Fingerprint(BitmapSource image)
    {
        if ((long)image.PixelWidth * image.PixelHeight > 40_000_000) throw new NotSupportedException("Image too large");
        var normalized = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = checked(image.PixelWidth * 4); var pixels = new byte[checked(stride * image.PixelHeight)];
        normalized.CopyPixels(pixels, stride, 0);
        return $"{image.PixelWidth}x{image.PixelHeight}:" + Convert.ToHexString(SHA256.HashData(pixels));
    }
    public async Task SaveClipboardImageAsync(BitmapSource bitmap)
    {
        int savedGeneration = generation;
        try
        {
            var entry = await Task.Run(() =>
            {
                string fingerprint = Fingerprint(bitmap);
                string path = Path.Combine(library.Storage.Cache, Guid.NewGuid().ToString("N") + ".png");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
                var file = new FileInfo(path);
                return new CaptureEntry { Path = path, Fingerprint = fingerprint, Owned = true, Size = file.Length, ModifiedTicks = file.LastWriteTimeUtc.Ticks };
            });
            if (disposed || savedGeneration != generation || library.Data.Preferences.HistoryPaused) library.Storage.DeleteOwnedImage(entry);
            else library.AddCaptures(new[] { entry });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { Status?.Invoke("This image could not be saved to capture history."); }
    }
    public void Dispose() { disposed = true; generation++; timer.Stop(); foreach (var watcher in watchers) watcher.Dispose(); }
}
