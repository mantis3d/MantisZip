# Issues - ZIP Copy-Mode Optimization

## Test Implementation (2026-06-19)

### SharpZipLib ZipOutputStream always triggers ZIP64 detection
SharpZipLib 1.4.2's `ZipOutputStream` defaults to `UseZip64.Dynamic`, which writes ZIP64 extra fields even for tiny entries (< 100 bytes). The `ZipBinaryRewriter` checks `CompressedSize >= 0xFFFFFFFF` and rejects ZIP64 archives. This means `ArchiveFixtures.CreateZipArchive()` cannot be used for direct ZipBinaryRewriter tests.

**Fix**: Always set `zipStream.UseZip64 = UseZip64.Off` when creating test ZIPs for copy-mode testing. Integration tests through `ZipEngine` are unaffected (they fall back to legacy path).

### InternalsVisibleTo not set for test project
`MantisZip.Core.csproj` has `InternalsVisibleTo` for `MantisZip.UI` only. `ZipBinaryRewriter` is `internal`, so tests must use reflection to invoke it directly. Integration tests through `ZipEngine` public API work without reflection.

### TaskCanceledException vs OperationCanceledException
`ZipEngine` wraps operations in `Task.Run(...)`, which converts `OperationCanceledException` to `TaskCanceledException` for the caller. Tests must use `Assert.ThrowsAnyAsync<OperationCanceledException>()` instead of `Assert.ThrowsAsync<OperationCanceledException>()`.

