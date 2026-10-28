using System.Collections.Generic;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 压缩源条目：一个待压缩的源（文件或目录）+ 本次压缩里它要落到的压缩包路径。
/// </summary>
/// <remarks>
/// 之所以把「源」和「目标」成对放进载荷，而不是只放一句
/// <c>IReadOnlyList&lt;string&gt; SourcePaths</c>：Separate 模式下一个源对应一个目标压缩包，
/// 目标路径是用户在确认那一刻就定下的意图；执行器若重新推导（再问一次 ViewModel 或按当前
/// 设置重算），就会在「确认之后、执行之前」这段窗口里被外部状态改动污染，出现
/// 「确认的意图 ≠ 实际执行」的偏差。成对记录后，执行器不需要再做任何推导。
/// </remarks>
/// <param name="SourcePath">源文件或源目录的绝对路径。</param>
/// <param name="TargetArchivePath">该源对应的目标压缩包路径（含文件名与扩展名）。</param>
public sealed record CompressSourceEntry(string SourcePath, string TargetArchivePath);

/// <summary>
/// 压缩计划条目：一个源的执行计划，额外携带「本次实际要打进包里的文件白名单」。
/// </summary>
/// <remarks>
/// <para>
/// 计划条目与 <see cref="CompressSourceEntry"/> 的区别是 <paramref name="IncludedFiles"/>：
/// 文件过滤器（扩展名/名称/大小/日期）会把目录里的部分文件裁掉，裁剪结果只在「构建预览」
/// 那一刻是确定的，之后用户仍可能继续改动过滤条件。把裁剪结果冻结进载荷，执行器才能保证
/// 「预览里看到的就是压出来的」。
/// </para>
/// <para>
/// <paramref name="IncludedFiles"/> 为 <c>null</c> 表示该源没有启用的过滤器（整份打进包），
/// 与 <c>空集合</c>（过滤后一个文件都不剩）语义不同：前者正常执行，后者在
/// <c>CompressService</c> 里被整体跳过。用 <c>null</c> 与空集合区分这两种情况，避免执行器
/// 把「没过滤」误判成「全被过滤掉」。
/// </para>
/// </remarks>
/// <param name="SourcePath">源文件或源目录的绝对路径。</param>
/// <param name="TargetArchivePath">该源对应的目标压缩包路径。</param>
/// <param name="IncludedFiles">
/// 过滤后实际要归档的文件白名单；<c>null</c> 表示未启用过滤器。
/// </param>
public sealed record CompressPlanEntry(
    string SourcePath,
    string TargetArchivePath,
    IReadOnlyList<string>? IncludedFiles);

/// <summary>
/// 与具体格式绑定的压缩引擎选项：压缩方法、加密方法、分卷等「引擎才会读」的开关。
/// </summary>
/// <remarks>
/// 这些字段在现状实现里分散在 Z 与 7z 两套参数里（同一批输入对 zip 用一套、对 7z 用另一套），
/// 但它们同属「用户在确认那一刻选定的引擎行为」。单独聚成一个记录而不是平铺进
/// <see cref="CompressSettings"/>，是为了让载荷的顶层保持「意图」可读性：顶层回答
/// 「压什么、压成什么、压到哪、怎么处理冲突」，这里回答「引擎怎么压」。
/// </remarks>
/// <param name="ZipCompressionMethod">ZIP 专用压缩方法（store/deflate/deflate64/bzip2/lzma/ppmd…）。</param>
/// <param name="ZipEncryptionMethod">ZIP 专用加密方法（aes256/zipcrypto 等）。</param>
/// <param name="SevenZipCompressionMethod">7z 专用压缩方法（lzma2/lzma/bzip2/copy…）。</param>
/// <param name="SevenZipSolid">7z 是否使用固实压缩。</param>
/// <param name="SevenZipSolidBlockSize">7z 固实块大小；<c>null</c> 表示交给引擎默认值。</param>
/// <param name="SevenZipDictionarySize">7z 字典大小；<c>null</c> 表示交给引擎默认值。</param>
/// <param name="SevenZipNumFastBytes">7z 快速字节数；<c>null</c> 表示交给引擎默认值。</param>
/// <param name="SevenZipMatchFinder">7z 匹配查找器；<c>null</c> 表示交给引擎默认值。</param>
/// <param name="SevenZipMultithreaded">7z 引擎内部是否多线程。</param>
/// <param name="SevenZipEncryptHeaders">7z 是否连文件头一起加密（隐藏文件名）。</param>
/// <param name="FileNameEncoding">文件名编码（ZIP 遗留编码兼容用）；<c>null</c> 表示自动。</param>
/// <param name="KeepOriginalExtension">是否保留源文件的原始扩展名。</param>
/// <param name="AdaptiveCompression">是否启用自适应压缩（按文件类型自动挑算法）。</param>
/// <param name="AdaptiveSmartDetect">自适应压缩是否启用智能识别。</param>
public sealed record CompressEngineOptions(
    string? ZipCompressionMethod,
    string? ZipEncryptionMethod,
    string? SevenZipCompressionMethod,
    bool SevenZipSolid,
    string? SevenZipSolidBlockSize,
    string? SevenZipDictionarySize,
    int? SevenZipNumFastBytes,
    string? SevenZipMatchFinder,
    bool SevenZipMultithreaded,
    bool SevenZipEncryptHeaders,
    string? FileNameEncoding,
    bool KeepOriginalExtension,
    bool AdaptiveCompression,
    bool AdaptiveSmartDetect);

/// <summary>
/// 成功压缩之后把密码写回密码库的意图（用户是否勾选、写到哪一条、匹配哪些文件）。
/// </summary>
/// <remarks>
/// 密码回写是「成功之后才发生的第二件事」，它依赖压缩结果（失败就不写），因此不能塞进执行
/// 参数里由引擎顺手做；但它的全部输入又必须在确认那一刻冻结——否则执行期间用户关掉设置窗口、
/// 或改动了描述文本，回写的内容就会与用户确认时看到的不一致。这里只记录「意图与文本」，
/// 真正的写库动作由会话在成功后执行。
/// </remarks>
/// <param name="SaveToLibrary">用户是否勾选了「保存到密码库」。</param>
/// <param name="IsLibraryMode">确认时是否处于「从密码库选密码」模式（决定是更新已有条目还是新建）。</param>
/// <param name="LibraryEntryId">密码库模式下的目标条目 Id；非密码库模式为 <c>null</c>。</param>
/// <param name="LibraryEntryPatterns">目标条目在确认那一刻已有的匹配规则（用于去重后追加）。</param>
/// <param name="LibraryEntryDescription">目标条目在确认那一刻的描述（写库时原样回写）。</param>
/// <param name="Description">新建条目时用户填写的描述（可为空，空则由会话生成默认描述）。</param>
/// <param name="RulesText">用户手写的匹配规则原文（多行，会话负责切分去空）。</param>
public sealed record CompressPasswordWriteback(
    bool SaveToLibrary,
    bool IsLibraryMode,
    string? LibraryEntryId,
    IReadOnlyList<string> LibraryEntryPatterns,
    string? LibraryEntryDescription,
    string? Description,
    string? RulesText);

/// <summary>
/// 一次压缩确认的完整不可变载荷：用户在压缩设置对话框按下「确认」的那一刻，
/// 「执行一次压缩所需要的一切输入」被冻结成值语义快照。执行器只读它，不再回读任何 ViewModel。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要这个载荷：</b>现状的执行路径（<c>MainWindowViewModel.ExecuteCompressFromSettings</c>）
/// 直接持有 <c>CompressSettingsViewModel</c> 并在压缩前后反复读它的属性、还写回它的
/// <c>SelectedPasswordEntry.Patterns</c>。这让「用户确认过的意图」与「实际执行的参数」之间
/// 隔着一个可变的活对象：设置窗口在压缩开始后被改动（或对象早已关闭销毁），执行结果就可能
/// 偏离用户确认的内容。快照把这段距离消除掉——确认即事务，之后只有一个只读的输入。
/// </para>
/// <para>
/// <b>集合一律用 <see cref="IReadOnlyList{T}"/>：</b>载荷一旦被创建就视为不再变化；
/// 宿主之后对原 ViewModel 集合做的任何增删都不应该再影响这次执行。执行器若需要可变副本
/// （例如过滤掉空条目），必须自己新建列表，而不是就地修改这里的东西。
/// </para>
/// <para>
/// <b>边界（这个类型刻意不包含什么）：</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>不含宿主依赖</b>：没有 Window / 进度窗口 / 展示器的引用，也没有
/// <see cref="CompressRunContext.ExitPolicy"/> 之类的宿主策略。它们是与「压什么」无关的
/// 宿主编排，放在 <see cref="CompressRunContext"/> 里，让同一份载荷能在 GUI 与 CLI 两种宿主下复用。
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>不含可变的回传通道</b>：没有 <c>Action</c> 回调、没有指向 ViewModel 的引用、没有
/// 「执行完请写回这里」的输出槽。执行结果统一由 <c>CompressOutcome</c> 返回，
/// 单向往外流，避免执行器偷偷改写调用方的状态。
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>不含呈现概念</b>：没有 UI 控件、没有 <c>Visibility</c>、没有「已本地化的标题」之类的
/// 界面状态。文案与可见性属于视图层，执行器只认业务字段；否则同一份载荷没法在无 UI 的
/// （CLI / 测试）场景下使用。
/// </description>
/// </item>
/// </list>
/// </remarks>
/// <param name="Sources">用户确认时列在「源」里的全部条目，含各自的目标压缩包路径。</param>
/// <param name="OutputMode">输出模式（每个源一个包 / 合并成一个包 / 手动指定路径）。</param>
/// <param name="Format">压缩格式（zip / 7z / tar.gz / 自定义格式名）。</param>
/// <param name="CompressionLevel">压缩级别（0-9，沿用引擎的既有取值）。</param>
/// <param name="EngineOptions">与格式绑定的引擎选项（压缩方法、固实、自适应等）。</param>
/// <param name="OutputPath">输出位置：单文件模式为压缩包路径，目录模式为输出目录；可为 <c>null</c>（按源推导）。</param>
/// <param name="Encrypt">是否启用加密（决定密码是否参与执行）。</param>
/// <param name="Password">加密密码（明文，仅在 <paramref name="Encrypt"/> 为真时使用）。</param>
/// <param name="PasswordWriteback">成功之后是否把密码写回密码库、写到哪一条。</param>
/// <param name="Comment">压缩包注释。</param>
/// <param name="CommentDistribution">注释分布策略（整个包 / 每个条目 / 两者）。</param>
/// <param name="SplitSizeBytes">分卷大小（字节）；0 表示不分卷。</param>
/// <param name="MultiThreaded">是否启用多线程压缩（引擎级开关）。</param>
/// <param name="ParallelDegree">并行压缩度（Separate 模式下同时压几个源）；0 表示由引擎/设置推导。</param>
/// <param name="ConflictAction">目标已存在时的冲突动作（ask/overwrite/add/rename/skip）。</param>
/// <param name="Filter">确认时生效的文件过滤器条件；<c>null</c> 表示未启用过滤。</param>
/// <param name="Plan">逐源执行计划，含「过滤后实际归档文件白名单」。</param>
public sealed record CompressSettings(
    IReadOnlyList<CompressSourceEntry> Sources,
    CompressOutputMode OutputMode,
    string Format,
    int CompressionLevel,
    CompressEngineOptions EngineOptions,
    string? OutputPath,
    bool Encrypt,
    string? Password,
    CompressPasswordWriteback PasswordWriteback,
    string? Comment,
    CommentDistribution CommentDistribution,
    long SplitSizeBytes,
    bool MultiThreaded,
    int ParallelDegree,
    string? ConflictAction,
    FileFilterCriteria? Filter,
    IReadOnlyList<CompressPlanEntry> Plan);
