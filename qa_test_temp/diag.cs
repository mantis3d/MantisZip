using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

var items = new List<ArchiveItem>
{
    new() { Name = "readme.txt", FullPath = "readme.txt", Size = 100, LastModified = DateTime.Now },
    new() { Name = "notes.md", FullPath = "notes.md", Size = 200, LastModified = DateTime.Now },
    new() { Name = "archive.zip", FullPath = "archive.zip", Size = 50000, LastModified = DateTime.Now },
    new() { Name = "main.cs", FullPath = "src/main.cs", Size = 1500, LastModified = DateTime.Now },
    new() { Name = "util.cs", FullPath = "src/util.cs", Size = 800, LastModified = DateTime.Now },
    new() { Name = "helper.cs", FullPath = "src/utils/helper.cs", Size = 3000, LastModified = DateTime.Now },
    new() { Name = "converter.cs", FullPath = "src/utils/converter.cs", Size = 12000, LastModified = DateTime.Now },
    new() { Name = "index.html", FullPath = "docs/index.html", Size = 2500, LastModified = DateTime.Now },
    new() { Name = "guide.pdf", FullPath = "docs/guide.pdf", Size = 150000, LastModified = DateTime.Now },
    new() { Name = "logo.png", FullPath = "images/logo.png", Size = 45000, LastModified = DateTime.Now },
    new() { Name = "banner.svg", FullPath = "images/banner.svg", Size = 12000, LastModified = DateTime.Now },
};

// Test: single character "a" in substring mode
var r1 = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "a", MatchMode = FilterMatchMode.Substring });
Console.WriteLine($"Substring 'a': {r1.Count}/{items.Count} matched");
foreach (var i in r1) Console.WriteLine($"  - {i.FullPath}");

// Test: single character "m" in substring mode
var r2 = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "m", MatchMode = FilterMatchMode.Substring });
Console.WriteLine($"Substring 'm': {r2.Count}/{items.Count} matched");
foreach (var i in r2) Console.WriteLine($"  - {i.FullPath}");

// Test: single character "a" in wildcard mode
var r3 = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "a", MatchMode = FilterMatchMode.Wildcard });
Console.WriteLine($"Wildcard 'a': {r3.Count}/{items.Count} matched");
foreach (var i in r3) Console.WriteLine($"  - {i.FullPath}");

// Test: empty string
var r4 = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = null, MatchMode = FilterMatchMode.Substring });
Console.WriteLine($"Null text: {r4.Count}/{items.Count} matched");
