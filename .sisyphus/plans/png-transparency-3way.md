# PNG 透明信息抛弃（Flatten Alpha）

## TL;DR

> 保留原有透明背景切换按钮（`☐`，透明/棋盘格切换）**完全不变**，新增一个独立按钮（`◼`）切换是否抛弃 PNG 透明信息。
> 抛弃透明时用 `FormatConvertedBitmap` 将图片转为无 alpha 通道格式，透明区域的 RGB 原始颜色显现。
>
> **涉及文件**: `MainWindow.xaml.cs`, `MainWindow.Preview.Image.cs`
> **预估**: ~30 分钟

---

## 方案说明

### 两个按钮分工

| 按钮 | 位置 | 功能 | 变动 |
|------|------|------|------|
| `☐` (已有) | 右侧工具栏 | 切换下方背景：透明 ↔ 棋盘格 | **保留不变** |
| `◼` (新增) | 右侧工具栏 | 切换图片本身：保留 alpha ↔ 抛弃 alpha | **新增** |

两个按钮**完全独立**，互不影响。

### 视觉对比

```
原图(透明背景):   点☐(棋盘格):      再点◼(抛弃透明):
┌──────┐          ┌──────┐          ┌──────┐
│  🎨  │          │  🎨  │          │  🎨  │
│ (透)  │          │ ▦▦▦  │          │ 不透明│
│ 看穿  │          │ ▦▦▦  │          │ RGB色│
└──────┘          └──────┘          └──────┘
 背景透明           棋盘背景          图片本身无透明
```

### 技术原理

抛弃透明按钮通过替换 `PreviewImage.Source` 实现：

1. 预览加载时**缓存原始 BitmapSource** (`_originalPreviewImage`)
2. 点 `◼` → 调用 `FlattenAlpha()` 生成无 alpha 副本 → 替换
3. 再点 `◼` → 恢复 `_originalPreviewImage`

`FlattenAlpha` 实现：

```csharp
private BitmapSource FlattenAlpha(BitmapSource source)
{
    var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
    int stride = formatted.PixelWidth * 4;
    byte[] pixels = new byte[stride * formatted.PixelHeight];
    formatted.CopyPixels(pixels, stride, 0);
    for (int i = 3; i < pixels.Length; i += 4)
        pixels[i] = 255;
    var result = BitmapSource.Create(
        formatted.PixelWidth, formatted.PixelHeight,
        formatted.DpiX, formatted.DpiY,
        PixelFormats.Bgra32, null, pixels, stride);
    result.Freeze();
    return result;
}
```

---

## 改动内容

### 1. `MainWindow.xaml.cs`

添加字段：
```csharp
private BitmapSource? _originalPreviewImage; // 原始图片缓存，用于恢复
private bool _flattenAlphaEnabled;           // 是否抛弃了透明
```

### 2. `MainWindow.Preview.Image.cs`

**`ShowImagePreviewAsync`** — 在设置 `PreviewImage.Source` 后添加：
```csharp
_originalPreviewImage = PreviewImage.Source;
_flattenAlphaEnabled = false;
```

**工具栏按钮** (约第 198-202 行):

现有 `☐` 按钮**保持完全不变**。新增 `◼` 按钮：

```csharp
(ext == ".png" || ext == ".ico" || ext == ".webp")
    ? new[] {
        // 已有按钮：不变
        new ToolbarButton { Text = "☐", Tooltip = L.T(L.Preview_ToggleTransparency), IsToggle = true, IsChecked = _transparentBgEnabled, OnClick = ToggleTransparencyBg },
        // 新增按钮：抛弃透明
        new ToolbarButton { Text = "◼", Tooltip = "切换透明 (显示 RGB 原始颜色)", IsToggle = true, IsChecked = _flattenAlphaEnabled, OnClick = ToggleFlattenAlpha },
      }
    : Array.Empty<ToolbarButton>()
```

**新增 `ToggleFlattenAlpha`**:

```csharp
private void ToggleFlattenAlpha()
{
    _flattenAlphaEnabled = !_flattenAlphaEnabled;
    if (_flattenAlphaEnabled)
    {
        if (_originalPreviewImage != null && PreviewImage.Source is BitmapSource current)
            PreviewImage.Source = FlattenAlpha(current);
    }
    else
    {
        if (_originalPreviewImage != null)
            PreviewImage.Source = _originalPreviewImage;
    }
}
```

**新增 `FlattenAlpha(BitmapSource)`** — 如上所述。

---

## 执行步骤

1. `MainWindow.xaml.cs`: 添加 `_originalPreviewImage` + `_flattenAlphaEnabled`
2. `MainWindow.Preview.Image.cs`: 缓存原始图片
3. `MainWindow.Preview.Image.cs`: 工具栏新增 `◼` 按钮
4. `MainWindow.Preview.Image.cs`: 添加 `ToggleFlattenAlpha()` + `FlattenAlpha()`
5. `dotnet build` 验证

---

## 验证

- 预览 PNG/ICO/WebP 图片
- 点 `☐` → 背景透明/棋盘格切换（原有行为不变）
- 点 `◼` → 图片本身变成无透明，再点恢复
- 两个按钮独立工作互不干扰
- 切换文件后重置
