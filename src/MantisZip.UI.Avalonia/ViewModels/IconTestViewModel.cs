using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 图标测试窗口的 ViewModel，列出程序中所有图标的使用情况。
/// 用于 emoji→PathIcon 替换计划的测试验证与补充。
/// </summary>
public partial class IconTestViewModel : ObservableObject
{
    public ObservableCollection<IconTestItem> Icons { get; } = [];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _convertedCount;

    [ObservableProperty]
    private int _pendingCount;

    public IconTestViewModel()
    {
        LoadAllIcons();
    }

    partial void OnFilterTextChanged(string value)
    {
        // 筛选由视图层的 CollectionView 处理
    }

    /// <summary>
    /// 获取所有图标数据，按分类排序。
    /// </summary>
    public List<IconTestItem> GetFilteredIcons()
    {
        var all = Icons.ToList();

        if (string.IsNullOrWhiteSpace(FilterText))
            return all;

        var filter = FilterText.Trim().ToLowerInvariant();
        return all.Where(i =>
            (i.SemanticName?.ToLowerInvariant().Contains(filter) ?? false) ||
            (i.ResourceKey?.ToLowerInvariant().Contains(filter) ?? false) ||
            (i.EmojiChar?.Contains(filter) ?? false) ||
            (i.Category?.ToLowerInvariant().Contains(filter) ?? false) ||
            (i.Location?.ToLowerInvariant().Contains(filter) ?? false) ||
            (i.Notes?.ToLowerInvariant().Contains(filter) ?? false)
        ).ToList();
    }

    private void LoadAllIcons()
    {
        // ═══════════════════════════════════════════════════════════
        // 1. 菜单图标（MainWindow.axaml）— 已替换为 PathIcon
        // ═══════════════════════════════════════════════════════════
        Add("菜单", "打开/浏览", null, "IconFolder", IconStatus.Converted,
            "Views/MainWindow.axaml:35", "菜单栏「文件→打开」");
        Add("菜单", "关闭/退出", null, "IconSignOut", IconStatus.Converted,
            "Views/MainWindow.axaml:44,97", "菜单栏「文件→关闭」「文件→退出」");
        Add("菜单", "最近文件", null, "IconHistory", IconStatus.Converted,
            "Views/MainWindow.axaml:58", "菜单栏「文件→最近文件」");
        Add("菜单", "清空最近", null, "IconDelete", IconStatus.Converted,
            "Views/MainWindow.axaml:70", "菜单栏「文件→清空最近文件」");
        Add("菜单", "刷新", null, "IconRefresh", IconStatus.Converted,
            "Views/MainWindow.axaml:79", "菜单栏「文件→刷新」");
        Add("菜单", "设置", null, "IconSettings", IconStatus.Converted,
            "Views/MainWindow.axaml:88", "菜单栏「文件→设置」");
        Add("菜单", "收藏夹管理", null, "IconStar", IconStatus.Converted,
            "Views/MainWindow.axaml:107", "菜单栏「文件→收藏夹→管理」");
        Add("菜单", "解压到……", null, "IconExport", IconStatus.Converted,
            "Views/MainWindow.axaml:119", "菜单栏「编辑→解压到……」");
        Add("密码", "导入密码", null, "IconImport", IconStatus.Converted,
            "Dialogs/PasswordManagerWindow.axaml:59", "密码管理器工具栏「导入」");
        Add("菜单", "原地解压", null, "IconPin", IconStatus.Converted,
            "Views/MainWindow.axaml:128", "菜单栏「编辑→原地解压」");
        Add("菜单", "解压到命名目录", null, "IconFolder", IconStatus.Converted,
            "Views/MainWindow.axaml:137", "菜单栏「编辑→解压到压缩包名」");
        Add("菜单", "智能解压", null, "IconWand", IconStatus.Converted,
            "Views/MainWindow.axaml:146", "菜单栏「编辑→智能解压」");
        Add("菜单", "添加文件", null, "IconAdd", IconStatus.Converted,
            "Views/MainWindow.axaml:156", "菜单栏「编辑→添加文件」");
        Add("菜单", "删除文件", null, "IconDismiss", IconStatus.Converted,
            "Views/MainWindow.axaml:165", "菜单栏「编辑→删除文件」");
        Add("菜单", "测试压缩包", null, "IconCheckmark", IconStatus.Converted,
            "Views/MainWindow.axaml:175", "菜单栏「编辑→测试压缩包」");
        Add("菜单", "压缩包注释", null, "IconChat", IconStatus.Converted,
            "Views/MainWindow.axaml:185", "菜单栏「编辑→压缩包注释」");
        Add("菜单", "新建压缩包", null, "IconNewFile", IconStatus.Converted,
            "Views/MainWindow.axaml:194", "菜单栏「编辑→新建压缩包」");
        Add("菜单", "压缩", null, "IconDownload", IconStatus.Converted,
            "Views/MainWindow.axaml:202", "菜单栏「编辑→压缩」");
        Add("菜单", "切换主题", null, "IconMoon", IconStatus.Converted,
            "Views/MainWindow.axaml:215", "菜单栏「查看→切换主题」");
        Add("菜单", "面板方向", null, "IconOrientation", IconStatus.Converted,
            "Views/MainWindow.axaml:247", "菜单栏「查看→信息面板方向」");
        Add("菜单", "进度条/分隔线", null, "IconInfo", IconStatus.Converted,
            "Views/MainWindow.axaml:257,266", "菜单栏「查看→进度条」「查看→分隔线」");
        Add("菜单", "密码管理器", null, "IconKey", IconStatus.Converted,
            "Views/MainWindow.axaml:289", "菜单栏「工具→密码管理器」");
        Add("菜单", "赞助", null, "IconHeart", IconStatus.Converted,
            "Views/MainWindow.axaml:297", "菜单栏「工具→赞助」");
        Add("菜单", "密码帮助", null, "IconQuestion", IconStatus.Converted,
            "Views/MainWindow.axaml:314", "菜单栏「工具→测试→密码帮助」");
        Add("菜单", "注释对话框", null, "IconChat", IconStatus.Converted,
            "Views/MainWindow.axaml:323", "菜单栏「工具→测试→注释对话框」");
        Add("菜单", "密码编辑", null, "IconEdit", IconStatus.Converted,
            "Views/MainWindow.axaml:331", "菜单栏「工具→测试→密码编辑」");
        Add("菜单", "密码对话框", null, "IconShieldLock", IconStatus.Converted,
            "Views/MainWindow.axaml:339", "菜单栏「工具→测试→密码对话框」");
        Add("菜单", "进度窗口", null, "IconTimer", IconStatus.Converted,
            "Views/MainWindow.axaml:348", "菜单栏「工具→测试→进度窗口」");

        // ═══════════════════════════════════════════════════════════
        // 2. 菜单图标（已替换）
        // ═══════════════════════════════════════════════════════════
        Add("菜单", "收藏夹（标题）", null, null, IconStatus.Converted,
            "Views/MainWindow.axaml:103", "菜单栏「文件→收藏夹」，emoji 已移除");

        // ═══════════════════════════════════════════════════════════
        // 3. 设置窗口图标（SettingsWindow.axaml）— 已替换
        // ═══════════════════════════════════════════════════════════
        Add("设置", "压缩设置", null, "IconCompress", IconStatus.Converted,
            "Views/SettingsWindow.axaml:26", "Tab 标签图标");
        Add("设置", "解压设置", null, "IconFolder", IconStatus.Converted,
            "Views/SettingsWindow.axaml:208", "Tab 标签图标");
        Add("设置", "外观设置", null, "IconPaintBrush", IconStatus.Converted,
            "Views/SettingsWindow.axaml:267", "Tab 标签图标");
        Add("设置", "预览设置", null, "IconEye", IconStatus.Converted,
            "Views/SettingsWindow.axaml:341", "Tab 标签图标");
        Add("设置", "密码设置", null, "IconShieldLock", IconStatus.Converted,
            "Views/SettingsWindow.axaml:602", "Tab 标签图标");
        Add("设置", "语言设置", null, "IconGlobe", IconStatus.Converted,
            "Views/SettingsWindow.axaml:627", "Tab 标签图标");
        Add("设置", "上下文菜单", null, "IconCopy", IconStatus.Converted,
            "Views/SettingsWindow.axaml:661", "Tab 标签图标");
        Add("设置", "文件关联", null, "IconLink", IconStatus.Converted,
            "Views/SettingsWindow.axaml:750", "Tab 标签图标");
        Add("设置", "调试设置", null, "IconBug", IconStatus.Converted,
            "Views/SettingsWindow.axaml:806", "Tab 标签图标");
        Add("设置", "高级设置", null, "IconWrench", IconStatus.Converted,
            "Views/SettingsWindow.axaml:867", "Tab 标签图标");

        // ═══════════════════════════════════════════════════════════
        // 4. 预览面板图标（PreviewPanel.axaml）— 已替换
        // ═══════════════════════════════════════════════════════════
        Add("预览", "缩小", null, "IconSubtract", IconStatus.Converted,
            "Views/PreviewPanel.axaml:46", "ZoomOut 按钮 PathIcon");
        Add("预览", "放大", null, "IconAdd", IconStatus.Converted,
            "Views/PreviewPanel.axaml:50", "ZoomIn 按钮 PathIcon");
        Add("预览", "适应视口", null, "IconArrowFitIn", IconStatus.Converted,
            "Views/PreviewPanel.axaml:54", "ZoomFit 按钮 PathIcon");
        Add("预览", "减小字号", null, "IconFontDecrease", IconStatus.Converted,
            "Views/PreviewPanel.axaml:63", "文字预览缩小字号按钮 PathIcon");
        Add("预览", "增大字号", null, "IconFontIncrease", IconStatus.Converted,
            "Views/PreviewPanel.axaml:67", "文字预览增大字号按钮 PathIcon");
        Add("预览", "上一帧", null, "IconPrevious", IconStatus.Converted,
            "Views/PreviewPanel.axaml:76", "GIF 上一帧按钮 PathIcon");
        Add("预览", "播放/暂停", null, "IconPlay", IconStatus.Converted,
            "Views/PreviewPanel.axaml:80", "GIF 播放暂停按钮 PathIcon");
        Add("预览", "下一帧", null, "IconNext", IconStatus.Converted,
            "Views/PreviewPanel.axaml:84", "GIF 下一帧按钮 PathIcon");
        Add("预览", "透明网格", null, "IconGrid", IconStatus.Converted,
            "Views/PreviewPanel.axaml:85", "透明背景切换按钮 PathIcon");
        Add("预览", "压平 Alpha", null, "IconPaintBrush", IconStatus.Converted,
            "Views/PreviewPanel.axaml:90", "颜色预览按钮 PathIcon");

        // ═══════════════════════════════════════════════════════════
        // 5. 对话框图标
        // ═══════════════════════════════════════════════════════════
        Add("对话框", "覆盖（冲突）", null, "IconRefresh", IconStatus.Converted,
            "Dialogs/ConflictDialog.axaml:115, CompressConflictDialog.axaml:68",
            "文件冲突对话框覆盖按钮 PathIcon");
        Add("对话框", "取消（冲突）", null, "IconDismiss", IconStatus.Converted,
            "Dialogs/ConflictDialog.axaml:158, CompressConflictDialog.axaml:113",
            "文件冲突对话框取消按钮 PathIcon");
        Add("对话框", "重命名（冲突）", null, "IconEdit", IconStatus.Converted,
            "Dialogs/ConflictDialog.axaml:155, CompressConflictDialog.axaml:98",
            "文件冲突对话框重命名按钮 PathIcon");
        Add("对话框", "跳过（冲突）", null, "IconArrowRight", IconStatus.Converted,
            "Dialogs/ConflictDialog.axaml:168, CompressConflictDialog.axaml:112",
            "文件冲突对话框跳过按钮 PathIcon");
        Add("对话框", "显示密码", null, "IconEye", IconStatus.Converted,
            "Views/PasswordDialog.axaml:33", "密码输入框显示/隐藏按钮 PathIcon");
        Add("对话框", "隐藏密码", null, "IconEyeOff", IconStatus.Converted,
            "Dialogs/MatchedPasswordDialog.axaml:70", "匹配密码对话框显示切换 PathIcon");
        Add("对话框", "显示密码（匹配）", null, "IconEye", IconStatus.Converted,
            "Dialogs/MatchedPasswordDialog.axaml:69", "匹配密码对话框显示切换 PathIcon");
        Add("对话框", "复制完成", null, "IconCheckmark", IconStatus.Converted,
            "Dialogs/MatchedPasswordDialog.axaml.cs:85", "C# 动态设置复制成功确认 PathIcon");

        // ═══════════════════════════════════════════════════════════
        // 6. 文件列表/树形图标（C# 动态）— 已替换为 IconKey 属性
        // ═══════════════════════════════════════════════════════════
        Add("文件列表", "文件夹节点", null, "IconFolder", IconStatus.Converted,
            "Models/PreviewTreeNode.cs:51", "PreviewTreeNode.IconKey 返回 IconFolder");
        Add("文件列表", "空目录", null, "IconEmptyFolder", IconStatus.Converted,
            "Models/PreviewTreeNode.cs:91", "PreviewTreeNode.IconKey 对空目录返回 IconEmptyFolder");
        Add("文件列表", "文件节点", null, "IconDocument", IconStatus.Converted,
            "Models/PreviewTreeNode.cs:52", "PreviewTreeNode.IconKey 返回 IconDocument");
        Add("文件列表", "冲突文件", null, "IconWarning", IconStatus.Converted,
            "Models/PreviewTreeNode.cs:50", "PreviewTreeNode.IconKey 返回 IconWarning");
        Add("文件列表", "根目录", null, "IconFolder", IconStatus.Converted,
            "Services/ResultPreviewService.cs:30", "根节点自动通过 IconKey 获取 IconFolder");

        // ═══════════════════════════════════════════════════════════
        // 7. 搜索过滤栏图标（MainWindow.axaml — 已替换为 PathIcon）
        // ═══════════════════════════════════════════════════════════
        Add("过滤栏", "搜索", null, "IconSearch", IconStatus.Converted,
            "Views/MainWindow.axaml:651", "过滤栏搜索框左侧 PathIcon");
        Add("过滤栏", "排除", null, "IconProhibited", IconStatus.Converted,
            "Views/MainWindow.axaml:671", "过滤栏排除框左侧 PathIcon");

        // ═══════════════════════════════════════════════════════════
        // 8. 树形预览图标（ResultTreeView.axaml — 已替换为 PathIcon）
        // ═══════════════════════════════════════════════════════════
        Add("文件列表", "紧凑切换", null, "IconFolder", IconStatus.Converted,
            "Controls/ResultTreeView.axaml:23", "ToggleButton 内 PathIcon");
        Add("文件列表", "冲突警告", null, "IconWarning", IconStatus.Converted,
            "Controls/ResultTreeView.axaml:83", "已存在的文件旁 PathIcon 警告");

        // ═══════════════════════════════════════════════════════════
        // 9. 空状态图标（已替换）
        // ═══════════════════════════════════════════════════════════
        Add("空状态", "拖拽提示", null, "IconFolder", IconStatus.Converted,
            "Views/MainWindow.axaml:773", "无压缩包时居中大图标 Width=64");

        // ═══════════════════════════════════════════════════════════
        // 10. AppIcons.axaml 中已定义但暂未使用的图标（备查）
        // ═══════════════════════════════════════════════════════════
        Add("资源库", "保存", null, "IconSave", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "警告", null, "IconWarning", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "锁定", null, "IconLockClosed", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "解锁", null, "IconLockOpen", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "定位", null, "IconPin", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry（菜单中同功能用 IconPin, 此条为重复检查）");
        Add("资源库", "数据统计", null, "IconDataBarVertical", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "信息面板", null, "IconPanelRight", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "主页", null, "IconHome", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry（context-toolbars 计划预留）");
  Add("资源库", "上移", null, "IconArrowUp", IconStatus.Defined,
  "Resources/Icons/AppIcons.axaml", "已定义 Geometry（context-toolbars 计划预留）");
  Add("资源库", "下移", null, "IconArrowDown", IconStatus.Defined,
  "Resources/Icons/AppIcons.axaml", "已定义 Geometry（元数据面板预览字段行移动用）");
        Add("资源库", "左箭头", null, "IconChevronLeft", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry（context-toolbars 计划预留）");
        Add("资源库", "右箭头", null, "IconChevronRight", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry（context-toolbars 计划预留）");
        Add("资源库", "导航菜单", null, "IconNavigation", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry（context-toolbars 计划预留）");
        Add("资源库", "全选", null, "IconSelectAll", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry（context-toolbars 计划预留）");
        Add("资源库", "闪电", null, "IconLightning", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "箭头展开", null, "IconArrowExpand", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "箭头折叠", null, "IconArrowCollapse", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "位置", null, "IconLocation", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "筛选", null, "IconFilter", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "定位", null, "IconLocate", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "暂停", null, "IconPause", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "标尺", null, "IconRuler", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "日历", null, "IconCalendar", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "取色器", null, "IconEyedropper", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "已定义 Geometry，未在 UI 中使用");
        Add("资源库", "存档", null, "IconArchive", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "应用设置", null, "IconAppsSettings", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "存档箭头返回", null, "IconArchiveArrowBack", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "存档时钟", null, "IconArchiveClock", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "存档多个", null, "IconArchiveMultiple", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "存档设置", null, "IconArchiveSettings", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "自动适应内容", null, "IconArrowAutofitContent", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "自动适应宽度", null, "IconArrowAutofitWidth", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "自动适应宽度虚线", null, "IconArrowAutofitWidthDotted", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "自动适应高度", null, "IconArrowAutofitHeight", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "圆形箭头下", null, "IconArrowCircleDown", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "圆形箭头下上", null, "IconArrowCircleDownUp", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "圆形箭头左", null, "IconArrowCircleLeft", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "圆形箭头右", null, "IconArrowCircleRight", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "圆形箭头上", null, "IconArrowCircleUp", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "全部折叠", null, "IconArrowCollapseAll", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "箭头进入", null, "IconArrowEnter", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "箭头退出", null, "IconArrowExit", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "全部展开", null, "IconArrowExpandAll", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "箭头移动", null, "IconArrowMove", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "箭头向内移动", null, "IconArrowMoveInward", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "日历勾选", null, "IconCalendarCheckmark", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "Chevron 下上", null, "IconChevronDownUp", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "Chevron 上下", null, "IconChevronUpDown", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "文件夹箭头左", null, "IconFolderArrowLeft", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "文件夹添加", null, "IconFolderAdd", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "文件夹箭头右", null, "IconFolderArrowRight", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "文件夹提示", null, "IconFolderHint", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "文件夹多个", null, "IconFolderMultiple", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "文件夹打开", null, "IconFolderOpen", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "钥匙多个", null, "IconKeyMultiple", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "位置勾选", null, "IconLocationCheckmark", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "位置设置", null, "IconLocationSettings", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "锁定钥匙", null, "IconLockClosedKey", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");
        Add("资源库", "锁定多个", null, "IconLockMultiple", IconStatus.Defined,
            "Resources/Icons/AppIcons.axaml", "Fluent UI System Icon");

        // 更新统计
        TotalCount = Icons.Count;
        ConvertedCount = Icons.Count(i => i.Status == IconStatus.Converted);
        PendingCount = Icons.Count(i => i.Status == IconStatus.Pending);
    }

    private void Add(string category, string semanticName, string? emoji, string? resourceKey,
        IconStatus status, string location, string notes)
    {
        Icons.Add(new IconTestItem
        {
            Category = category,
            SemanticName = semanticName,
            EmojiChar = emoji,
            ResourceKey = resourceKey,
            Status = status,
            Location = location,
            Notes = notes
        });
    }
}
