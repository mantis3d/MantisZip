using System.IO.Compression;
using System.Text;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// Tests for the multi-source overload of <see cref="ZipBinaryRewriter.RewriteAsync(string[], string, HashSet{string}, List{NewEntry}, Encoding, string, IProgress{Abstractions.ArchiveProgress}, CancellationToken)"/>.
///
/// Multi-source copy-mode merge is the backbone of N-group parallel ZIP compression:
/// every group produces its own ZIP, and those ZIPs are then spliced together
/// byte-for-byte (never decompressed, never recompressed) into the final archive.
/// These tests pin down the observable contract: all sources' entries survive,
/// entry payloads stay byte-identical, and the atomic-replace temp file is cleaned up.
/// </summary>
public class ZipBinaryRewriterMultiSourceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mz_rewrite_ms").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Build a small in-memory ZIP on disk with the given entries.</summary>
    private string MakeZip(string name, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var fs = File.Create(path);
        using var za = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            var e = za.CreateEntry(entry);
            using var w = new StreamWriter(e.Open(), Encoding.UTF8);
            w.Write(content);
        }
        return path;
    }

    [Fact]
    public async Task MergeTwoSources_ContainsAllEntriesFromBoth()
    {
        var a = MakeZip("a.zip", ("a1.txt", "AAA"), ("a2.txt", "BBB"));
        var b = MakeZip("b.zip", ("b1.txt", "CCC"));
        var dest = Path.Combine(_dir, "merged.zip");

        await ZipBinaryRewriter.RewriteAsync(new[] { a, b }, dest, null, null, Encoding.UTF8);

        using var za = ZipFile.OpenRead(dest);
        var names = za.Entries.Select(e => e.FullName).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "a1.txt", "a2.txt", "b1.txt" }, names);
    }

    [Fact]
    public async Task MergePreservesEntryContentByteForByte()
    {
        var payload = new string('x', 5000);
        var a = MakeZip("a.zip", ("big.txt", payload));
        var b = MakeZip("b.zip", ("small.txt", "s"));
        var dest = Path.Combine(_dir, "merged.zip");

        await ZipBinaryRewriter.RewriteAsync(new[] { a, b }, dest, null, null, Encoding.UTF8);

        using var za = ZipFile.OpenRead(dest);
        using var r = new StreamReader(za.GetEntry("big.txt")!.Open());
        Assert.Equal(payload, await r.ReadToEndAsync());
    }

    [Fact]
    public async Task MergeThreeSources_EntryCountIsSum()
    {
        var a = MakeZip("a.zip", ("a1.txt", "1"));
        var b = MakeZip("b.zip", ("b1.txt", "2"));
        var c = MakeZip("c.zip", ("c1.txt", "3"));
        var dest = Path.Combine(_dir, "merged.zip");

        await ZipBinaryRewriter.RewriteAsync(new[] { a, b, c }, dest, null, null, Encoding.UTF8);

        using var za = ZipFile.OpenRead(dest);
        Assert.Equal(3, za.Entries.Count);
    }

    [Fact]
    public async Task SingleSourceOverload_BehavesLikeMultiSource()
    {
        var a = MakeZip("a.zip", ("only.txt", "Z"));
        var dest = Path.Combine(_dir, "one.zip");

        await ZipBinaryRewriter.RewriteAsync(new[] { a }, dest, null, null, Encoding.UTF8);

        using var za = ZipFile.OpenRead(dest);
        Assert.Single(za.Entries);
    }

    [Fact]
    public async Task TempFileIsCleanedUpOnSuccess()
    {
        var a = MakeZip("a.zip", ("x.txt", "1"));
        var dest = Path.Combine(_dir, "out.zip");

        await ZipBinaryRewriter.RewriteAsync(new[] { a }, dest, null, null, Encoding.UTF8);

        Assert.False(File.Exists(dest + ".tmp"));
    }

    // ══════════════════════════════════════════════════════════
    // Failure paths — no half-product
    // ══════════════════════════════════════════════════════════
    //
    // Task 4 merges N per-group ZIPs through this overload, so a merge that
    // fails halfway must leave NOTHING behind: no destination file, and no
    // orphaned .tmp. These tests forge the four inputs copy-mode rejects.

    /// <summary>
    /// Neither the destination nor the temp file may survive a failed merge.
    /// </summary>
    private static void AssertNoHalfProduct(string dest)
    {
        Assert.False(File.Exists(dest), "failed merge must not produce a destination file");
        Assert.False(File.Exists(dest + ".tmp"), "failed merge must clean up the temp file");
    }

    /// <summary>
    /// Build a single-entry ZIP byte-by-byte so we can forge fields that
    /// <see cref="ZipArchive"/> refuses to emit (encryption flag, exotic
    /// compression methods, a ZIP64 end-of-central-directory record).
    /// Payload is Store (method 0), so no compressor is involved.
    /// </summary>
    private string MakeRawZip(
        string name,
        string entryName,
        string payload,
        ushort compressionMethod = 0,
        ushort flags = 0,
        bool zip64 = false)
    {
        var nameBytes = Encoding.UTF8.GetBytes(entryName);
        var data = Encoding.UTF8.GetBytes(payload);
        uint crc = Crc32(data);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // ── Local file header ──
        w.Write(0x04034b50u);              // signature
        w.Write((ushort)20);               // version needed to extract
        w.Write(flags);                    // general purpose bit flag
        w.Write(compressionMethod);
        w.Write(crc);
        w.Write((uint)data.Length);        // compressed size
        w.Write((uint)data.Length);        // uncompressed size
        w.Write((ushort)nameBytes.Length);
        w.Write((ushort)0);                // extra field length
        w.Write(nameBytes);
        w.Write(data);

        // ── Central directory header ──
        uint cdOffset = (uint)ms.Position;
        w.Write(0x02014b50u);              // signature
        w.Write((ushort)20);               // version made by
        w.Write((ushort)20);               // version needed
        w.Write(flags);
        w.Write(compressionMethod);
        w.Write(crc);
        w.Write((uint)data.Length);
        w.Write((uint)data.Length);
        w.Write((ushort)nameBytes.Length);
        w.Write((ushort)0);                // extra field length
        w.Write((ushort)0);                // comment length
        w.Write((ushort)0);                // disk number start
        w.Write((ushort)0);                // internal attributes
        w.Write(0u);                       // external attributes
        w.Write(0u);                       // relative offset of local header
        w.Write(nameBytes);
        uint cdSize = (uint)(ms.Position - cdOffset);

        if (zip64)
        {
            // A real ZIP64 archive lays down EOCD record + locator before the
            // EOCD. The rewriter sniffs for the locator signature exactly
            // 20 bytes ahead of the EOCD, so both records are required —
            // merely setting entryCount to 0xFFFF is not enough to trip it.
            long z64EocdOffset = ms.Position;
            w.Write(0x06064b50u);          // ZIP64 EOCD signature
            w.Write((ulong)44);            // size of ZIP64 EOCD record - 12
            w.Write((ushort)45);           // version made by
            w.Write((ushort)45);           // version needed
            w.Write(0u);                   // number of this disk
            w.Write(0u);                   // disk with start of CD
            w.Write((ulong)1);             // entries on this disk
            w.Write((ulong)1);             // total entries
            w.Write((ulong)cdSize);
            w.Write((ulong)cdOffset);

            // ZIP64 EOCD locator — fixed 20 bytes
            w.Write(0x07064b50u);
            w.Write(0u);                   // disk with the ZIP64 EOCD
            w.Write((ulong)z64EocdOffset); // relative offset of ZIP64 EOCD
            w.Write(1u);                   // total number of disks
        }

        // ── End of central directory ──
        w.Write(0x06054b50u);              // signature
        w.Write((ushort)0);                // number of this disk
        w.Write((ushort)0);                // disk with start of CD
        w.Write((ushort)(zip64 ? 0xFFFF : 1)); // entries on this disk
        w.Write((ushort)(zip64 ? 0xFFFF : 1)); // total entries
        w.Write(zip64 ? 0xFFFFFFFFu : cdSize);
        w.Write(zip64 ? 0xFFFFFFFFu : cdOffset);
        w.Write((ushort)0);                // comment length

        w.Flush();
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    [Fact]
    public async Task EmptySourceList_ThrowsArgumentException()
    {
        var dest = Path.Combine(_dir, "out.zip");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ZipBinaryRewriter.RewriteAsync(Array.Empty<string>(), dest, null, null, Encoding.UTF8));

        AssertNoHalfProduct(dest);
    }

    [Fact]
    public async Task SfxSource_Throws_AndLeavesNoHalfProduct()
    {
        // A self-extracting archive starts with "MZ"; copy-mode refuses it
        // because the payload offsets assume a bare ZIP.
        var raw = File.ReadAllBytes(MakeRawZip("body.bin", "b.txt", "BBB"));
        var sfx = Path.Combine(_dir, "sfx.zip");
        var patched = new byte[raw.Length + 2];
        patched[0] = (byte)'M';
        patched[1] = (byte)'Z';
        raw.CopyTo(patched, 2);
        File.WriteAllBytes(sfx, patched);

        var a = MakeZip("a.zip", ("a.txt", "AAA"));
        var dest = Path.Combine(_dir, "out.zip");

        await Assert.ThrowsAsync<ZipCopyModeException>(() =>
            ZipBinaryRewriter.RewriteAsync(new[] { a, sfx }, dest, null, null, Encoding.UTF8));

        AssertNoHalfProduct(dest);
    }

    [Fact]
    public async Task EncryptedEntry_Throws_AndLeavesNoHalfProduct()
    {
        // general purpose bit flag bit 0 = encrypted
        var a = MakeZip("a.zip", ("a.txt", "AAA"));
        var enc = MakeRawZip("enc.zip", "secret.txt", "SSS", flags: 0x0001);
        var dest = Path.Combine(_dir, "out.zip");

        await Assert.ThrowsAsync<ZipCopyModeException>(() =>
            ZipBinaryRewriter.RewriteAsync(new[] { a, enc }, dest, null, null, Encoding.UTF8));

        AssertNoHalfProduct(dest);
    }

    [Fact]
    public async Task UnsupportedCompressionMethod_Throws_AndLeavesNoHalfProduct()
    {
        // copy-mode copies bytes verbatim, so it only accepts Store/Deflate/Deflate64
        var a = MakeZip("a.zip", ("a.txt", "AAA"));
        var bzip = MakeRawZip("bz.zip", "b.txt", "BBB", compressionMethod: 12);
        var dest = Path.Combine(_dir, "out.zip");

        await Assert.ThrowsAsync<ZipCopyModeException>(() =>
            ZipBinaryRewriter.RewriteAsync(new[] { a, bzip }, dest, null, null, Encoding.UTF8));

        AssertNoHalfProduct(dest);
    }

    [Fact]
    public async Task Zip64Source_Throws_AndLeavesNoHalfProduct()
    {
        var a = MakeZip("a.zip", ("a.txt", "AAA"));
        var z64 = MakeRawZip("z64.zip", "z.txt", "ZZZ", zip64: true);
        var dest = Path.Combine(_dir, "out.zip");

        await Assert.ThrowsAsync<ZipCopyModeException>(() =>
            ZipBinaryRewriter.RewriteAsync(new[] { a, z64 }, dest, null, null, Encoding.UTF8));

        AssertNoHalfProduct(dest);
    }

    [Fact]
    public async Task FailureMidway_LeavesDestinationUntouched()
    {
        // The sharpest failure mode: source 1 is copied successfully (so the
        // temp file already holds real bytes) and source 2 then trips a guard.
        // The pre-existing destination must survive byte-identical, and the
        // partially written temp file must be discarded — not renamed over it.
        var dest = Path.Combine(_dir, "existing.zip");
        byte[] original = "PRE-EXISTING PAYLOAD"u8.ToArray();
        File.WriteAllBytes(dest, original);

        var a = MakeZip("a.zip", ("a.txt", "AAA"));
        var enc = MakeRawZip("enc.zip", "secret.txt", "SSS", flags: 0x0001);

        await Assert.ThrowsAsync<ZipCopyModeException>(() =>
            ZipBinaryRewriter.RewriteAsync(new[] { a, enc }, dest, null, null, Encoding.UTF8));

        // The destination still holds the pre-existing bytes ...
        Assert.True(File.Exists(dest), "pre-existing destination must not be removed");
        Assert.Equal(original, await File.ReadAllBytesAsync(dest));
        // ... and the half-written temp file is gone.
        Assert.False(File.Exists(dest + ".tmp"), "partial temp file must be discarded");
    }
}
