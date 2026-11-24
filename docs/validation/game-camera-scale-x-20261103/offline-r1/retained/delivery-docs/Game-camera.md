# 局内相机（`riz.game.camera`）

> 状态：`experimental` / `modifiesGameplay`。相机位置与缩放可能影响输入投影、玩法和成绩。当前声明当前局内相机 rig 的 `local-position-x` 和 `local-scale-x` 两个独立标量。

## 能力与合同

| 项 | 值 |
|---|---|
| capability | `riz.game.camera` |
| 角色 | `GameCameraRoles.RigLocalPositionX`、`GameCameraRoles.RigLocalScaleX` |
| 服务 | `IGameCameraService` |
| 依赖 | `targets`、`mutations`、`lifecycle` |
| 目标 | 当前 active、正确已加载 scene 的 `NormalModeInLevelController.LevelInformation` 对应的相机 rig |
| 写入 | 分别修改 `Transform.localPosition.x` 或 `Transform.localScale.x`；每次真实读取该Vector3并保留当时Y/Z |

```csharp
public interface IGameCameraService
{
    Result<IReadOnlyList<GameCameraScalarProperty>> GetSupportedProperties();
    Result<IReadOnlyList<GameCameraTarget>> GetTargets(TargetQuery? query = null);
    Result<IMutationLease<float>> OverrideScalar(GameCameraScalarOverrideRequest request);
}
```

`GameCameraScalarProperty` 明确 `Unit`、`Minimum`/`Maximum`、范围证据、来源和 `ExternalWritePolicy`。当前没有建立数值上下界，`Minimum`/`Maximum` 为 null，不能用其它属性的范围推断。

| 属性 | 稳定角色 | 单位 | 当前局别名 |
|---|---|---|---|
| `local-position-x` | `game.camera.rig.local-position-x` | `unity-local-unit` | `game.camera/current/local-position-x` |
| `local-scale-x` | `game.camera.rig.local-scale-x` | `dimensionless` | `game.camera/current/local-scale-x` |

`local-scale-x` 表示实际rig的局部X轴缩放值。游戏的 `CameraControl.SetScale` 将基准Vector3各轴分别乘以游戏scale，基准XYZ不保证相等；本接口不把一个X值称为统一缩放倍率，也不覆盖Y/Z来推算其它轴。两个属性的目标、别名和修改通道分别仲裁；调用者可按具体Role查询，宽查询可能返回多个目标。

## 目标解析

1. 唯一 active、正确已加载 scene 的 `NormalModeInLevelController.LevelInformation`。
2. `LevelInformation.cameraControl +0x78` → `CameraControl.levelInformation +0x20` 回指同一 information。
3. `LevelInformation.noteJudgeControl +0x58` → `NoteJudgeControl` 的 Camera 字段；`CameraControl.Update@0x3E32A0` 调用位置与缩放更新。
4. 通过当前 run 缓存的真实 `UnityEngine.Camera` 验证同体或实际后代关系；替换 information/chart/progress/rig/Camera 会使旧 lease 失效。
5. 空场景/无 active run 时服务仍可用，但 `GetTargets` 返回空；不会发明目标。

所有 native getter/setter 调用前后都重新核对 run、scene、active、祖先关系与非 terminal progress；任一不匹配即拒绝，不写入陈旧根。

位置和缩放候选分别保留自己的完整图根。一个属性的目标退役、发现失败或租约结束，不会提前释放另一属性仍持有的对象根。服务只接收backend最初声明的完整属性记录，包含单位与外写策略；候选不能通过自报属性字符串扩大写入范围。

## 使用约定

- `OverrideScalar` 返回 scope-owned `IMutationLease<float>`；释放时只恢复该通道，不能把外部新写入的 Y/Z 一并回滚。
- 共享 `SemanticTargetRegistry<float>` / `MutationCoordinator` 负责别名、优先权和外部 baseline；不要绕过 registry 直接写 native 字段。
- 该能力应作为 `modifiesGameplay` 的 run effect 进入审计；不要在未确认实际效果前把它当作纯视觉开关。
- 相机rig属性的数值范围、输入投影影响没有经过实机验收；插件应允许 `CapabilityUnavailable`、空目标和拒绝。

## 集成状态与边界

- `GameCameraModule` 已注册到生产 `RizlineAdapter`（`GameAppearanceModule` 之后），共享 `SemanticTargetsModule`、`GameAssemblySha256`、`GameLifecycleSnapshot()` 与 `MatchesCurrentGameplayRun`。
- `RizToolbox.GameCamera.Tests` 已加入 `RizToolbox.sln` 与 `scripts/Build.ps1`；相机专项 33/33，整树 27 套 2369/2369 通过。证据见 [game-camera-integration-20261030](validation/game-camera-integration-20261030/README.md)。
- `local-scale-x` 为2026-11-03独立候选中的新增功能。新915输入freeze的专项55/55、整树28套2620/2620、SDK独立双插件14/14、solution0错误／56警告及六组件Release已实际通过；11对PE/PDB与990份源码文档已逐项核对。候选尚未合主，证据见 [缩放离线候选](validation/game-camera-scale-x-20261103/offline-r1/2026-11-03_game-camera-scale-x-report.md)。上述历史位置X结果只签当时版本。
- **实际 update/render 次序未证明。** `CameraControl.Update` 每帧覆写 X；`GameCameraModule.IntervalSeconds = 0` 的每 tick reapply 只增加协调机会，不能保证晚于相机 Update 或成为渲染前最后一次写入。本批没有添加相机 hook。
- 真实游戏的持续可见效果、输入投影、玩法/成绩影响、停止恢复和生产首次准入仍待实测；性能优化后置。
