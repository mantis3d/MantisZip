using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「文件类型关联 + 打开对话框筛选」两个集合的读写口径（<see cref="AssocPreferences"/>）与目录默认值。
/// </summary>
public class AssocPreferencesTests
{
    [Fact]
    public void Catalog_EngineExtensions_ExcludeSplitVolumePlaceholder()
    {
        Assert.Contains(".zip", ArchiveExtensionCatalog.EngineExtensions);
        Assert.Contains(".jar", ArchiveExtensionCatalog.EngineExtensions);
        // .001 只是分卷占位，真实格式由基名决定，不能当独立引擎扩展名注册到资源管理器
        Assert.DoesNotContain(".001", ArchiveExtensionCatalog.EngineExtensions);
    }

    [Fact]
    public void Catalog_ListsAliasOnceInSettings()
    {
        var listed = ArchiveExtensionCatalog.Listed.Select(i => i.Extension).ToList();
        Assert.Contains(".tgz", listed);
        // .tar.gz 与 .tgz 同义，设置页只列一行
        Assert.DoesNotContain(".tar.gz", listed);
        // 2026-10-29 新增的可配置行
        Assert.Contains(".jar", listed);
        Assert.Contains(".apk", listed);
        Assert.Contains(".001", listed);
    }

    [Fact]
    public void Catalog_Defaults_DoNotGrabJarApkOrVolumeAssociation()
    {
        // 默认不抢 .jar/.apk 的默认程序（issue #54），也不接管可能不是压缩包的 .001
        Assert.DoesNotContain(".jar", ArchiveExtensionCatalog.DefaultAssocExtensions);
        Assert.DoesNotContain(".apk", ArchiveExtensionCatalog.DefaultAssocExtensions);
        Assert.DoesNotContain(".001", ArchiveExtensionCatalog.DefaultAssocExtensions);
        // 但默认都在「打开压缩包」筛选里
        Assert.Contains(".jar", ArchiveExtensionCatalog.DefaultOpenDialogExtensions);
        Assert.Contains(".001", ArchiveExtensionCatalog.DefaultOpenDialogExtensions);
    }

    [Fact]
    public void GetAssocExtensions_Default_AppendsCustomExtensions()
    {
        var settings = new AppSettings { AssocExtensions = null, CustomAssocExtensions = [".xyz"] };
        var assoc = AssocPreferences.GetAssocExtensions(settings);
        Assert.Contains(".zip", assoc);
        Assert.Contains(".xyz", assoc);
        Assert.True(AssocPreferences.IsAssocEnabled(settings, ".xyz"));
    }

    [Fact]
    public void SetAssocEnabled_TogglesSingleExtension()
    {
        var settings = new AppSettings();
        AssocPreferences.SetAssocEnabled(settings, ".7z", false);
        Assert.False(AssocPreferences.IsAssocEnabled(settings, ".7z"));
        Assert.True(AssocPreferences.IsAssocEnabled(settings, ".zip"));

        AssocPreferences.SetAssocEnabled(settings, ".7z", true);
        Assert.True(AssocPreferences.IsAssocEnabled(settings, ".7z"));
        // 写回的是归一化后的集合（小写、带点、无重复）
        Assert.Equal(settings.AssocExtensions!.Distinct(StringComparer.Ordinal).Count(), settings.AssocExtensions!.Count);
    }

    [Fact]
    public void SetAssocEnabled_TreatsTgzAndTarGzAsSameFormat()
    {
        var settings = new AppSettings();
        AssocPreferences.SetAssocEnabled(settings, ".tar.gz", false);
        Assert.False(AssocPreferences.IsAssocEnabled(settings, ".tgz"));
        Assert.False(AssocPreferences.IsAssocEnabled(settings, ".tar.gz"));

        AssocPreferences.SetAssocEnabled(settings, ".tgz", true);
        Assert.True(AssocPreferences.IsAssocEnabled(settings, ".tar.gz"));
    }

    [Fact]
    public void SetOpenDialogVisible_DoesNotTouchAssociationColumn()
    {
        var settings = new AppSettings();
        AssocPreferences.SetOpenDialogVisible(settings, ".7z", false);

        Assert.False(AssocPreferences.IsOpenDialogVisible(settings, ".7z"));
        // 关联列不受影响：取消「打开展示」不等于取消文件关联
        Assert.True(AssocPreferences.IsAssocEnabled(settings, ".7z"));
    }

    [Fact]
    public void MigrateLegacyFlags_ConvertsBoolsAndCustomExtensions()
    {
        var settings = new AppSettings
        {
            AssocZip = true,
            Assoc7z = false,
            AssocRar = true,
            AssocTar = false,
            AssocTarGz = true,
            AssocGz = false,
            AssocIso = true,
            CustomAssocExtensions = [".XYZ"],
        };

        var migrated = AssocPreferences.MigrateLegacyFlags(settings);

        Assert.Contains(".zip", migrated);
        Assert.Contains(".rar", migrated);
        Assert.Contains(".tgz", migrated);
        Assert.Contains(".iso", migrated);
        Assert.Contains(".xyz", migrated);
        Assert.DoesNotContain(".7z", migrated);
        Assert.DoesNotContain(".tar", migrated);
        Assert.DoesNotContain(".gz", migrated);
    }

    [Fact]
    public void Normalize_DropsBlankEntriesAndKeepsFirstOccurrenceOrder()
    {
        var normalized = AssocPreferences.Normalize([".ZIP", "  ", ".zip", "7z", "", ".tar.gz", ".tgz"]);
        Assert.Equal([".zip", ".7z", ".tar.gz", ".tgz"], normalized);
    }
}
