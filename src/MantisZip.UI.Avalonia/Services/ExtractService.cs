using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Avalonia UI layer wrapper for archive extraction.
/// Uses <see cref="ArchiveEngineFactory"/> to resolve the appropriate engine
/// and delegates to <see cref="IArchiveEngine.ExtractAsync"/> with progress reporting.
/// </summary>
public class ExtractService
{
    /// <summary>
    /// Extract an archive to the specified destination path with progress reporting.
    /// </summary>
    /// <param name="archivePath">Full path to the archive file.</param>
    /// <param name="destinationPath">Directory to extract into.</param>
    /// <param name="password">Optional archive password.</param>
    /// <param name="progress">Optional progress reporter for <see cref="ArchiveProgress"/> updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when the archive format is not supported.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the archive file does not exist.</exception>
    public async Task ExtractAsync(
        string archivePath,
        string destinationPath,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("Archive path cannot be null or empty.", nameof(archivePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path cannot be null or empty.", nameof(destinationPath));
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive file not found.", archivePath);

        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null)
            throw new InvalidOperationException(
                $"Unsupported archive format: {Path.GetExtension(archivePath)}");

        await engine.ExtractAsync(archivePath, destinationPath, password, progress, ct);
    }
}
