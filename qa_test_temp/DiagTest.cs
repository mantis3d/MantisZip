using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests;

public class TempDiagTest
{
    [Fact]
    public void SingleChar_Diagnostic()
    {
        var items = new List<ArchiveItem>
        {
            new() { Name = "readme.txt", FullPath = "readme.txt", Size = 100, LastModified = DateTime.Now },
            new() { Name = "notes.md", FullPath = "notes.md", Size = 200, LastModified = DateTime.Now },
            new() { Name = "archive.zip", FullPath = "archive.zip", Size = 50000, LastModified = DateTime.Now },
            new() { Name = "main.cs", FullPath = "src/main.cs", Size = 1500, LastModified = DateTime.Now },
            new() { Name = "util.cs", FullPath = "src/util.cs", Size = 800, LastModified = DateTime.Now },
        };

        var rSubA = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "a", MatchMode = FilterMatchMode.Substring });
        Assert.NotEqual(items.Count, rSubA.Count); // "a" should NOT match ALL

        var rSubM = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "m", MatchMode = FilterMatchMode.Substring });
        Assert.NotEqual(items.Count, rSubM.Count); // "m" should NOT match ALL

        var rWildA = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "a", MatchMode = FilterMatchMode.Wildcard });
        Assert.Empty(rWildA); // "a" in wildcard = exact match "a" → 0 results

        var rWildM = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "m", MatchMode = FilterMatchMode.Wildcard });
        Assert.Empty(rWildM); // "m" in wildcard = exact match "m" → 0 results
    }
}
