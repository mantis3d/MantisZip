namespace MantisZip.Core.Models;

public enum ExtractOutputMode
{
    Here,    // 解压到此处（压缩包所在目录）
    Smart,   // 智能解压（分析结构后自动选择）
    ToName,  // 解压到压缩包名（所在目录/包名/）
    Manual   // 手动输入（用户指定目录）
}
