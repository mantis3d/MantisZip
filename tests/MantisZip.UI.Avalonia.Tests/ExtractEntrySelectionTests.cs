using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「选择要解压的文件」条目选择模式（v0.5.5 第三步后半，2026-10-30）：
/// ① 窗口在条目模式下显示条目页、隐藏「添加压缩包」页，并默认选中条目页；
/// ② 载荷只收勾选的条目（未勾选的不参与解压）。
/// </summary>
public sealed class ExtractEntrySelectionTests
{
    [AvaloniaFact]
    public void EntrySelectionMode_ShowsEntryTabAndHidesAddArchivesTab()
    {
        var window = CreateEntryModeWindow(out var entries);
        try
        {
            window.Show();
            Pump(window);
            window.SetEntries(entries);

            var entryTab = window.FindControl<TabItem>("EntrySelectionTab");
            var addTab = window.FindControl<TabItem>("AddArchivesTab");
            Assert.NotNull(entryTab);
            Assert.NotNull(addTab);
            Assert.True(entryTab!.IsVisible);
            Assert.True(entryTab.IsSelected);
            Assert.False(addTab!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void BuildSelectedEntries_OnlyIncludesCheckedRows()
    {
        var window = CreateEntryModeWindow(out var entries);
        try
        {
            var vm = window.ViewModel;
            vm.InitializeEntrySelection(entries, currentFolder: null, preserveFullPath: true);
            Assert.Equal(entries.Count, vm.EntryRows.Count);
            Assert.All(vm.EntryRows, r => Assert.True(r.IsSelected));

            // 取消勾选第一条：载荷里不应再出现它
            vm.EntryRows[0].IsSelected = false;
            var selected = vm.BuildSelectedEntries();
            Assert.Equal(entries.Count - 1, selected.Count);
            Assert.DoesNotContain(selected, i => (i.FullPath ?? i.Name) == (entries[0].FullPath ?? entries[0].Name));
        }
        finally
        {
            window.Close();
        }
    }

    private static ExtractSettingsWindow CreateEntryModeWindow(out IReadOnlyList<ArchiveItem> entries)
    {
        var items = new List<ArchiveItem>
        {
            new() { FullPath = "docs/a.txt", Name = "a.txt", Size = 3, IsDirectory = false },
            new() { FullPath = "docs/b.txt", Name = "b.txt", Size = 5, IsDirectory = false },
            new() { FullPath = "readme.md", Name = "readme.md", Size = 7, IsDirectory = false },
        };
        entries = items;

        var window = new ExtractSettingsWindow(
            new NullExtractSession(),
            new ExtractRunContext(Owner: null, Presenter: new NullPresenter(), ExitPolicy: ExitPolicy.None),
            new[] { @"C:\fake\sample.zip" },
            hideAddArchivesTab: true,
            entrySelectionMode: true,
            entryCurrentFolder: null,
            entryPreserveFullPath: true);
        return window;
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private sealed class NullExtractSession : IExtractSession
    {
        public Task<ExtractOutcome> RunAsync(ExtractSettings settings, ExtractRunContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<ExtractOutcome>(ExtractOutcome.Completed);
    }

    private sealed class NullPresenter : IProgressPresenter
    {
        public ProgressWindow? ActiveWindow => null;

        public Task<bool> RunAsync(ProgressRunRequest request) => Task.FromResult(true);
    }
}