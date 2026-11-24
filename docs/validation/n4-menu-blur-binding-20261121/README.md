# 主相机模糊覆盖续接

`IGamePostProcessingService.BindDownsample` 接受一个场景和固定角色，插件提交一次后，宿主会跟随该场景里的主相机重建。新 V5 候选支持 Steam 25977441 的 `MainMenu` 和 `GameStart`，值为 0、1 或 2。实际画面与恢复仍待实机验收。 [SDK 定义](../../../src/RizToolbox.Sdk/GamePostProcessingContracts.cs)

## 注册和读取状态

在插件清单的 `RequiredCapabilities` 声明 `riz.game.post-processing`。下面的辅助方法在插件宿主线程运行，返回的意图由插件作用域持有。

```csharp
using RizToolbox.Sdk;

static Result<IGameBlurBindingIntent> BindMenuBlur(IGamePostProcessingService service)
{
    return service.BindDownsample(
        new TargetQuery(Scene: "MainMenu", Role: GamePostProcessingRoles.MainCameraBlurDownsample),
        value: 1,
        priority: 10);
}
```

通过 `LastSnapshot` 读取上一次宿主处理结果。它可以在后台线程读取。`Stop()` 在宿主线程永久结束意图。插件禁用或能力卸载时，宿主负责释放。

| 状态 | 含义 |
| --- | --- |
| `Waiting` | 等待所选场景出现唯一符合条件的目标。 |
| `Ambiguous` | 所选场景有多个活动模糊来源，宿主暂缓写入。 |
| `Bound` | 当前代次已有这个插件的优先级层。更高优先级或较新的同优先级层可能生效。 |
| `Stopped` | 意图已结束。`Error` 可查看原因，后续重建不会恢复该意图。 |

同一组件发生外部改值时，系统保留实际值并结束旧覆盖。组件重建时重新取得代次，现有优先级规则也用于新代次。场景切换时，意图仅在提供程序仍活跃的期间等待，提供程序撤回会结束所有意图。实际恢复写入仍要求原组件满足原生校验，因此原组件消失或写入时出现多个候选，可能无法恢复原值。 [模块实现](../../../src/RizToolbox.Adapter/Modules/GamePostProcessingModule.Intents.cs) · [原生校验](../../../src/RizToolbox.Adapter/Modules/NativeGameBlurBackend.cs)

## 离线验证

模块检查使用正式插件宿主、共享目标及修改协调器和受控后端，覆盖等待、歧义、身份复用、重建、两个插件竞争、外部改值、通道失效、写入期间停用插件及能力卸载。 [定向用例](../../../tests/RizToolbox.Module.Tests/GamePostProcessingModuleChecks.cs) · [封存结果](../../../work/n4-menu-blur-binding-20261121/validation.json)

当前快照的 Release 解决方案 0 错误、66 条既有警告。模块 48/48、共享目标与修改 21/21、视觉 97/97、玩法外观 52/52、相机 65/65，合计 283/283。构建前后核对 1814 项输入。 [构建日志](../../../work/n4-menu-blur-binding-20261121/solution-final.log) · [模块日志](../../../work/n4-menu-blur-binding-20261121/Module-sealed.log)

## V5 候选包与 SDK

发行构建使用封存的源码，重新编译六个组件。两个 V7 映像夹具共 554 项，ZIP 内 58 项哈希回读一致，隔离 `ValidateOnly` 核对 26 个运行文件。包内 DLL 通过 48 项模块检查和 175 项倍率恢复检查。独立插件项目使用同源 SDK 引用包编译 `BindDownsample` 和 `LastSnapshot`。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-blur-binding-candidate-20261121-r1.zip) · [SDK 引用包](D:/riz-m2/v5-blur-binding-contracts-20261121-r1/sdk-feed/RizToolbox.Sdk.1.2.0.nupkg) · [包验证](../../../work/n4-menu-blur-binding-20261121/candidate-validation-r1.json) · [合同验证](../../../work/n4-menu-blur-binding-20261121/packaged-contract-validation-r1.json)
