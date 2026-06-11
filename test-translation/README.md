# MyApp

一个轻量级的文件处理工具。

## 简介

MyApp 是一个用 Rust 编写的命令行工具，用于批量处理文本文件。它支持多种编码格式，并提供高效的并行处理能力。

## 安装

```bash
pip install myapp
```

## 使用方法

### 基本用法

```bash
myapp process input.txt
```

### 高级选项

```bash
myapp process --output result.txt --encoding utf-8 --threads 4
```

## 配置

MyApp 支持通过 `config.yaml` 配置文件自定义行为：

```yaml
output_dir: ./output
encoding: utf-8
threads: 4
verbose: true
```

## 贡献指南

欢迎提交 Pull Request。请确保代码通过测试。

## 许可证

MIT License

> 🌐 其他语言: [English](docs/README_en.md)
