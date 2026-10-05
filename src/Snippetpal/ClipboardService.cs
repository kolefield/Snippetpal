using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Snippetpal;

public sealed class ClipboardService : IDisposable
{
    private readonly HwndSource source;
    private readonly Library library;
    private readonly CaptureService captures;
    private uint ownSequence;
    private int generation;
    public event Action<string>? Status;
    public ClipboardService(IntPtr hwnd, Library library, CaptureService captures)
    {
        this.library = library; this.captures = captures;
        source = HwndSource.FromHwnd(hwnd)!; source.AddHook(Hook);
        if (!NativeMethods.AddClipboardFormatListener(hwnd)) throw new InvalidOperationException("Windows could not start clipboard monitoring.");
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x031D) _ = ReadAsync(++generation);
        return IntPtr.Zero;
    }
    private async Task ReadAsync(int currentGeneration)
    {
        if (library.Data.Preferences.HistoryPaused) return;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (currentGeneration != generation || library.Data.Preferences.HistoryPaused) return;
            uint sequence = NativeMethods.GetClipboardSequenceNumber();
            if (sequence == ownSequence) return;
            try
            {
                // Respect Windows and password-manager opt-out formats before reading content.
                if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing") ||
                    Clipboard.ContainsData("Clipboard Viewer Ignore") ||
                    Clipboard.ContainsData("application/x-keepassxc") ||
                    DisallowsHistory(Clipboard.GetData("CanIncludeInClipboardHistory"))) return;
                if (Clipboard.ContainsImage())
                {
                    var bitmap = Clipboard.GetImage();
                    if (bitmap == null) return;
                    bitmap.Freeze();
                    await captures.SaveClipboardImageAsync(bitmap);
                }
                else if (Clipboard.ContainsText(TextDataFormat.UnicodeText)) library.AddClipboard(Clipboard.GetText(TextDataFormat.UnicodeText));
                return;
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            { await Task.Delay(45 * (attempt + 1)); }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
            { Status?.Invoke("This clipboard item could not be added to history."); return; }
        }
        Status?.Invoke("Clipboard is busy. The next copied item will be captured.");
    }
    internal static bool DisallowsHistory(object? value)
    {
        if (value is int number) return number == 0;
        if (value is byte[] bytes && bytes.Length >= 4) return BitConverter.ToInt32(bytes, 0) == 0;
        if (value is MemoryStream stream && stream.Length >= 4) return BitConverter.ToInt32(stream.ToArray(), 0) == 0;
        return false;
    }
    public async Task<bool> CopyTextAsync(string text) => await WriteAsync(() => Clipboard.SetDataObject(text, true));
    public async Task<bool> CopyImageAsync(string path) => await WriteAsync(() => Clipboard.SetImage(CaptureService.LoadImage(path)));
    public async Task<bool> CopyFileAsync(string path) => await WriteAsync(() =>
    {
        var files = new System.Collections.Specialized.StringCollection { path }; Clipboard.SetFileDropList(files);
    });
    private async Task<bool> WriteAsync(Action write)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try { write(); ownSequence = NativeMethods.GetClipboardSequenceNumber(); ++generation; return true; }
            catch (Exception ex) when (ex is ExternalException) { await Task.Delay(50 * (attempt + 1)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            { Status?.Invoke("Cannot copy this item. Its source file may have moved."); return false; }
        }
        Status?.Invoke("Another app is using the clipboard. Please try again."); return false;
    }
    public void Dispose() { ++generation; NativeMethods.RemoveClipboardFormatListener(source.Handle); source.RemoveHook(Hook); }
}
