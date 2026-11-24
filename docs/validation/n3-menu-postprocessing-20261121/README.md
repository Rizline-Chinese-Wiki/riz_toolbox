# 菜单主相机模糊降采样

Steam 25977441 的 `GameStart` 和 `MainMenu` 可按需提供 `riz.game.post-processing`。插件可以查询当前主相机的模糊降采样目标，并用租约覆盖 `Downsample`。参数接受 0、1、2 三个整数，影响模糊纹理的分辨率。当前为实验能力，实际画面变化与恢复待实机验收。

## 插件调用

在插件清单的 `RequiredCapabilities` 声明 `riz.game.post-processing`。在已启用插件的主线程中查询目标。`Target` 含当前绑定代次，切场景或换相机后需要重新查询。

```csharp
var service = context.GetService<IGamePostProcessingService>(
    GamePostProcessingCapabilityIds.PostProcessing).Service;
if (service?.GetDownsampleTarget() is { Success: true, Value: { } target })
{
    var result = service.OverrideDownsample(target.Target, 1, priority: 10);
    if (result.Success)
        blurLease = result.Value; // IMutationLease<int> field owned by the plugin scope.
}
```

租约由插件作用域持有，也可提前 `Dispose()`。同一目标的多个覆盖按优先级取胜。写入失败时尝试恢复经读回确认的前值，外部写入保留实际值并结束现有租约。目标同时出现在共享语义目标服务，角色和别名均为 `game.post-processing.main-camera.blur-downsample`，属性键为 `downsample`。

当前源码新增了按场景续接覆盖的绑定意图。 [调用与验证](../n4-menu-blur-binding-20261121/README.md)

## 实现依据

| 证据 | 结论 |
| --- | --- |
| 打包场景 `level1` 与 `level2` | 两个场景的 `Camera` 和 `TranslucentImageSource` 分别属于同一个已启用的 `Main Camera` 对象。序列化 `Downsample` 分别为 2 和 0。选择这两个资源值之间的整数作为 SDK 范围。 [静态复核](../../../work/n3-menu-postprocessing-20261121/static-blur-r2.json) |
| 固定程序集和方法体 | `get_Downsample` 与 `set_Downsample` 读取或写入 `0x40` 的 `Int32`。`OnRenderImage` 将它和上次值比较，纹理尺寸计算读取同一字段并右移。 [指令证据](../../../work/n3-menu-postprocessing-20261121/static-blur-r2.json) |
| 固定目标的接线 | 运行时复核程序集、方法元数据及 RVA，按 active scene 和 `Camera.main` 的实际对象绑定。只接受同一个 GameObject 上的唯一活动模糊来源。写前重查唯一性，替换后的旧句柄失效。 [接线](../../../src/RizToolbox.Adapter/Modules/NativeGameBlurBackend.cs) |

本轮对缓存程序集和场景文件做只读核对。IDA 服务缺少 idalib-mcp，指令和场景证据由现有 Capstone、pefile 和 UnityPy 复核。 [复核脚本](../../../work/n3-menu-postprocessing-20261121/Capture-StaticBlur.py)

## 验证

Release 解决方案 0 错误、66 条既有警告。模块 43/43、共享目标与修改 21/21、视觉 97/97、玩法外观 52/52、相机 65/65，共 278 项。模块用例通过正式模块与插件宿主，使用受控 blur 后端，验证按需加载、别名竞争、写入后异常回读恢复、外部改值、歧义与目标重建。 [结果汇总](../../../work/n3-menu-postprocessing-20261121/validation-r1.json) · [模块日志](../../../work/n3-menu-postprocessing-20261121/module-test-final.log)

后处理已编入新的 V5 候选。使用包内 DLL 复跑模块 43/43，独立项目从当前 SDK NuGet 引用包编译成功。 [V5 候选](D:/riz-m2/RizToolbox-0.3.0-V5-postprocessing-candidate-20261121-r1.zip) · [SDK 引用包](D:/riz-m2/v5-postprocessing-contracts-20261121-r1/sdk-feed/RizToolbox.Sdk.1.2.0.nupkg) · [包内验证](../../../work/v5-postprocessing-delivery-20261121/packaged-contract-validation-r1.json)
