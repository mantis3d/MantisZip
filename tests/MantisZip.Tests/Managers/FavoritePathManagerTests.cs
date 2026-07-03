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
    public void Add_SystemPath_CreatesUserFavorite()
    {
        // User may manually add a favorite with the same path as a system path.
        // This is allowed — the user entry coexists with the system path entry.
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var name = "Desktop_cpy_" + Guid.NewGuid().ToString("N");
        try
        {
            FavoritePathManager.Add(name, desktop);

            // System path entry still exists
            Assert.True(FavoritePathManager.IsSystemPath(desktop));
            // And we also have a user entry with the same path
            var userFavs = FavoritePathManager.GetUserFavorites();
            Assert.Contains(userFavs, f => f.Path == desktop && f.Name == name && !f.IsSystem);
        }
        finally
        {
            FavoritePathManager.Remove(desktop);
        }
    }

    [Fact]
    public void Add_TrailingSlash_Normalized()
    {
        var uniquePath = @"D:\__MantisZipTest_" + Guid.NewGuid().ToString("N");
        try
        {
            FavoritePathManager.Add("SlashTest", uniquePath + @"\");
            Assert.True(FavoritePathManager.Exists(uniquePath));
            var all = FavoritePathManager.GetAll();
            // Path stored without trailing separator
            Assert.Contains(all, i => i.Path == uniquePath);
        }
        finally
        {
            FavoritePathManager.Remove(uniquePath);
        }
    }

    [Fact]
    public void IsSystemPath_TrailingSlash_Normalized()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        Assert.True(FavoritePathManager.IsSystemPath(desktop + @"\"));
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
    public void Remove_SystemPath_DoesNotThrow()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        // System paths are not stored in _userFavorites, so Remove should silently do nothing.
        FavoritePathManager.Remove(desktop);
        // No exception expected — the call is a no-op.
        Assert.True(FavoritePathManager.IsSystemPath(desktop));
    }
}