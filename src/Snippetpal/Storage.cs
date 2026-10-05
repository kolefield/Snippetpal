using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Snippetpal;

// Windows DPAPI ties saved text and metadata to the current Windows account.
public sealed class Storage
{
    public string Root { get; }
    public string Cache => Path.Combine(Root, "Images");
    public string FilePath => Path.Combine(Root, "library.dat");
    public string? LoadWarning { get; private set; }
    public Storage(string root) { Root = root; Directory.CreateDirectory(root); Directory.CreateDirectory(Cache); }

    public AppData Load()
    {
        if (!File.Exists(FilePath)) return new AppData();
        try { return Read(FilePath); }
        catch (Exception error) when (error is IOException or JsonException or Win32Exception or InvalidDataException)
        {
            // Preserve unreadable data before a future save. Never silently overwrite the only copy.
            File.Copy(FilePath, FilePath + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            if (File.Exists(FilePath + ".bak"))
            {
                try { var data = Read(FilePath + ".bak"); LoadWarning = "Recovered your library from its last backup. The unreadable file was preserved."; return data; }
                catch (Exception backupError) when (backupError is IOException or JsonException or Win32Exception or InvalidDataException) { }
            }
            LoadWarning = "The saved library could not be read. Its original file was preserved in the data folder.";
            return new AppData();
        }
    }
    private static AppData Read(string path)
    {
        var data = JsonSerializer.Deserialize<AppData>(Protect(File.ReadAllBytes(path), false)) ?? throw new InvalidDataException("Empty library");
        if (data.Version != 1 || data.Preferences == null || data.Snippets == null || data.Clipboard == null || data.Captures == null || data.DismissedCaptureVersions == null)
            throw new InvalidDataException("Unsupported library format");
        data.Preferences.HistoryLimit = Math.Clamp(data.Preferences.HistoryLimit, 25, 500);
        return data;
    }
    public void Save(AppData data)
    {
        var bytes = Protect(JsonSerializer.SerializeToUtf8Bytes(data), true);
        var temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(true); }
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
        else File.Move(temp, FilePath);
    }
    public void ForgetBackup() { if (File.Exists(FilePath + ".bak")) File.Delete(FilePath + ".bak"); }
    public bool IsOwnedImage(string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(Cache) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public void DeleteOwnedImage(CaptureEntry capture)
    {
        if (capture.Owned && IsOwnedImage(capture.Path))
        { try { File.Delete(capture.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob data, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob data, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            Blob output;
            bool success = encrypt ? CryptProtectData(ref input, "Snippetpal", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
}
