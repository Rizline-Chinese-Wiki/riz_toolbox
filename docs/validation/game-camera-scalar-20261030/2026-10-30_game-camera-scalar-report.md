# 局内相机 local X 标量实现与受控验证

普通技术验证（`flavor = null`）；范围见 [scope.md](scope.md)。

本轮实现了具体的 `game.camera.rig.local-position-x` float 属性，复用宿主共享 `SemanticTargetRegistry<float>` 和 `MutationCoordinator`。它来自当前 run 的 `CameraControl` transform，并通过该 run 缓存的真实 `UnityEngine.Camera` 验证两者同体或实际后代关系。源码没有给颜色接口塞 float，也不把相机伪装成 VisualAsset。隔离构建成功，33/33 检查通过。真实游戏未运行；可见效果和恢复尚未实测。

相机位置可能影响输入投影、玩法或成绩，因此该能力应标为 experimental / modifiesGameplay，由 root 处理 run effect 审计。`CameraControl.Update` 每帧覆写 X；每宿主 tick 重 apply 只增加协调机会，不能证明晚于相机更新或成为渲染前最后一次写入。本批没有添加相机 hook。

## 固定源码路径与更正

固定 dump 中 `_camera +0x70` 与 `Start@0x3FD3E0` 的实际声明类是 **`InLevel.NoteJudgeControl`**，不是 `InputControl`。`LevelInformation.noteJudgeControl +0x58` 指向它；`InputControl +0x50` 没有 camera 字段。静态 JSON 的 `sourceFacts` 记录该更正，并由脚本核对精确类块。

真实路径为：

1. 唯一 active、正确已加载 scene 的 `NormalModeInLevelController.LevelInformation`。
2. `LevelInformation.cameraControl +0x78` → `CameraControl.levelInformation +0x20` 必须回指同一 information。
3. `LevelInformation.noteJudgeControl +0x58` → `NoteJudgeControl.levelInformation +0x20` 必须回指同一 information。
4. `NoteJudgeControl._camera +0x70` 是 `Start` 缓存的 `Camera.main`；这里读取当前 run 的现有字段，不重新扫描全局 Camera 或使用菜单 `GameManager.mainCamera`。
5. 分别读取 CameraControl 与 Camera 的官方 `Component.get_transform`。Camera transform 必须与 rig transform 相同，或官方 `Transform.IsChildOf(rig)` 为真；该关系每次访问重核。

chart、progress、information、cameraControl、noteJudge、Camera 与 transform 的具体引用全部 rooted 并逐次复查。`loadOver`、`!levelOver`、`!crashed`、`progress.timeReady` 加上宿主 `Playing/Paused` 决定当前目标；无 `levelBegan` 门槛。强制 matcher 核对 rooted controller/info、run ID 与 scene generation；没有 permissive 默认值。

## 完整静态方法证据

使用 pefile 解析 imports/exports 与 `.pdata` 的全部 RUNTIME_FUNCTION 片段，再以脚本符号边界限定方法范围，完整解码并验证所有字节均被消费。初次只取首片段会截断 SetScale/SetXPosition，本轮已合并其同一方法内全部片段；最终 JSON 包含每片段及完整方法 hash。

| 方法 | 完整范围 / 字节 | SHA256 | 消费 |
|---|---|---|---|
| `CameraControl.SetXPosition` | `[3E3060,3E3292)` / 562 | `c13729b7c99784a9a692bd7a325db19b5f784e61549a74f65c8013c3a5402c16` | 计算 `scale*xPosition`，自身 transform 的 `localPosition=(X,-2.4,0)` |
| `CameraControl.SetScale` | `[3E2E00,3E3060)` / 608 | `8dbdb6bd01875bd0fe24ae9ef9428d7deb2fb9f411cd504aec4890ea00886b1e` | 自身 transform 的 localScale；仅作为来源证据，不暴露第二属性 |
| `CameraControl.Update` | `[3E32A0,3E3301)` / 97 | `b3620bbcbe97763df88ed70329fe0fbe3fbeedbfe1003bb26e605acbba1eb447` | 条件就绪后调用 SetScale，再 tail-call SetXPosition |
| `NoteJudgeControl.Start` | `[3FD3E0,3FD405)` / 37 | `ae86a1bb8dc7cfd0d93f04710752f56474d4b642c707a03cb45f711edd786952` | 缓存 Camera.main 到 `_camera` |
| `Component.get_transform` | `[1536440,1536473)` / 51 | `b7cebcd24a88da7e3969da2da3697e1b92d80d1a7b2304bb27d3e22bea62fd0e` | 完整官方 reference getter wrapper |
| `Transform.get_localPosition` | `[1561640,156168D)` / 77 | `9c491dacfae649cec8d0e6693a0239f5fa71430c39e495720c5e6785439ee034` | 完整非 injected Vector3 getter wrapper |
| `Transform.set_localPosition` | `[1561CD0,1561D12)` / 66 | `ff34c6262b9386729319be565efaca3242b2a52786ea79ee16e09ba2646e6c7a` | 完整非 injected Vector3 setter wrapper |
| `Transform.IsChildOf` | `[15605F0,1560632)` / 66 | `9b5cf715d9a42b1b14e7d1a5d1d9ad1695c97ea33ba6144da9c490505ab6ef48` | 完整祖先关系 wrapper |

Unity wrapper 转入引擎 icall；未把 wrapper 解码冒充引擎内部实现分析。IDA helper 返回 Missing idalib-mcp，日志保留在 D 盘；本轮明确采用已安装的 Capstone 5.0.6 / pefile 2024.8.26 替代，没有启动 GUI。

生产 constructor 检查实际已加载 GameAssembly 的文件 SHA256、上述完整方法体 hash，以及写入/读回方法的官方 metadata、MethodInfo RVA、参数、返回类型与声明类。通过当前 fixture 不表示已对真实加载模块执行该 gate。

## 标量合同与恢复策略

| 项 | 值 |
|---|---|
| capability / service | `riz.game.camera` / `IGameCameraService` |
| role / property | `game.camera.rig.local-position-x` / `local-position-x` |
| unit | `unity-local-unit`，相对 parent 的 local-space 坐标，未声称 SI 米或 chart fraction |
| range | 仅 finite Single；Minimum/Maximum 均 null，没有臆造游戏上下界 |
| coordinator policy | `PriorityOverride` + `AdoptAndReapply` |
| generation | target generation、run ID 与 scene generation；不跟随对象替换 |
| write / rollback | 每次 fresh 读取 Vector3，仅替换 X，保留该次读取的 Y/Z，再按正式 Vector3 ABI 调 setter并读回实际 X |

因此 scope 释放时也只恢复 X；外部变化的 Y/Z 被保留。外部 X 变化成为新的 baseline，持有 lease 时按当前赢家重新应用。清理时若观察到新的外部 X，则保留外部写入。setter 后读回不一致不能报告成功；mutate-then-throw 走共享 coordinator 的条件回滚。getter 可重入时，在 fresh getter 后、setter 前再核当前 graph。

`GetTargets` / `OverrideScalar` 只从 camera 自己拥有的 bindings 选择目标，不能用 query 选择其他 provider 的 float。canonical key 与别名指向相同 target/property channel，generic `IMutationService<float>` 与 camera lease 共同仲裁。

## 构造与集成边界

```csharp
GameCameraService(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
    SemanticTargetRegistry targets, MutationCoordinator mutations,
    Func<GameLifecycleSnapshot?> lifecycle,
    Func<long, long, string, long, bool> matchesRun)
NativeGameCameraBackend(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
    Func<long, long, string, long, bool> matchesRun)
```

以上为 internal constructor 签名上下文；native backend 公开给 root module 的静态字段是 `Fingerprint`、`ValidationEvidence`、`Requirements`。root 提供 Module/注册/metadata，建议 coordinator 每宿主 tick refresh、discovery 保持 250ms；实际渲染次序仍需单独验证。

## 可复现受控检查

最终 D 盘原型在 `D:/riz-build-cache-next/game-camera-scalar-prototype-20261030`。它使用冻结的 SDK/Core/Runtime DLL 与本目录自己的 `frozen-fixtures` 源码，最终构建不读取 live repo 源码。

```powershell
Set-Location D:/riz-build-cache-next/game-camera-scalar-prototype-20261030
dotnet build RizToolbox.CameraPrototype.Tests.csproj --artifacts-path D:/riz-build-cache-next/game-camera-scalar-reproduce-20261030 --disable-build-servers --ignore-failed-sources -p:NuGetAudit=false -p:UseSharedCompilation=false -m:1 -v:minimal
& D:/riz-build-cache-next/game-camera-scalar-reproduce-20261030/bin/RizToolbox.CameraPrototype.Tests/debug/RizToolbox.GameAppearance.Tests.exe
```

隔离原型的 tests assembly 暂用 `RizToolbox.GameAppearance.Tests` 复用已冻结 Runtime 的 friend attr。交付 overlay 的正式项目名为 `RizToolbox.GameCamera.Tests`，并包含独立 Runtime friend attr；root 集成后以真实项目名再编译。已有 `NativeVector3` 和 `AdapterOperationException` 在原型中复制为支持输入，overlay 不重复覆盖这些共享文件。

最终 `tests-build-final.log` 为 0 warnings / 0 errors，`tests-final.log` 为 33/33。16 项 service 检查覆盖双插件及 generic float 仲裁、别名/歧义、run/scene、外部 baseline、仅 X 恢复、失败回滚、非有限值、foreign provider 拒绝、roots/scope/thread。17 项实际 NativeRuntime 检查覆盖完整 discovery、所有具体字段与方法引用替换、就绪/terminal/scene/active/祖先关系、metadata/receiver/Vector3 ABI、重入、读回及 native 失败恢复。测试内反射仅用于构造实际 native backend 私有实现，没有增加绕过生产 PE gate 的入口。

## Evidence → Finding → Path

| Evidence | source_ref / artifact_path | repro_command / 观察 |
|---|---|---|
| E-001 | `camera-static-evidence.json`；hash 见交付清单 | `python inspect-camera.py <GameAssembly.dll> <dump.cs> <script.json> <new-output.json>`；8 个完整方法、imports/exports、owner 更正与 `runtimeAcceptance:false` |
| E-002 | D 盘 `tests-build-final.log`；hash 见 `validation-artifact-hashes.json` | 上述 build 命令；0 warnings / 0 errors |
| E-003 | D 盘 `tests-final.log`；hash 见 `validation-artifact-hashes.json` | 上述 exe；33/33 passed |

| Finding | severity / category / status | evidence_ids | confidence / location | 结论 |
|---|---|---|---|---|
| F-001 | `n/a_re` / `reverse_algo` / `validated` | E-001 | high；固定 dump / 8 完整方法 | 具体位置路径、官方 Vector3 accessor 与 NoteJudgeControl Camera 来源得到固定构建静态证据支持 |
| F-002 | `n/a_re` / `other` / `validated` | E-002, E-003 | high；Camera service/native graph | 受控环境中只改 X、共享仲裁、旧 graph 失效与 roots 清理按预期工作 |
| F-003 | `n/a_re` / `other` / `candidate` | E-001 | medium；实际宿主/相机/渲染次序 | 每帧覆写已确定；持续可见与输入投影效果需真实游戏独立验收，未声称通过 |

P-001（`path_type=callflow`）：rooted controller/info + matcher → exact chart/progress/rig/judge/Camera references → 实际 Camera/rig transform 关系 → shared float channel → fresh Vector3 getter → 再核 graph → 只替换 X 的 setter → 实际 getter 读回（E-001/F-001，E-003/F-002）。剩余风险为真实回调与渲染次序、持续可见、输入/玩法影响及停止恢复。

## 后续 after-original 路径评估

现有代码可解析 `CameraControl.Update@0x3E32A0` 为 `ExactHookAbi.InstanceVoid`，再用 `ExactHookBroker.BindRetainedObservation` 在 original 正常返回后以 rooted run/rig context 刷新既有 X channel。broker 的 `AfterRetained` 先于普通 After observers，但这仍不保证后续其他脚本或渲染前无人改写。

注意该 legacy InstanceVoid 路径不同于新 native 边界：`ResolveObservedInstanceVoid0` 硬限 UnityEvent.Invoke@0x15642E0，OperationVoid0 也走 UnityEvent/Tween allowlist。不能把它们当成已发布的通用 Camera native lane。本轮没有安装该 hook、修改 native 操作或冻结发布；如采用应单独验证其原始异常/SEH、in-flight、退出与渲染次序保障。

## 时间线与历史

本轮先确认 fixed PE/dump 身份并纠正 camera owner；完整静态读取从首 RF 片段修正为同方法所有片段；随后在 D 盘实现单一 scalar，并用冻结依赖与当前进程自有 exports 编译和检查。早期日志保留了原型缺共享 helper、单元素 params 语法、旧 scope proxy 预期及缓存 method metadata 的问题；修复后最终 33/33。原有 GameAppearance 14 文件只读快照保留，live repo 没有由本相机任务写入。root 后续集成与整仓验证应另记独立证据。

正式接入证据见 [game-camera-integration-20261030](../game-camera-integration-20261030/README.md)：`GameCameraModule` 已注册到 `RizlineAdapter`，测试项目已入 `.sln` 与 `Build.ps1`；解决方案 0 error，相机 33/33，整树 27 套 2369/2369。真实更新/渲染次序、可见效果与输入投影仍未实测。
