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
}
