# 关于 MantisZip

> **轻量级全功能 Windows 压缩/解压软件**

## 📌 项目简介

MantisZip 是一款免费开源的 Windows 压缩/解压工具，专注于提供流畅、完整的压缩包管理体验。支持预览、密码管理、Shell 集成等实用功能。

## ✨ 核心特性

### 📂 格式支持

| 格式 | 压缩 | 解压 | 加密 |
|------|:----:|:----:|:----:|
| ZIP | ✅ | ✅ | ✅ AES-256 |
| 7z | ✅ | ✅ | ✅ |
| TAR | ✅ | ✅ | ❌ |
| GZ / TGZ | ✅ | ✅ | ❌ |
| RAR | ❌ | ✅ | ✅ |

### 🔐 智能密码管理

- 保存常用密码，支持 **glob 模式**（`*.zip`）和 **正则表达式** 匹配
- 打开压缩包时**自动匹配**已保存密码，无需手动输入
- **QuickVerify** 技术：读 1 字节即可验证密码，不等完整解压
- 密码匹配成功后进度条窗口显示密码文本，支持一键复制

### 👁 文件预览

支持在压缩包内直接预览文件内容，无需解压：

- **图片** — JPG / PNG / GIF / BMP / WebP / ICO
- **文本** — TXT / LOG / 代码文件（自动检测编码）
- **HTML** — 网页文件直接渲染
- **Markdown** — 渲染为带样式的 HTML

### 🖱 Shell 集成

- **右键菜单**：层叠/独立双模式，支持压缩、解压、快速压缩、打开
- **文件关联**：双击 .zip/.7z 等文件自动用 MantisZip 打开
- **CLI 命令行**：支持 `--compress`、`--extract`、`--open` 等参数

### 🚀 更多功能

- 拖拽提取到文件资源管理器
- 解压冲突处理（覆盖 / 重命名 / 跳过 / 询问）
- 暂停/继续解压
- 分卷压缩
- 压缩包完整性测试
- 预览大小上限设置

## 🏗 技术架构

```
MantisZip
├── MantisZip.Core      — 核心业务逻辑
│   ├── Abstractions     — 接口与数据模型
│   ├── Engines          — ZipEngine / SevenZipEngine / TarGzEngine
│   └── Utils            — 密码管理 / 提取 / 冲突处理
└── MantisZip.UI         — WPF 桌面应用
    ├── MainWindow       — 主窗口
    ├── ProgressWindow   — 进度条 + 密码匹配区 + 暂停
    └── ShellIntegration — 右键菜单 + 文件关联
```

## 🤖 关于 Sisyphus

Sisyphus 是一个 AI Agent 系统，由 OhMyOpenCode 提供支持。

正如西西弗斯每天将巨石推向山顶，Sisyphus Agent 也在不断地迭代、优化代码——每次提交都让项目更接近完美。不写"AI 废料"，只产出有工程水准的代码。

## 📄 许可证

本项目基于 **MIT 许可证** 开源。

```
MIT License

Copyright (c) 2026 MantisZip

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction...
```

---

*MantisZip — 当解压成为一种享受* 😊

---

## 🧪 Markdown 渲染测试

以下内容用于测试 Markdown 预览新扩展的支持情况。

### ~~删除线测试~~

- ~~这行文字应该显示为删除线~~
- 这行文字没有删除线
- 混合 ~~删除线~~ 与普通文字

### ~下标~ 与 ^上标^ 测试

- H~2~O（水的化学式）
- X^2^ + Y^2^ = Z^2^（勾股定理）
- 混合 ~下标~ 和 ^上标^ 在同一行

### ++插入文字++ 与 ==标记文字== 测试

- ++这行文字应该显示为插入（下划线）效果++
- ==这行文字应该显示为高亮标记效果==
- 普通文字 ==标记== ++插入++ 混合

### 任务列表测试

- [ ] 未完成的任务
- [x] 已完成的任务
- [ ] 另一个未完成的任务
- [x] 另一个已完成的任务
- [ ] ~~已被删除的未完成任务~~

嵌套列表中的任务：

- 一级任务组
  - [x] 子任务 A
  - [ ] 子任务 B
    - [x] 孙子任务 i
    - [ ] 孙子任务 ii

### :表情: 快捷代码测试

- `:smile:` → :smile:
- `:heart:` → :heart:
- `:+1:` → :+1:
- `:rocket:` → :rocket:
- `:warning:` → :warning:
- `:fire:` → :fire:
- `:100:` → :100:
- `:tada:` → :tada:

### 标题锚点测试

点击以下链接应该跳转到对应章节：

- [跳转到项目简介](#-项目简介)
- [跳转到格式支持表格](#-格式支持)
- [跳转到删除线测试](#删除线测试)
- [跳转到表情快捷代码测试](#表情-快捷代码测试)

### 综合示例

这是一个包含 ~多~ *种* **格式** ~~混~~ ++合++ ==使== ^用^ 的段落，展示所有扩展同时生效的效果。

任务进度：

- [x] ~~删除线任务~~ — 已完成并标记为删除
- [x] 表格渲染 :white_check_mark:
- [ ] ~~上标下标~~ + :表情: + ==标记== — 待完成 :soon:

> **提示**：如果以上所有内容都正确渲染，说明 Markdown 预览扩展工作正常！:ok_hand:
