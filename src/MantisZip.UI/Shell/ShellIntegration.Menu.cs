using System.IO;
using Microsoft.Win32;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

internal static partial class ShellIntegration
{
    /// <summary>检查是否已安装 Shell 扩展（COM 或静态注册表）。</summary>
    public static bool IsInstalled
    {
        get
        {
            // 检查 COM 注册（优先）
            using var comKey = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\CLSID\{ComClsid}");
            if (comKey != null) return true;

            // 回退：检查静态注册表
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\*\shell\{CascadeRoot}");
            if (key != null) return true;
            using var key2 = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\*\shell\{CompressVerb}\command");
            if (key2 != null) return true;
            using var key3 = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\*\shell\{ExtractSmartVerb}\command");
            if (key3 != null) return true;
            using var key4 = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\*\shell\{CompressSeparateVerb}\command");
            return key4 != null;
        }
    }

    /// <summary>
    /// 安装 Shell 右键菜单。
    /// 使用 COM IContextMenu 实现（MantisZip.ShellExt.comhost.dll），
    /// 回退到静态注册表方案（旧版兼容）。
    /// </summary>
    public static void Install()
    {
        App.LogDebug("ShellIntegration.Install: starting");
        var s = AppSettings.Instance;
        var exePath = GetExePath();

        // 先清理旧注册，避免残留
        Uninstall();

        // 尝试安装 COM 组件（MantisZip.ShellExt.comhost.dll）
        if (InstallCom())
        {
            App.LogDebug("ShellIntegration.Install: COM context menu installed");
        }
        else
        {
            // 回退到静态注册表方案（仅级联模式，动词模式已在 v0.4.0 移除）
            App.LogDebug("ShellIntegration.Install: COM not available, falling back to static cascade");
            InstallCascade(s, exePath);
        }

        // 通知 Windows Shell 刷新上下文菜单缓存
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        App.LogDebug("ShellIntegration.Install: done, exePath={0}", exePath);
    }

    /// <summary>
    /// 卸载所有 MantisZip 右键菜单注册。
    /// </summary>
    public static void Uninstall()
    {
        App.LogDebug("ShellIntegration.Uninstall: starting");
        // 层叠入口
        foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
        {
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CascadeRoot}");
        }

        // 层叠子命令定义
        DeleteRegistryKey($@"Software\Classes\{CascadeRoot}");

        // 独立动词（含新旧动词名称全量，升级时清理旧版注册）
        foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
        {
            // 新名称
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{OpenVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractHereVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractSmartVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractToNamedVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{ExtractVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressSeparateVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressCombinedVerb}");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\{CompressVerb}");

            // 菜单分隔线（独立动词模式）
            DeleteRegistryKey($@"Software\Classes\{target}\shell\00_MantisZipSepTop");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\02a_MantisZipSep");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\04a_MantisZipSep");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\99_MantisZipSepBottom");

            // 旧版动词名称（v0.1.3 及以前）
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipOpen");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipExtract");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipQuick");
            DeleteRegistryKey($@"Software\Classes\{target}\shell\MantisZipCompress");
        }

        // 清理 COM 组件注册
        UninstallCom();

        // 旧版 per-extension 注册（v0.1.3 早期版本遗留）
        foreach (var ext in ArchiveExtensions)
        {
            DeleteRegistryKey($@"Software\Classes\{ext}\shell\MantisZipExtract");
        }
        // 通知 Windows Shell 刷新上下文菜单缓存
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

        App.LogDebug("ShellIntegration.Uninstall: done");
    }

    #region COM 组件注册/反注册

    /// <summary>
    /// 安装 COM 右键菜单（MantisZip.ShellExt.comhost.dll）。
    /// 写入 HKCU\Software\Classes，无需管理员权限。
    /// 返回 true 表示安装成功，false 表示 COM host DLL 不存在（触发回退）。
    /// </summary>
    private static bool InstallCom()
    {
        try
        {
            var baseDir = Path.GetDirectoryName(GetExePath());
            if (baseDir == null) return false;

            var comhostPath = Path.Combine(baseDir, "MantisZip.ShellExt.comhost.dll");
            if (!File.Exists(comhostPath))
            {
                // Also check subdirectory (x64/x86 for architecture-specific builds)
                comhostPath = Path.Combine(baseDir,
                    Environment.Is64BitProcess ? "x64" : "x86",
                    "MantisZip.ShellExt.comhost.dll");
                if (!File.Exists(comhostPath))
                {
                    App.LogDebug("InstallCom: comhost.dll not found at {0}", comhostPath);
                    return false;
                }
            }

            App.LogDebug("InstallCom: registering from {0}", comhostPath);

            // 1. CLSID 注册（HKCU — per-user）
            //    .NET 6+ comhost.dll 通过 runtimeconfig.json 定位运行时和托管程序集，
            //    不需要 InprocServer32 下额外的 Assembly/Class/CodeBase 值
            var clsidKey = $@"Software\Classes\CLSID\{ComClsid}";
            SetRegistryValue(clsidKey, null, "MantisZip Context Menu Handler");
            SetRegistryValue($@"{clsidKey}\InprocServer32", null, comhostPath);
            SetRegistryValue($@"{clsidKey}\InprocServer32", "ThreadingModel", "Apartment");
            SetRegistryValue($@"{clsidKey}\ProgId", null, ComProgId);
            // ProgId 反向查找（由 COM CreateObject/ProgId 需要）
            SetRegistryValue($@"Software\Classes\{ComProgId}", null, "MantisZip Context Menu Handler");
            SetRegistryValue($@"Software\Classes\{ComProgId}\CLSID", null, ComClsid);

            // 2. 上下文菜单处理程序注册
            foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
            {
                var handlerKey = $@"Software\Classes\{target}\shellex\ContextMenuHandlers\{ComHandlerKey}";
                SetRegistryValue(handlerKey, null, ComClsid);
            }

            App.LogDebug("InstallCom: COM registration successful");
            WriteMenuTextToRegistry();
            return true;
        }
        catch (Exception ex)
        {
            App.LogDebug("InstallCom: failed: {0}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 卸载 COM 右键菜单，并清理旧的静态注册表条目。
    /// </summary>
    private static void UninstallCom()
    {
        try
        {
            // 1. 删除上下文菜单处理程序注册
            foreach (var target in new[] { "*", "Directory", @"Directory\Background" })
            {
                DeleteRegistryKey($@"Software\Classes\{target}\shellex\ContextMenuHandlers\{ComHandlerKey}");
            }

            // 2. 删除 CLSID 注册及 ProgId 反向查找
            DeleteRegistryKey($@"Software\Classes\CLSID\{ComClsid}");
            DeleteRegistryKey($@"Software\Classes\{ComProgId}");

            App.LogDebug("UninstallCom: COM registration cleaned up");
        }
        catch (Exception ex)
        {
            App.LogDebug("UninstallCom: failed: {0}", ex.Message);
        }
    }

    #endregion

    #region 层叠子菜单模式 (ExtendedSubCommandsKey)

    private static void InstallCascade(AppSettings s, string exePath)
    {
        // 每个目标类使用独立的子命令 key，因为 %1 / %V 不同
        InstallCascadeFor("*",                  "File",      s, exePath, argVar: "%1", includeExtract: true);
        InstallCascadeFor("Directory",           "Directory", s, exePath, argVar: "%1", includeExtract: false);
        InstallCascadeFor(@"Directory\Background", "Background", s, exePath, argVar: "%V", includeExtract: false);
    }

    private static void InstallCascadeFor(string target, string subKeySuffix, AppSettings s, string exePath, string argVar, bool includeExtract)
    {
        // 顶级层叠入口：MUIVerb + ExtendedSubCommandsKey
        var entryPath = $@"Software\Classes\{target}\shell\{CascadeRoot}";
        SetRegistryValue(entryPath, "MUIVerb", L.T(L.App_MantisZipTitle));

        var subCommandsPath = $@"{CascadeRoot}\{subKeySuffix}";
        SetRegistryValue(entryPath, "ExtendedSubCommandsKey", subCommandsPath);

        if (s.ShowMenuIcons)
            SetRegistryValue(entryPath, "Icon", $"""{exePath},0""");

        // 子命令：{CascadeRoot}\{suffix}\shell\{order_name}\command
        // 注：分隔线使用独立的 separator verb（CommandFlags=8），
        // 不直接设置在真实动词上，避免部分 Windows 版本上
        // ECF_SEPARATORBEFORE 导致动词本身不显示。
        var shellPath = $@"Software\Classes\{subCommandsPath}\shell";
        int order = 0;

        // 1. 用MantisZip打开（所有文件均可，由应用层处理非压缩包）
        if (s.EnableOpenMenu)
        {
            order++;
            var verb = $"{order:D2}_open";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, OpenDisplay);
            if (s.ShowMenuIcons)
            {
                string? icon = GetMenuIconPath(verb);
                if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
            }
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --open ""{argVar}""");
        }

        // 解压相关动词（所有文件均可，跟压缩一样）
        if (includeExtract)
        {
            bool hasExtract = s.EnableExtractHereMenu || s.EnableSmartExtractMenu || s.EnableExtractToNamedMenu || s.EnableExtractToMenu;
            if (hasExtract)
            {
                order++;
                InstallSeparator(shellPath, order);
            }

            // 2. 用MantisZip解压到此处
            if (s.EnableExtractHereMenu)
            {
                order++;
                var verb = $"{order:D2}_extracthere";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractHereDisplay);
                if (s.ShowMenuIcons)
                {
                    string? icon = GetMenuIconPath(verb);
                    if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
                }
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract-here ""{argVar}""");
            }

            // 2.5. 智能解压到此处 (Smart Extract)
            if (s.EnableSmartExtractMenu)
            {
                order++;
                var verb = $"{order:D2}_smartextract";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractSmartDisplay);
                if (s.ShowMenuIcons)
                {
                    string? icon = GetMenuIconPath(verb);
                    if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
                }
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract-smart ""{argVar}""");
            }

            // 3. 用MantisZip解压到（压缩包名）
            if (s.EnableExtractToNamedMenu)
            {
                order++;
                var verb = $"{order:D2}_extracttonamed";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractToNamedDisplay);
                if (s.ShowMenuIcons)
                {
                    string? icon = GetMenuIconPath(verb);
                    if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
                }
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract-to-name ""{argVar}""");
            }

            // 4. 用MantisZip解压到……
            if (s.EnableExtractToMenu)
            {
                order++;
                var verb = $"{order:D2}_extract";
                var verbPath = $@"{shellPath}\{verb}";
                SetRegistryValue(verbPath, null, ExtractDisplay);
                if (s.ShowMenuIcons)
                {
                    string? icon = GetMenuIconPath(verb);
                    if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
                }
                SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --extract ""{argVar}""");
            }

            // 分隔线：解压与压缩之间
            bool hasCompress = s.EnableCompressSeparate || s.EnableCompressCombined || s.EnableCompressMenu;
            if (hasExtract && hasCompress)
            {
                order++;
                InstallSeparator(shellPath, order);
            }
        }

        // 压缩相关动词
        // 5. 压缩到独立的（文件名）
        if (s.EnableCompressSeparate)
        {
            order++;
            var verb = $"{order:D2}_compressseparate";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, CompressSeparateDisplay);
            if (s.ShowMenuIcons)
            {
                string? icon = GetMenuIconPath(verb);
                if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
            }
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --compress-separate ""{argVar}""");
        }

        // 6. 压缩到（父目录名）
        if (s.EnableCompressCombined)
        {
            order++;
            var verb = $"{order:D2}_compresscombined";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, CompressCombinedDisplay);
            if (s.ShowMenuIcons)
            {
                string? icon = GetMenuIconPath(verb);
                if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
            }
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --compress-combined ""{argVar}""");
        }

        // 7. 用MantisZip压缩
        if (s.EnableCompressMenu)
        {
            order++;
            var verb = $"{order:D2}_compress";
            var verbPath = $@"{shellPath}\{verb}";
            SetRegistryValue(verbPath, null, CompressDisplay);
            if (s.ShowMenuIcons)
            {
                string? icon = GetMenuIconPath(verb);
                if (icon != null) SetRegistryValue(verbPath, "Icon", icon);
            }
            SetRegistryValue($@"{verbPath}\command", null, $@"""{exePath}"" --compress ""{argVar}""");
        }
    }

    #endregion

    /// <summary>在指定位置添加一条菜单分隔线。</summary>
    private static void InstallSeparator(string shellPath, int order)
    {
        var verb = $"{order:D2}_sep";
        SetRegistryDword($@"{shellPath}\{verb}", "CommandFlags", 8);
    }

    /// <summary>
    /// Write localized ShellExt menu text to registry under HKCU\Software\MantisZip\ContextMenu.
    /// Called during COM install so ShellExt reads the current language's strings.
    /// </summary>
    private static void WriteMenuTextToRegistry()
    {
        var regPath = @"Software\MantisZip\ContextMenu";
        SetRegistryValue(regPath, "TextOpen", L.T(L.ShellExt_Open));
        SetRegistryValue(regPath, "TextExtractHereSingle", L.T(L.ShellExt_ExtractHereSingle));
        SetRegistryValue(regPath, "TextExtractHereMulti", L.T(L.ShellExt_ExtractHereMulti));
        SetRegistryValue(regPath, "TextSmartExtractSingle", L.T(L.ShellExt_SmartExtractSingle));
        SetRegistryValue(regPath, "TextSmartExtractMulti", L.T(L.ShellExt_SmartExtractMulti));
        SetRegistryValue(regPath, "TextExtractToNamed", L.T(L.ShellExt_ExtractToNamed));
        SetRegistryValue(regPath, "TextExtractTo", L.T(L.ShellExt_ExtractTo));
        SetRegistryValue(regPath, "TextCompress", L.T(L.ShellExt_Compress));
        App.LogDebug("WriteMenuTextToRegistry: wrote ShellExt menu text to registry");
    }

    /// <summary>
    /// 返回菜单动词对应的图标文件绝对路径（与 COM 动态菜单一致）。
    /// 动词名形如 "01_open"、"02_extracthere"，自动忽略前缀匹配。
    /// 图标文件位于输出目录的 Resources\MenuIcons\ 下。
    /// 如果图标文件不存在，返回 null。
    /// </summary>
    private static string? GetMenuIconPath(string verb)
    {
        // Strip order prefix ("01_open" → "open")
        int underscoreIdx = verb.IndexOf('_');
        string key = underscoreIdx >= 0 ? verb.Substring(underscoreIdx + 1) : verb;

        var iconName = key.ToLowerInvariant() switch
        {
            "open" => "Open.ico",
            "extracthere" => "ExtractHere.ico",
            "smartextract" => "ExtractSmart.ico",
            "extracttonamed" => "ExtractToNamed.ico",
            "extract" => "ExtractTo.ico",
            "compressseparate" => "CompressSeparate.ico",
            "compresscombined" => "CompressCombined.ico",
            "compress" => "CompressDialog.ico",
            _ => null,
        };
        if (iconName == null) return null;

        var baseDir = Path.GetDirectoryName(GetExePath());
        if (baseDir == null) return null;

        var iconPath = Path.Combine(baseDir, "Resources", "MenuIcons", iconName);
        return File.Exists(iconPath) ? iconPath : null;
    }
}
