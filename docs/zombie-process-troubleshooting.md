# 僵尸进程排查指南（点击关闭后界面消失但后台仍运行）

> 适用版本：Avalonia 版（MantisZip.UI.Avalonia），2026-08-24 加入诊断埋点之后的构建。
> 现象：点击关闭后所有界面消失，但任务管理器中 MantisZip 进程仍然存在（无任何可见窗口）。
> 高发场景：Explorer 右键菜单触发的压缩/解压操作之后。

---

## 一、复发时你要做的三步

### 第 1 步：保持现场，不要结束进程

僵尸进程本身就是证据。**不要**在任务管理器里结束它——一旦杀掉，窗口残留状态就丢失了。

如果已经不小心结束了也没关系，日志是实时落盘的，之前的记录仍在。

### 第 2 步：导出诊断日志

```powershell
Get-Content "$env:LOCALAPPDATA%\MantisZip\lifecycle.log" -Tail 50
```

把输出发给开发者（或贴到 issue）。

### 第 3 步（可选）：补充任务管理器信息

打开任务管理器 → 详细信息页签：

- 残留的 `MantisZip.UI.Avalonia.exe` 进程有几个？
- 各自的启动时间是什么时候？

这能区分「单进程泄漏」和「多实例并存」两种情况。

---

## 二、日志文件说明

| 项目 | 说明 |
|------|------|
| 位置 | `%LOCALAPPDATA%\MantisZip\lifecycle.log` |
| 开关 | **无需开启任何设置**，无条件写入 |
| 隐私 | 文件路径已自动脱敏（显示为 `[PATH_1]`、`[FILE_1]` 等占位符） |
| 大小 | 超过 5MB 自动轮转为 `lifecycle.log.<时间戳>.bak` |
| 影响性能 | 无。仅在窗口开/关和异常时写一行，空闲时不产生任何输出 |

---

## 三、关键日志条目解读

定位问题主要看这几类条目：

| 条目 | 含义 |
|------|------|
| `── app start pid=… mode=… args=[…]` | 一次会话的开始。`mode=OnLastWindowClose` 为正常主窗口启动；`mode=OnExplicitShutdown` 为右键菜单/CLI 流程启动 |
| `WINDOW OPEN …` / `WINDOW CLOSE …` | 每个窗口的打开与关闭（**包括不可见窗口**）。僵尸发生前最后一条 `OPEN` 而没有对应 `CLOSE` 的窗口，就是头号嫌疑 |
| `ZOMBIE STATE ENTERED after …s mode=… windows=[…]` | **核心条目**。无可见窗口但进程存活持续 6 秒后触发，`windows=[…]` 里列出的就是当时残留的所有窗口（类型/可见性/标题） |
| `ZOMBIE STILL ALIVE ticks=…` | 僵尸状态持续中，每约 16 秒重申一次现场快照 |
| `ShutdownRequested cancel=False …` | 一次显式退出请求。如果僵尸发生时**没有**这条记录，说明某条流程根本没走到退出代码 |
| `UIThread EXCEPTION …` | UI 线程未处理异常（如拖拽处理器抛出）。附完整调用栈 |
| `ConflictDialog OPEN / btn=… / CLOSING without button` | 解压冲突弹窗的决策链取证 |

### 判断示例

```
[11:40:32] ── app start pid=41256 mode=OnExplicitShutdown args=[--extract-to-name …]
[11:40:34] WINDOW OPEN  ProgressWindow#4CF0D3 vis=True …
[11:40:34] WINDOW OPEN  ConflictDialog#1197561 vis=True …
[11:40:52] WINDOW CLOSE ConflictDialog#612F8 …        ← 弹窗已关
[11:41:10] ZOMBIE STATE ENTERED … windows=[ProgressWindow#4CF0D3 vis=False …]
                                                      ← 残留的是进度窗（不可见）→ 查进度窗关闭链路
```

---

## 四、这套机制是怎么工作的（背景）

应用退出分两类路径：

- 正常启动：默认 `OnLastWindowClose`，所有窗口关闭即自动退出；
- 右键菜单/CLI 流程（压缩、解压等约 10 条）：显式设为 `OnExplicitShutdown`，必须代码调用 `desktop.Shutdown()` 才退出——任何一个分支漏调或被卡住，就会出现本问题。

诊断器（`src/MantisZip.UI.Avalonia/Services/LifetimeDiagnostics.cs`）以纯观察方式运行：每 2 秒对比一次窗口列表差分并检测僵尸状态，同时记录 UI 线程异常与冲突弹窗决策。**不改变任何行为**。

相关改动位置（将来定位根因、修复后可整体移除）：

- `src/MantisZip.UI.Avalonia/Services/LifetimeDiagnostics.cs` —— 诊断器本体
- `src/MantisZip.UI.Avalonia/App.axaml.cs` —— `OnFrameworkInitializationCompleted` 中一行接线
- `src/MantisZip.UI.Avalonia/Dialogs/ConflictDialog.axaml.cs` —— 冲突弹窗各出口的取证日志

---

## 五、常见疑问

**Q：日志会不会记录我的文件路径？**
不会原样记录。路径统一脱敏为 `[PATH_1]`、`[FILE_1].zip` 之类的占位符，仅保留扩展名用于判断格式。

**Q：平时开着会有影响吗？**
没有。空闲时定时器只做内存比对，不写磁盘；只有窗口开关、异常、僵尸状态才会落盘。

**Q：问题修好之后这些日志怎么办？**
确认修复后可删除 `%LOCALAPPDATA%\MantisZip\lifecycle.log`，并随修复提交一并移除上文第三节列出的诊断代码。
