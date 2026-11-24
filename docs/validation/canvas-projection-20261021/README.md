# MainMenu Canvas 投影

旧包已能通过真实 `Component.GetComponentInParent(Type,bool)` 找到主菜单 Canvas，但 Canvas 局部矩形和 pixelRect 比例不同，旧等比假设使 LOCAL 窗口拒绝创建。失败现场保留在 `../runtime-local-catalog-20261021/menu-parent-fixed-canvas-rejected-20261021T092329Z/`；这不代表坐标映射已通过。

本次通过精确签名 `Transform.TransformPoint(Vector3)`、`RectTransformUtility.WorldToScreenPoint(Camera,Vector3)` 取得四角，另外用 Camera.WorldToScreenPoint 的 Z 验证前向同深度平面。只有轴对齐、正向、有限、非退化的仿射映射可用；旋转、斜切、透视深度变化、背面、RenderTexture 和非主屏相机保守拒绝。可视区域取实际四角、Canvas.pixelRect、camera.pixelRect、Screen 的交集。

父坐标位置分别用 X/Y 单位反算，只修改自建窗口根的 localScale.X 补偿比例；内部字体和控件仍使用同一个单位缩放，对应公开 UiCoordinates 的均匀映射。投影缓存只保存值，按 Capture/ClearInput 清空，窗口释放移除 Canvas key。没有修改原 Canvas 的缩放、相机或布局。

验证命令：

```powershell
dotnet run --project tests/RizToolbox.UiInput.Tests/RizToolbox.UiInput.Tests.csproj -c Release
```

- `red.log`：测试引用 internal exception 的编译失败，不能作为行为 red。
- `red-behavior.log`：73/77；旧73通过，新4组因 helper 尚未实现而失败。
- `green.log`：77/77；非均匀缩放、overscan/相机与屏幕偏移交集、实际根补偿的往返坐标、旋转/深度/反转/非法拒绝均通过。

这些是坐标数学与拒绝路径证据。真实 MainMenu 窗口的可见位置、按钮命中、场景切换及缩放变化须由之后新包实机日志和截图证明；不能用托管回归代替。
