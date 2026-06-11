> 🌐 Language: [中文](../README.md) | [English](README_en.md)

# MyApp

A lightweight file processing tool.

## Introduction

MyApp is a command-line tool written in Rust for batch processing text files. It supports multiple encoding formats and provides efficient parallel processing capabilities.

## Installation

```bash
pip install myapp
```

## Usage

### Basic Usage

```bash
myapp process input.txt
```

### Advanced Options

```bash
myapp process --output result.txt --encoding utf-8 --threads 4
```

## Configuration

MyApp supports customization via a `config.yaml` configuration file:

```yaml
output_dir: ./output
encoding: utf-8
threads: 4
verbose: true
```

## Contributing

Pull requests are welcome. Please ensure that tests pass.

## License

MIT License
