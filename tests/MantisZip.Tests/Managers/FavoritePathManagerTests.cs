using System;
using System.Linq;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Managers;

public class FavoritePathManagerTests
{
    [Fact]
    public void GetAll_ContainsSystemPaths()
    {
        var all = FavoritePathManager.GetAll();
        Assert.NotEmpty(all);
        Assert.Contains(all, i => i.IsSystem);
    }

    [Fact]
    public void Add_Then_GetAll_ContainsUserFavorite()
    {
        var uniquePath = @"D:\__MantisZipTest_" + Guid.NewGuid().ToString("N");
        try
        {
            FavoritePathManager.Add("TestFolder", uniquePath);
            var all = FavoritePathManager.GetAll();
            Assert.Contains(all, i => i.Name == "TestFolder" && i.Path == uniquePath && !i.IsSystem);
        }
        finally
        {
            FavoritePathManager.Remove(uniquePath);
        }
    }

    [Fact]
    public void Add_DuplicatePath_SilentlyIgnored()
    {
        // Add is no-op for system paths (they already exist via system defs)
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        FavoritePathManager.Add("Desktop_cpy", desktop);
        // Should not have created a duplicate - Exists returns true (system path)
        Assert.True(FavoritePathManager.Exists(desktop));
    }

    [Fact]
    public void Add_Then_Remove_RemovesCorrectly()
    {
        var uniquePath = @"D:\__MantisZipTest_" + Guid.NewGuid().ToString("N");
        FavoritePathManager.Add("ToRemove", uniquePath);
        Assert.True(FavoritePathManager.Exists(uniquePath));

        FavoritePathManager.Remove(uniquePath);
        Assert.False(FavoritePathManager.Exists(uniquePath));
    }

    [Fact]
    public void Update_ChangesNameAndPath()
    {
        var oldPath = @"D:\__MantisZipTest_Old_" + Guid.NewGuid().ToString("N");
        var newPath = @"D:\__MantisZipTest_New_" + Guid.NewGuid().ToString("N");
        try
        {
            FavoritePathManager.Add("Before", oldPath);
            FavoritePathManager.Update(oldPath, "After", newPath);

            Assert.False(FavoritePathManager.Exists(oldPath));
            Assert.True(FavoritePathManager.Exists(newPath));

            var updated = FavoritePathManager.GetAll().First(i => i.Path == newPath);
            Assert.Equal("After", updated.Name);
        }
        finally
        {
            FavoritePathManager.Remove(oldPath);
            FavoritePathManager.Remove(newPath);
        }
    }

    [Fact]
    public void Exists_ReturnsFalseForUnknown()
    {
        Assert.False(FavoritePathManager.Exists(@"Z:\__MantisZipTest_NonExistent__"));
    }

    [Fact]
    public void IsSystemPath_ReturnsTrueForDesktop()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        Assert.True(FavoritePathManager.IsSystemPath(desktop));
    }

    [Fact]
    public void SystemPath_AlwaysInGetAll()
    {
        // System paths should survive any operation
        var before = FavoritePathManager.GetAll();
        var desktopCount = before.Count(i => i.IsSystem);
        Assert.True(desktopCount > 0, "Expected at least one system path");
    }

    [Fact]
    public void SetSystemPathHidden_ToggleWorks()
    {
        // Hide then show
        FavoritePathManager.SetSystemPathHidden("Desktop", true);
        var hiddenAll = FavoritePathManager.GetAll();
        Assert.DoesNotContain(hiddenAll, i => i.SystemKey == "Desktop");

        FavoritePathManager.SetSystemPathHidden("Desktop", false);
        var shownAll = FavoritePathManager.GetAll();
        Assert.Contains(shownAll, i => i.SystemKey == "Desktop");
    }

    [Fact]
    public void GetUserFavorites_ExcludesSystemPaths()
    {
        var uniquePath = @"D:\__MantisZipTest_" + Guid.NewGuid().ToString("N");
        try
        {
            FavoritePathManager.Add("UserOnly", uniquePath);
            var userFavs = FavoritePathManager.GetUserFavorites();
            Assert.All(userFavs, i => Assert.False(i.IsSystem));
        }
        finally
        {
            FavoritePathManager.Remove(uniquePath);
        }
    }

    [Fact]
    public void GetSystemPaths_ExcludesUserFavorites()
    {
        var sysPaths = FavoritePathManager.GetSystemPaths();
        Assert.All(sysPaths, i => Assert.True(i.IsSystem));
    }

    [Fact]
    public void Remove_SystemPath_Throws()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        Assert.Throws<InvalidOperationException>(() => FavoritePathManager.Remove(desktop));
    }
}