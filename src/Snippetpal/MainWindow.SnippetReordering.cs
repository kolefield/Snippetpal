using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Snippetpal;

public partial class MainWindow
{
    private const string SnippetDragFormat = "Snippetpal.SnippetReorder";
    private Snippet? pendingSnippet, draggedSnippet;
    private Point dragOrigin;
    private Point? dragPosition;
    private readonly DispatcherTimer dragScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };

    private void ReorderHandleDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: Snippet snippet } handle) return;
        handle.Focus();
        pendingSnippet = snippet;
        dragOrigin = e.GetPosition(ItemsList);
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void ReorderHandleUp(object sender, MouseButtonEventArgs e)
    {
        pendingSnippet = null;
        ((Button)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ReorderHandleLostCapture(object sender, MouseEventArgs e) => pendingSnippet = null;

    private void ReorderHandleMove(object sender, MouseEventArgs e)
    {
        if (pendingSnippet is not { } snippet || e.LeftButton != MouseButtonState.Pressed) return;
        Point position = e.GetPosition(ItemsList);
        if (Math.Abs(position.X - dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        pendingSnippet = null;
        ((Button)sender).ReleaseMouseCapture();
        draggedSnippet = snippet;
        // Only a private ID enters the drag data; the snippet's text never leaves the app.
        var data = new DataObject(SnippetDragFormat, snippet.Id.ToString(), false);
        try { DragDrop.DoDragDrop(ItemsList, data, DragDropEffects.Move); }
        catch (COMException) { Report("Could not start dragging. Try again, or use Ctrl+Up / Ctrl+Down on the handle."); }
        finally
        {
            draggedSnippet = null;
            ClearSnippetDropMarker();
            Refresh();
            ItemsList.ScrollIntoView(snippet);
        }
        e.Handled = true;
    }

    private bool IsSnippetDrag(DragEventArgs e) => tab == 0 && draggedSnippet != null &&
        e.Data.GetDataPresent(SnippetDragFormat, false) &&
        e.Data.GetData(SnippetDragFormat, false) is string id && id == draggedSnippet.Id.ToString();

    private void SnippetDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (!IsSnippetDrag(e)) { ClearSnippetDropMarker(); return; }
        dragPosition = e.GetPosition(ItemsList);
        if (UpdateSnippetDropMarker(dragPosition.Value))
        {
            e.Effects = DragDropEffects.Move;
            dragScrollTimer.Start();
        }
        else ClearSnippetDropMarker();
    }

    private void SnippetDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (IsSnippetDrag(e) && FindSnippetDropTarget(e.GetPosition(ItemsList)) is { } target &&
            library.MoveSnippet(draggedSnippet!, target.Snippet, target.After)) e.Effects = DragDropEffects.Move;
        ClearSnippetDropMarker();
    }

    private void SnippetDragLeave(object sender, DragEventArgs e)
    {
        if (!new Rect(ItemsList.RenderSize).Contains(e.GetPosition(ItemsList))) ClearSnippetDropMarker();
    }

    // Resolve against actual visible rows, including partially visible rows and variable heights.
    // Using an anchor (not a filtered index) preserves the relative order of hidden search results.
    private (Snippet Snippet, bool After, double Y)? FindSnippetDropTarget(Point position)
    {
        var viewport = VisualChildren<ScrollContentPresenter>(ItemsList).FirstOrDefault();
        if (viewport == null) return null;
        var viewportBounds = new Rect(viewport.TranslatePoint(new Point(), ItemsList), viewport.RenderSize);
        if (!viewportBounds.Contains(position)) return null;
        (Snippet Snippet, bool After, double Y)? last = null;
        foreach (var row in VisualChildren<ListBoxItem>(ItemsList).OrderBy(x => x.TranslatePoint(new Point(), ItemsList).Y))
        {
            if (row.DataContext is not Snippet snippet) continue;
            var bounds = new Rect(row.TranslatePoint(new Point(), ItemsList), row.RenderSize);
            if (bounds.Bottom < viewportBounds.Top || bounds.Top > viewportBounds.Bottom) continue;
            bool after = position.Y >= bounds.Top + bounds.Height / 2;
            // Rows include an 8px bottom gap; center the insertion line in that gap.
            last = (snippet, after, after ? bounds.Bottom - 4 : bounds.Top - 4);
            if (position.Y <= bounds.Bottom) return last;
        }
        return last;
    }

    private bool UpdateSnippetDropMarker(Point position)
    {
        if (FindSnippetDropTarget(position) is not { } target) return false;
        SnippetDropMarker.RenderTransform = new TranslateTransform(0, Math.Clamp(target.Y, 0, Math.Max(0, ItemsList.ActualHeight - 2)));
        SnippetDropMarker.Visibility = Visibility.Visible;
        return true;
    }

    private void ScrollDuringSnippetDrag()
    {
        if (draggedSnippet == null || dragPosition is not { } position) { ClearSnippetDropMarker(); return; }
        var scroll = VisualChildren<ScrollViewer>(ItemsList).FirstOrDefault();
        if (scroll == null) return;
        if (position.Y < 28) scroll.LineUp();
        else if (position.Y > ItemsList.ActualHeight - 28) scroll.LineDown();
        else return;
        ItemsList.UpdateLayout();
        UpdateSnippetDropMarker(position);
    }

    private void ClearSnippetDropMarker()
    {
        dragScrollTimer.Stop();
        dragPosition = null;
        SnippetDropMarker.Visibility = Visibility.Collapsed;
    }

    private void ReorderHandleKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Key is not (Key.Up or Key.Down) ||
            sender is not Button { DataContext: Snippet snippet }) return;
        e.Handled = true;
        var visible = ItemsList.Items.OfType<Snippet>().ToList();
        int target = visible.IndexOf(snippet) + (e.Key == Key.Up ? -1 : 1);
        if (target < 0 || target >= visible.Count || !library.MoveSnippet(snippet, visible[target], e.Key == Key.Down)) return;
        ItemsList.ScrollIntoView(snippet);
        ItemsList.UpdateLayout();
        if (ItemsList.ItemContainerGenerator.ContainerFromItem(snippet) is ListBoxItem row)
            VisualChildren<Button>(row).FirstOrDefault(x => x.Name == "ReorderHandle")?.Focus();
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }
}
