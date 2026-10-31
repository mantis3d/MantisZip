using System.Threading.Tasks;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 拖拽解压的收尾动作：打开目录照常，但**绝不删原包**（spec §11.2 D18：拖拽入口不显示「移入回收站」）。
/// 与 ExtractFlow 的 allowDeleteArchive:false 同一语义，改成 postActions 后由唯一执行器表达。
/// </summary>
internal sealed class DragDropExtractPostActions : IExtractPostActions
{
    public bool SupportsOpenFolder => true;

    /// <summary>拖拽解压不允许删原包：忽略 enabled，什么都不做。</summary>
    public void DeleteArchive(string archivePath, bool enabled)
    {
        // 有意留空（见类注释）
    }

    public Task OpenFolderAsync(string destinationPath, string archivePath, string? password)
        => new DefaultExtractPostActions().OpenFolderAsync(destinationPath, archivePath, password);
}