using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Snippetpal;

public sealed class Library
{
    public AppData Data { get; }
    public Storage Storage { get; }
    public event Action? Changed;
    public event Action<string>? Error;
    public Library(Storage storage) { Storage = storage; Data = storage.Load(); }
    public bool Save(bool eraseBackup = false)
    {
        try { Storage.Save(Data); if (eraseBackup) Storage.ForgetBackup(); Changed?.Invoke(); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Error?.Invoke("Could not save your changes. " + ex.Message); return false; }
    }
    public bool SaveSnippet(Snippet? existing, string name, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length > 1_000_000) { Error?.Invoke("Please keep snippets under 1 million characters."); return false; }
        string oldName = existing?.Name ?? "", oldText = existing?.Text ?? "";
        var snippet = existing ?? new Snippet(); snippet.Name = name.Trim(); snippet.Text = text;
        if (existing == null) Data.Snippets.Insert(0, snippet);
        if (Save()) return true;
        if (existing == null) Data.Snippets.Remove(snippet); else { snippet.Name = oldName; snippet.Text = oldText; }
        return false;
    }
    public bool MoveSnippet(Snippet snippet, Snippet anchor, bool after)
    {
        int source = Data.Snippets.IndexOf(snippet), destination = Data.Snippets.IndexOf(anchor);
        if (source < 0 || destination < 0) return false;
        if (source == destination) return true;
        if (after) destination++;
        if (source < destination) destination--;
        if (source == destination) return true;
        Data.Snippets.RemoveAt(source);
        Data.Snippets.Insert(destination, snippet);
        if (Save()) return true;
        Data.Snippets.RemoveAt(destination);
        Data.Snippets.Insert(source, snippet);
        return false;
    }
    public void AddClipboard(string text)
    {
        if (Data.Preferences.HistoryPaused || string.IsNullOrWhiteSpace(text) || text.Length > 1_000_000) return;
        if (Data.Clipboard.FirstOrDefault()?.Text == text) return;
        Data.Clipboard.RemoveAll(x => x.Text == text);
        Data.Clipboard.Insert(0, new ClipboardEntry { Text = text });
        Data.Clipboard = Data.Clipboard.Take(Data.Preferences.HistoryLimit).ToList(); Save();
    }
    public bool SetHistoryPaused(bool paused)
    {
        bool previous = Data.Preferences.HistoryPaused;
        var previousWatermark = Data.CaptureWatermark;
        Data.Preferences.HistoryPaused = paused;
        if (previous && !paused) Data.CaptureWatermark = DateTimeOffset.Now;
        if (Save()) return true;
        Data.Preferences.HistoryPaused = previous; Data.CaptureWatermark = previousWatermark;
        return false;
    }
    public static string CaptureVersion(CaptureEntry entry) => $"{entry.Path.ToUpperInvariant()}|{entry.ModifiedTicks}|{entry.Size}";
    public bool ShouldImport(CaptureEntry entry) => entry.Created > Data.CaptureWatermark && !Data.DismissedCaptureVersions.Contains(CaptureVersion(entry));
    public void AddCaptures(IEnumerable<CaptureEntry> entries)
    {
        if (Data.Preferences.HistoryPaused) return;
        bool changed = false;
        foreach (var entry in entries.OrderBy(x => x.Created))
        {
            if (!ShouldImport(entry)) continue;
            var existingPath = Data.Captures.FirstOrDefault(x => string.Equals(x.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
            if (existingPath != null && existingPath.ModifiedTicks == entry.ModifiedTicks && existingPath.Size == entry.Size) continue;
            if (existingPath != null) Data.Captures.Remove(existingPath);
            var duplicate = !entry.IsVideo ? Data.Captures.FirstOrDefault(x => x.Fingerprint == entry.Fingerprint && !string.IsNullOrEmpty(entry.Fingerprint)) : null;
            if (duplicate != null)
            {
                // Prefer the user's saved image to an app-owned clipboard copy.
                if (duplicate.Owned && !entry.Owned) { Data.Captures.Remove(duplicate); Storage.DeleteOwnedImage(duplicate); }
                else { Storage.DeleteOwnedImage(entry); continue; }
            }
            Data.Captures.Add(entry); changed = true;
        }
        if (changed) { Trim(); Save(); }
    }
    public void Trim()
    {
        Data.Clipboard = Data.Clipboard.OrderByDescending(x => x.Created).Take(Data.Preferences.HistoryLimit).ToList();
        Data.Captures = Data.Captures.OrderByDescending(x => x.Created).ToList();
        foreach (var entry in Data.Captures.Skip(Data.Preferences.HistoryLimit))
        { Data.CaptureWatermark = entry.Created > Data.CaptureWatermark ? entry.Created : Data.CaptureWatermark; Storage.DeleteOwnedImage(entry); }
        Data.Captures = Data.Captures.Take(Data.Preferences.HistoryLimit).ToList();
    }
    public void RemoveCapture(CaptureEntry entry)
    {
        Data.Captures.Remove(entry); Data.DismissedCaptureVersions.Add(CaptureVersion(entry));
        if (Data.DismissedCaptureVersions.Count > 2000)
        { Data.CaptureWatermark = DateTimeOffset.Now; Data.DismissedCaptureVersions.Clear(); }
        Storage.DeleteOwnedImage(entry); Save(true);
    }
    public void ClearHistory(bool captures)
    {
        if (captures)
        {
            foreach (var entry in Data.Captures) Storage.DeleteOwnedImage(entry);
            Data.Captures.Clear(); Data.CaptureWatermark = DateTimeOffset.Now; Data.DismissedCaptureVersions.Clear();
        }
        else Data.Clipboard.Clear();
        Save(true);
    }
}
