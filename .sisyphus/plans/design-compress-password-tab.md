# 压缩窗口密码 Tab 设计

> 将 CompressSettingsWindow 的密码输入从通用 Tab 内联移至独立 Tab，并与 PasswordManager 深度集成。

---

## 一、Tab 结构变更

```
CompressSettingsWindow (TabControl)
├── 通用 (General)        ← 原样，去掉加密 GroupBox
├── 加密 (Password)       ← 新增
└── 注释 (Comment)        ← 原样
```

加密 Tab 只在 ZIP/7z 格式时启用，非 ZIP/7z 时整个 Tab 灰化（`IsEnabled=false`），底部显示提示。

---

## 二、加密 Tab 布局

### 顶层结构

```
┌─ ◉ 从密码库选择 ──────────────────────────┐  ← 激活态 Enabled
│  (详情见 2.1)                              │
└────────────────────────────────────────────┘

┌─ ○ 输入新密码 ────────────────────────────┐  ← 禁用态 Opacity 0.3
│  (详情见 2.2)                              │
└────────────────────────────────────────────┘

──────────────────────────────────── 分割线

┌─ 共享区域 ────────────────────────────────┐
│  (详情见 2.3)                              │
└────────────────────────────────────────────┘
```

### 2.1 从密码库选择面板

```
┌─ ◉ 从密码库选择 ──────────────────────────┐
│  [搜索...描述或规则...      ]              │
│  ┌─────────────────────────────────────┐  │
│  │  工作邮箱                            │  │  ← 两行一个条目
│  │  work_*, *@company.com              │  │
│  │─────────────────────────────────────│  │
│  │  个人文档         ◀ 选中高亮         │  │
│  │  私人*                               │  │
│  │─────────────────────────────────────│  │
│  │  数据库备份                          │  │
│  │  db_*, *.bak                         │  │
│  └─────────────────────────────────────┘  │
│  已选择: 个人文档                          │
└────────────────────────────────────────────┘

- 列表数据源: PasswordManager.Instance.GetAllPasswords()
- 排序: LastUsed 降序（最近使用排最前），未使用的按 CreatedAt 降序
- 点击条目: 记录 _selectedEntryId，底部显示"已选择: {描述}"
- 搜索框: 实时过滤匹配 Description 或 Patterns（任一包含搜索词即显示）
- 条目只显示描述+规则，不显示密码（任何形式都不显示）
```

### 2.2 输入新密码面板

```
┌─ ○ 输入新密码 ────────────────────────────┐
│  密码:  [••••••••]               [👁]    │
│  确认:  [••••••••]                       │
│  强度:  ████████░░ 强                    │
└────────────────────────────────────────────┘

- 👁 按钮切换 PasswordBox/TextBox 的掩码/明文
- 强度指示器: 基于密码长度+字符类型（纯数字/字母/混合/含特殊字符）判断
  - < 6 位: 弱
  - 6-10 位纯数字: 弱
  - 6-10 位混合: 中
  - ≥ 10 位混合: 强
- 密码框内容变化时自动清空 _selectedEntryId（取消密码库选中态）
- 确认框仅在"输入新密码"激活时校验
```

### 2.3 共享区域

```
──────────────────────────────────── 分割线

☑ 保存到密码库 / ☐ 更新匹配规则          ← 文案随 RadioButton 变化
    描述: [个人文档密码                     ]
    ☑ 自动规则
    规则 (一行一个):
    [project-2024*.zip                    ]
```

| ◉ 从密码库选择 | ◉ 输入新密码 |
|---|---|
| 勾: `☐ 更新匹配规则` | 勾: `☐ 保存到密码库` |
| 描述: 选中条目的描述（只读 TextBox） | 描述: 可编辑，默认空 |
| 规则: 可编辑，初始从条目已有的 Patterns 加载 | 规则: 可编辑，初始自动生成 |
| 勾选后压缩成功 → 将规则追加到条目的 Patterns | 勾选后压缩成功 → 新增条目 |

---

## 三、自动规则生成逻辑

### 3.1 按模式

| 模式 | 规则行数 | 生成示例 |
|------|---------|---------|
| **Manual** | 1 行 | `project-2024*.zip`（输出文件名主体 + `*.{format}`） |
| **Separate** | 每源文件 1 行 | `report*.zip`、`photo*.zip`、`invoice*.zip` |
| **Combined** | 1 行 | `myapp*.zip`（公共目录名 + `*.{format}`） |

### 3.2 自动规则开关

| 状态 | 规则 TextBox | 行为 |
|------|:---:|------|
| ☑ 自动规则 | 禁用（ReadOnly）+ 内容自动更新 | 每次输出路径/格式/模式变化时重新计算覆盖 |
| ☐ 自动规则 | 可用 | 用户可以手动编辑，系统不干预 |
| 再 ☑ 回去 | 禁用 + 内容重新覆盖 | 上次手动编辑内容丢弃 |

### 3.3 触发重新计算的事件

- FormatComboBox 切换
- OutputMode RadioButton 切换
- Manual 模式下 OutputPathTextBox 变化
- Separate/Combined 模式下 SourceListBox 变化（增减源文件）

### 3.4 规则去重（保存时）

无论追加还是新增，每条规则在保存前检查是否已存在（字符串精确匹配），已存在则跳过。

---

## 四、密码取值逻辑

```csharp
private string? GetActivePassword()
{
    if (_isUsingLibrary)           // ◉ 从密码库选择
        return _selectedEntry?.Password;
    else                           // ○ 输入新密码
        return PasswordBox.Password;
}
```

压缩验证时：
- 库模式：确保 `_selectedEntry != null`
- 新密码模式：确保 `PasswordBox.Password` 非空且与 `ConfirmPasswordBox.Password` 一致
- 任一模式：`EncryptCheckBox` 未勾选时跳过整个验证

---

## 五、状态管理规则

| 操作 | 面板 1（密码库） | 面板 2（新密码） | 共享区 |
|------|:---:|:---:|:---:|
| 点击密码库条目 | 更新 _selectedEntryId | 清空密码框、清空确认框 | 描述=条目描述（只读），勾改为"更新匹配规则" |
| 修改密码框内容 | 清空 _selectedEntryId | 保留内容 | 描述清空（可编辑），勾改为"保存到密码库" |
| 切换 RadioButton | 保留选中态/搜索态 | 保留输入内容 | 共享区内容不变 |

---

## 六、压缩保存流程

```
CompressButton_Click
  → 验证（加密勾选 + 密码有效）
  → 压缩执行
  → 成功后：
      if 勾选了"保存到密码库" 且 输入新密码模式:
          PasswordManager.AddPassword(password, desc, rules)
      if 勾选了"更新匹配规则" 且 库模式:
          foreach rule in rules: 去重追加到 selectedEntry.Patterns
          PasswordManager.MarkUsed(selectedEntry.Id)
          PasswordManager.Save()
```

---

## 七、涉及改动文件

| 文件 | 改动 |
|------|------|
| `CompressSettingsWindow.xaml` | 通用 Tab 删除加密 GroupBox；新增加密 Tab XAML（两面板 + 共享区） |
| `CompressSettingsWindow.xaml.cs` | 新增字段/事件/GetActivePassword()；修改所有引用 PasswordBox 的地方；修改 EncryptCheckBox 为控制整个 Tab |
| `Localization/*.json` | 新增约 10 条字符串 |
| `App.xaml.cs` / `App.Cli.cs` | 不涉及（保留旧有提取侧密码逻辑不变） |
| `PasswordManager.cs` / `PasswordManagerWindow.*` | 不涉及（Core 层不变） |

---

## 八、不做的事情

- 重复密码检测（以后再说）
- 强度指示器的配色/动画（纯文本或简单进度条）
- 密码库管理功能的扩展（仍然是 PasswordManagerWindow）
- CLI 路径的密码库集成（--compress-quick 暂不改）
