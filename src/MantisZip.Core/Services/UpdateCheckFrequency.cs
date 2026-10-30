namespace MantisZip.Core.Services;

/// <summary>自动检查更新的频率。</summary>
public enum UpdateCheckFrequency
{
    /// <summary>从不自动检查（用户可在设置里关闭）。</summary>
    Never,
    /// <summary>每次启动都检查（仍受「不再提示此版本」约束）。</summary>
    EveryStartup,
    /// <summary>每天最多检查一次。</summary>
    Daily,
    /// <summary>每周最多检查一次。</summary>
    Weekly,
}
