using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Services;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Avalonia UI layer wrapper for Core's <see cref="MantisZip.Core.Services.CompressService"/>.
/// Provides compression with progress reporting for the Avalonia frontend.
/// </summary>
public class AvaloniaCompressService
{
    /// <summary>
    /// Compress files with progress reporting.
    /// Delegates to <see cref="MantisZip.Core.Services.CompressService.CompressAsync"/>.
    /// </summary>
    /// <param name="request">Compression request specifying source paths, format, options.</param>
    /// <param name="progress">Optional progress reporter for <see cref="ArchiveProgress"/> updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="conflictResolver">Optional callback for resolving file conflicts during compression.
    /// When null, existing files are silently overwritten.</param>
    /// <param name="onItemStatus">Optional callback for per-item status updates (Separate mode batch list).
    /// Invoked from a background thread with the item index and its status.</param>
    /// <returns>A <see cref="CompressResult"/> with success/failure/skip counts.</returns>
    public async Task<CompressResult> CompressAsync(
        CompressRequest request,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken ct = default,
        CompressConflictResolver? conflictResolver = null,
        Action<int, BatchItemStatus>? onItemStatus = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Wrap progress to ensure non-null for Core's required parameter.
        var innerProgress = progress ?? new Progress<ArchiveProgress>();

        return await MantisZip.Core.Services.CompressService.CompressAsync(
            request, conflictResolver, innerProgress, ct, onItemStatus);
    }

    /// <summary>
    /// Compute output paths for a <see cref="CompressRequest"/>.
    /// Delegates to <see cref="MantisZip.Core.Services.CompressService.GetOutputPaths"/>.
    /// </summary>
    /// <param name="request">Compression request.</param>
    /// <returns>List of computed output file paths.</returns>
    public static List<string> GetOutputPaths(CompressRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MantisZip.Core.Services.CompressService.GetOutputPaths(request);
    }
}
