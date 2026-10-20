using Xunit;

// 本测试程序集多处读写全局静态状态——尤其是 LocalizationManager.CurrentLanguage
// （ProgressViewModelTests 的「语言即时刷新」用例会临时切换语言并还原）。
// xUnit 默认按测试类并行执行集合，语言切换会与其它类的 LocalizationManager.T 读取
// 产生竞态，导致本地化断言偶发假失败（曾观察到 PasswordStrength / IoErrorHintResolver
// 等用例随机失败）。关闭并行执行以保证确定性——测试集体量小，串行代价可接受。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
