namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 进度窗口「冲突时」控件的流程类型 —— 决定选项集合与可见性。
/// <see cref="None"/> 用于「测试压缩包 / 删除条目 / 测试菜单」等根本没有冲突概念的流程：
/// 这些窗口不显示该控件（能力位与接线同源，见计划 §7 不变量 1）。
/// </summary>
public enum ConflictPolicyFlow
{
    /// <summary>不显示控件（无冲突概念的流程）。</summary>
    None = 0,

    /// <summary>解压流程：询问 / 覆盖 / 仅覆盖较旧 / 仅覆盖较小 / 重命名 / 跳过。</summary>
    Extract = 1,

    /// <summary>压缩流程：询问 / 覆盖 / 追加到已有包 / 重命名 / 跳过。</summary>
    Compress = 2,
}
