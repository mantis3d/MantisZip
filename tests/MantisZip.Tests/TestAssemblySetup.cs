using System.Runtime.CompilerServices;
using System.Text;

namespace MantisZip.Tests;

/// <summary>
/// 测试程序集级初始化。
///
/// 背景：.NET Core+ 默认不包含 GBK（代码页 936）等编码，必须先调用
/// <see cref="Encoding.RegisterProvider"/> 注册 <see cref="CodePagesEncodingProvider"/>。
/// 生产环境由 <c>App.axaml.cs</c> 在启动时注册；测试宿主没有这条启动路径，
/// 此前依赖各测试类构造函数里的零散注册（ZipBinaryRewriterTests / ZipEngineTests 等）。
///
/// 问题：这种「谁先跑谁注册」的隐式依赖在 xUnit 并行调度下不可靠 ——
/// 未自行注册的测试类（如 ZipBinaryRewriterMultiSourceTests）一旦被先调度，
/// 就会在 <c>Encoding.GetEncoding("gbk")</c> 处抛
/// <c>ArgumentException: 'gbk' is not a supported encoding name</c>。
/// 表现为「全量跑通过、单类跑失败」的顺序依赖型偶发失败。
///
/// 方案：用模块初始化器把注册提前到程序集加载期，先于任何测试执行，彻底消除顺序依赖。
/// 各测试类构造函数里已有的注册调用保持不动（幂等，无副作用）。
/// </summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void Initialize()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
