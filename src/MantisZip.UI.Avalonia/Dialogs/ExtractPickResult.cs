namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 解压目录选择结果：目标目录 + 本次解压是否保留压缩包内完整路径。
/// </summary>
/// <remarks>
/// 决策 A：仅本次生效，不回写 <c>AppSettings.ExtractPreserveFullPath</c>——设置窗口仍是唯一来源。
/// <para>
/// 本 record 存在的理由：原对话框只返回 <c>Task&lt;string?&gt;</c>，丢弃了用户在弹窗内表达的勾选意图，
/// 调用方拿到路径后**回头独立读设置值**再传给解压流程，形成「预览所见 ≠ 实际落盘」。
/// 改成强类型返回后，类型系统保证该值一定传到解压侧。
/// </para>
/// <para>
/// 参数区（<see cref="PickerOptionItem"/> 宿主）可承载任意多个参数，但**已知参数仍用强类型字段**承载；
/// 未来确需动态参数时再另行扩展为携带参数字典，不在此预先泛化（YAGNI）。
/// </para>
/// </remarks>
/// <param name="DestPath">目标目录绝对路径。</param>
/// <param name="PreserveFullPath">
/// 本次解压是否保留压缩包内完整路径。<c>true</c> = 保留压缩包内路径；
/// <c>false</c> = 裁剪当前浏览文件夹前缀（语义与 <c>ExtractPathResolver.TrimCurrentFolderPrefix</c> 一致）。
/// </param>
public sealed record ExtractPickResult(string DestPath, bool PreserveFullPath);