using System.Runtime.CompilerServices;
using System.Text;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 测试程序集级初始化。
///
/// 背景：.NET Core+ 默认不包含 GBK（代码页 936）等编码，必须先调用
/// <see cref="Encoding.RegisterProvider"/> 注册 <see cref="CodePagesEncodingProvider"/>。
/// 生产环境由 <c>App.axaml.cs</c> 在启动时注册；测试宿主没有这条启动路径。
///
/// 问题：此前 UI 测试程序集**没有**模块初始化器——2026-10-20 那次只在
/// <c>tests/MantisZip.Tests/TestAssemblySetup.cs</c>（Core 测试程序集）补了注册，
/// UI 测试程序集仍依赖「某个测试类构造函数里零散注册、正好先跑到」。
/// 2026-10-26 CI 上该假设破功：`WholeArchiveErrorLocalizerTests`（本程序集里最先跑到
/// GBK 路径的用例）在 4.5s 处单跑，无人注册 →
/// <c>WholeArchiveErrorLocalizer</c> 把 <c>ArgumentException: 'gbk' is not a supported
/// encoding name</c> 当成未知异常，渲染成「操作失败：'gbk' …」，用例红。
///
/// 方案：与 Core 测试程序集一致，用模块初始化器把注册提前到程序集加载期，
/// 先于任何测试执行，彻底消除顺序依赖（生产侧另由 <c>ZipEngine</c> 静态构造器自注册兜底）。
/// 各测试类构造函数里已有的注册调用保持不动（幂等，无副作用）。
/// </summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void Initialize()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
