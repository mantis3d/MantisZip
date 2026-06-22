using System;
using System.Linq;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Managers;

public class PathHistoryManagerTests
{
    public PathHistoryManagerTests()
    {
        PathHistoryManager.Clear();
    }

    [Fact]
    public void GetRecent_InitiallyEmpty()
    {
        var entries = PathHistoryManager.GetRecent();
        Assert.Empty(entries);
    }

    [Fact]
    public void Record_AddsEntry()
    {
        PathHistoryManager.Record(@"D:\Test");
        var entries = PathHistoryManager.GetRecent();
        Assert.Single(entries);
        Assert.Equal(@"D:\Test", entries[0].Path);
    }

    [Fact]
    public void Record_DuplicateMovesToTop()
    {
        PathHistoryManager.Record(@"D:\First");
        PathHistoryManager.Record(@"D:\Second");
        PathHistoryManager.Record(@"D\:\First"); // Different path

        PathHistoryManager.Record(@"D:\First");
        var entries = PathHistoryManager.GetRecent();
        Assert.Equal(@"D:\First", entries[0].Path);
    }

    [Fact]
    public void Record_EmptyPath_Ignored()
    {
        PathHistoryManager.Record("");
        var entries = PathHistoryManager.GetRecent();
        Assert.Empty(entries);
    }

    [Fact]
    public void Record_NullPath_Ignored()
    {
        PathHistoryManager.Record(null!);
        var entries = PathHistoryManager.GetRecent();
        Assert.Empty(entries);
    }

    [Fact]
    public void Record_Max50Entries()
    {
        for (int i = 0; i < 55; i++)
            PathHistoryManager.Record(@"D:\Path_" + i);

        var entries = PathHistoryManager.GetRecent();
        Assert.Equal(50, entries.Count);
    }

    [Fact]
    public void GetRecent_ReturnsMostRecentFirst()
    {
        PathHistoryManager.Record(@"D:\Alpha");
        PathHistoryManager.Record(@"D:\Beta");
        PathHistoryManager.Record(@"D:\Gamma");

        var entries = PathHistoryManager.GetRecent();
        Assert.Equal(@"D:\Gamma", entries[0].Path);
        Assert.Equal(@"D:\Beta", entries[1].Path);
        Assert.Equal(@"D:\Alpha", entries[2].Path);
    }

    [Fact]
    public void Record_RecentDuplicate_DoesNotDuplicate()
    {
        PathHistoryManager.Record(@"D:\PathA");
        PathHistoryManager.Record(@"D:\PathB");
        PathHistoryManager.Record(@"D:\PathA"); // duplicate

        var entries = PathHistoryManager.GetRecent();
        Assert.Equal(2, entries.Count);
        Assert.Equal(@"D:\PathA", entries[0].Path); // moved to top
        Assert.Equal(@"D:\PathB", entries[1].Path);
    }

    [Fact]
    public void Record_IgnoresDuplicateConsecutive()
    {
        PathHistoryManager.Record(@"D:\Same");
        PathHistoryManager.Record(@"D:\Same");
        Assert.Single(PathHistoryManager.GetRecent());
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        PathHistoryManager.Record(@"D:\A");
        PathHistoryManager.Record(@"D:\B");
        PathHistoryManager.Clear();

        Assert.Empty(PathHistoryManager.GetRecent());
    }

    [Fact]
    public void Record_TrimsTo50WhenFull()
    {
        for (int i = 0; i < 50; i++)
            PathHistoryManager.Record(@"D:\Path_" + i);

        // Add one more - should push oldest out
        PathHistoryManager.Record(@"D:\NewPath");
        var entries = PathHistoryManager.GetRecent();

        Assert.Equal(50, entries.Count);
        Assert.Equal(@"D:\NewPath", entries[0].Path);
        // The oldest (Path_0) should be gone
        Assert.DoesNotContain(entries, e => e.Path == @"D:\Path_0");
    }
}