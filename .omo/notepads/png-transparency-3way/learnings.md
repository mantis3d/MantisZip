## 2026-05-27 PNG Flatten Alpha Implementation

- FlattenAlpha uses `FormatConvertedBitmap` → `Bgra32` → byte array → set alpha=255 for every pixel
- Toolbar addition: `◼` button alongside existing `☐`, both are `IsToggle = true`
- `_originalPreviewImage` caches the original `BitmapSource` when image is first loaded
- `_flattenAlphaEnabled` tracks toggle state, reset to `false` on each new image preview
- Both buttons are independent: `☐` changes background, `◼` changes image source
- `ToggleFlattenAlpha` toggles between `_originalPreviewImage` and `FlattenAlpha(current)`
- Need `using System.Windows.Media.Imaging;` in `MainWindow.xaml.cs` for `BitmapSource`
