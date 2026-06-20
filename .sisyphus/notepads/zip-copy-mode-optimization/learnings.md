# Learnings - ZIP Copy-Mode Optimization

## Purpose
Track conventions, patterns, and insights discovered during implementation.

## Test Implementation Findings (2026-06-19)

### ZipBinaryRewriter is `internal` — tests need reflection
- `ZipBinaryRewriter` class is `internal static partial`, but `RewriteAsync` method is `public static`.
- Core's `.csproj` has `InternalsVisibleTo` only for `MantisZip.UI`, not for `MantisZip.Tests`.
- Solution: use reflection to invoke `RewriteAsync` via `typeof(ZipEngine).Assembly.GetType("...")`.
- `RewriteResult` and `NewEntry` are `public` type — no reflection needed for them.

### SharpZipLib ZipOutputStream defaults to ZIP64
- SharpZipLib 1.4.2's `ZipOutputStream` defaults to `UseZip64.Dynamic`, which writes ZIP64 extra fields even for small entries.
- `ZipBinaryRewriter` explicitly rejects ZIP64 (`CompressedSize >= 0xFFFFFFFF` checks).
- **Fix for tests**: always set `zipStream.UseZip64 = UseZip64.Off` when creating test ZIPs for copy-mode testing.
- Integration tests through `ZipEngine` (AddToArchiveAsync, DeleteEntriesAsync) don't need this fix — they fall back to legacy path on `ZipCopyModeException`.

### Cancellation wrapping in Task.Run
- `ZipEngine.AddToArchiveAsync` wraps work in `Task.Run(...)`. When a pre-cancelled token is used, `ThrowIfCancellationRequested()` throws `OperationCanceledException` inside the task, which TPL converts to `TaskCanceledException`.
- **Use `Assert.ThrowsAnyAsync<OperationCanceledException>()`** for ZipEngine-based cancellation tests.
- Direct `ZipBinaryRewriter.RewriteAsync` cancellation tests can use `Assert.ThrowsAsync<OperationCanceledException>()` since there's no `Task.Run` wrapping.

### Encrypted archive fallback behavior
- Direct `ZipBinaryRewriter.RewriteAsync` on encrypted ZIP throws `ZipCopyModeException` immediately.
- `ZipEngine.AddToArchiveAsync` on encrypted ZIP: copy-mode throws → caught → legacy path tries to extract encrypted entries without password → `SharpCompress.Common.CryptographicException`.
- `ZipEngine.DeleteEntriesAsync` on encrypted ZIP with password: copy-mode throws → caught → legacy succeeds with password.
- `ZipEngine.DeleteEntriesAsync` encrypt helper test needs `Assert.ThrowsAsync<CryptographicException>()`.

### Only AddToArchiveAsync and DeleteEntriesAsync use copy-mode
- `CompressAsync` always creates a new archive (no copy-mode).
- `ExtractAsync` doesn't modify the archive (reads only).
- copy-mode is only in the Add/Delete paths of `ZipEngine`.

### SharpCompress API note
- `ArchiveFactory.OpenArchive(Stream)` — NOT `ArchiveFactory.Open(Stream)`.
- `ZipArchive.Open(Stream)` from `SharpCompress.Archives.Zip` — only works with that namespace.
- Test project only needs `using SharpCompress.Archives;` for reading output.

