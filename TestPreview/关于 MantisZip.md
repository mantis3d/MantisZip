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
