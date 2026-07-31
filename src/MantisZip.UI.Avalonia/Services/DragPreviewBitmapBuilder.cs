using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Pre-renders the drag preview tree to a bitmap before DoDragDropAsync is called.
/// Must be called on the UI thread.
/// </summary>
internal static class DragPreviewBitmapBuilder
{
    /// <summary>
    /// Pre-renders the ResultTreeView to a byte array (BGRA 32bpp) that can be
    /// passed to DragPreviewPopup for display during the drag operation.
    /// </summary>
    public static Task<PreviewBitmapData> RenderAsync(
        IReadOnlyList<ArchiveItem> selectedItems,
        IReadOnlyList<ArchiveItem> allItems,
        ArchiveFormat format,
        string archivePath,
        int maxWidth = 320,
        int maxHeight = 500)
    {
        // 1. Expand items: directories become flat file list, duplicate-free by FullPath
        var expanded = DragDropItemExpander.ExpandItems(selectedItems, allItems);
        if (expanded.Count == 0)
            return Task.FromResult(PreviewBitmapData.Empty);

        // 2. Build preview tree using the shared ResultPreviewService
        var rootName = Path.GetFileName(archivePath) ?? "archive";
        var previewTree = ResultPreviewService.BuildExtractPreview(
            expanded, rootName, rootName, checkExists: false);

        // 3. Create a ResultTreeView control with the preview root
        //    BuildExtractPreview returns destDir → content; wrap content under an
        //    archive-name root for the drag preview display.
        var contentChildren = previewTree.Children;

        var treeView = new ResultTreeView
        {
            Root = new PreviewTreeNode
            {
                Name = rootName,
                FullPath = "",
                DisplayLabel = rootName,
                IsExpanded = true,
                Children = contentChildren
            },
            Width = maxWidth,
            CompactMode = true,
            MaxItemsPerDirectory = 10,
            MaxDepth = 5,
            ShowFilteredGhosts = false,
            ShowSummaryBar = true
        };

        // 4. Apply the template, then measure + arrange the tree
        treeView.ApplyTemplate();

        // Measure with infinite height constraint so the tree grows to its full content
        treeView.Measure(new Size(maxWidth, double.PositiveInfinity));
        var desiredHeight = Math.Min(treeView.DesiredSize.Height, maxHeight);
        treeView.Arrange(new Rect(0, 0, maxWidth, desiredHeight));

        // 5-7. Create RenderTargetBitmap and render the tree
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(treeView.Bounds.Width));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(treeView.Bounds.Height));

        var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
        bitmap.Render(treeView);

        // 8. Copy pixels to BGRA 32bpp byte array
        var stride = pixelWidth * 4;
        var pixels = new byte[stride * pixelHeight];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new PixelRect(0, 0, pixelWidth, pixelHeight),
                handle.AddrOfPinnedObject(),
                pixels.Length,
                stride);
        }
        finally
        {
            handle.Free();
        }

        // 9. Build summary string (total files + total size)
        long totalSize = expanded.Sum(i => i.Size);
        var summary = $"{FormatUtil.FormatSize(totalSize)} — {expanded.Count} 个文件";

        return Task.FromResult(new PreviewBitmapData
        {
            Pixels = pixels,
            Width = pixelWidth,
            Height = pixelHeight,
            Summary = summary,
            TotalFiles = expanded.Count
        });
    }
}

/// <summary>
/// Holds the pre-rendered bitmap data and metadata for the drag preview popup.
/// </summary>
public class PreviewBitmapData
{
    /// <summary>BGRA 32bpp pixel data (premultiplied alpha).</summary>
    public byte[] Pixels { get; set; } = Array.Empty<byte>();

    /// <summary>Bitmap width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Bitmap height in pixels.</summary>
    public int Height { get; set; }

    /// <summary>Summary line displayed in the popup bar (e.g. "12.3 MB — 45 个文件").</summary>
    public string Summary { get; set; } = "";

    /// <summary>Total file count in the selection.</summary>
    public int TotalFiles { get; set; }

    /// <summary>Singleton empty instance for no-op / empty selection.</summary>
    public static readonly PreviewBitmapData Empty = new();
}
