namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// <see cref="PlaceholderInsertButton"/> 服务于哪一套占位符。
/// 两套模板的 token 集合不同，列错了会诱导用户写出必错的模板
/// （根名里没有 <c>{ext}</c>/<c>{seq}</c>）。
/// </summary>
public enum PlaceholderSet
{
    /// <summary>压缩包文件名模板（8 个占位符）。</summary>
    FileName,

    /// <summary>包内根目录名模板（5 个占位符）。</summary>
    RootName,
}
