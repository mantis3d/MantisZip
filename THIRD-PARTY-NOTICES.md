# THIRD-PARTY-NOTICES — 第三方版权与许可声明

本文件汇总 MantisZip 在分发或艺术创作中使用到的第三方组件的版权与许可信息。各项完整法律条款以对应项目发布的官方原文为准，本文档仅作汇总说明。本项目自身代码依照 [MIT License](LICENSE) 许可。

---

## 1. 7z.dll（7-Zip 原生压缩引擎）

- **产品 / 作者**：7-Zip，Copyright (C) Igor Pavlov
- **来源**：<https://www.7-zip.org/>
- **许可证**：GNU LGPL 2.1（<https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html>，另见 7-Zip 官方分发包内 `License.txt`）
- **分发方式**：随 MantisZip 应用**动态链接**分发。依照 LGPL 要求，分发时应附带该库的许可文本；7z.dll 及其源代码可自上方链接获取。
- **用途**：7z / RAR / ISO 格式的解析与解压（经 [SharpSevenZip](https://github.com/sevenzipsharp/SevenZipSharp) 绑定调用）。

---

## 2. Fira Code 字体（栅格化使用）

- **项目 / 作者**：Fira Code，Copyright (C) 2015–2023 The Fira Code Project Authors（<https://github.com/tonsky/FiraCode>）
- **许可证**：[SIL Open Font License 1.1](https://openfontlicense.org/)
- **使用方式**：文件类型图标（`zip/gz/tgz/tar/rar/iso/7z` 共 7 个 `.ico`）的字形以 Fira Code 绘制，制作过程经栅格化为 PNG/ICO 后嵌入成品图标。
- **许可说明**：栅格化产物属于「使用字体创建的内容」（created using the font software），依据 OFL 1.1 第 3 条与官方 FAQ，不作为字体程序重新分发，不在 OFL 约束范围内；本仓库亦**不分发 Fira Code 字体文件本身**。图标成品沿用本项目 MIT 许可。
- **备注**：若日后改为随应用分发字体文件（如字体预览内置引擎），需同时附带 OFL 1.1 许可证全文及上述版权声明。

---

*最后更新：2026-10-20*
