# GameAppearance 实现与受控验证

2026-10-30；报告结构采用普通技术验证（`flavor = null`）。

GameAppearance 已提供实验性的轨道起点色、轨道终点色与固定来源的 note renderer 颜色能力。当前 SDK 用当前 run、scene generation 和独立 target generation 限定覆盖范围，所有修改进入宿主共享的语义 target 与 mutation coordinator。固定构建的 setter、字段、方法签名和原生对象图均有准入检查。最新隔离构建成功，40/40 受控检查通过。真实游戏内可见效果与恢复尚未验收；本轮没有安装、启动或操作真实游戏。

## 范围

详见 [scope.md](scope.md)。本报告覆盖 GameAppearance owned 源码及测试，不替代 root 的模块、注册与全仓集成验证。

## 可用属性与行为

| SDK role | 原生来源 | 写入与读回 |
|---|---|---|
| `game.track.segment.start-color` | 当前 `LevelInformation.lineControl` 的 dynamic/group lines → 可见的 `LineInformation` → active `lineRendererPlan` → 当前 `poolChild` | `LineInformation.set_startColor` 写缓存并传播；读回实际 `LineRenderer.startColor` |
| `game.track.segment.end-color` | 与起点相同的具体轨道图，但拥有独立属性通道 | `LineInformation.set_endColor` 写缓存并传播；读回实际 `LineRenderer.endColor` |
| `game.note.renderer.color` | 当前 `NoteControl.notes` → active `NoteInformation.noteRenderers[]` → active plan → 当前 `poolChild.targetComponent`，实际类必须是 `SpriteRenderer` | 声明在 `SpriteRenderer` 上的 `set_color` / `get_color` |

notes 元数据不能通过时，仅关闭 note 属性；不退回 UI 全局扫描或 shader 名称猜测。轨道属性必须通过固定构建检查。发现过程在完整来源扫描完成后发布；来源过大或中途失败时不发布部分结果。多对象匹配返回 `AmbiguousTarget`，要求调用方选择具体别名或 canonical key。

目标缓存有 512 项上限；完整来源扫描有 4096 项边界。native 对象图保留由 registry 持有的 roots，并在失效、注册失败、服务退出时释放。外部颜色重写采用 `AdoptAndReapply`：运行中重新应用当前胜出的 lease，lease 结束时恢复可确认的外部基线。退出 GameAppearance 不释放宿主其他 UI provider 的 targets/channels。

## 当前 run 与原生图

生产构造必须传入宿主的 `Func<long,long,string,long,bool> matchesRun`，不存在默许通过的默认 matcher。`CaptureRun` 只选择唯一 active、已加载目标 scene 的 NormalMode controller，先 root controller 与 information，再核对宿主当前 run。`loadOver`、`!levelOver`、`!crashed` 和 `ProgressControl.timeReady` 决定原生就绪；生命周期允许 `Playing` 和 `Paused`。代码没有把 `levelBegan` 作为门槛。

每次 native read/write 均经过 `NativeGraph.Current`，重新核对 matcher、实际类、controller active、scene、记录的引用关系、数组成员与就绪 flags。`Fork` 保留原始 run/scene 标识。这样，sampler 两次更新之间出现的替换 controller/information 不会继承旧 run 的颜色意图。`gameObject` 与 boxed scene 在后续原生调用前保持 rooted。

## 固定构建证据

固定 `GameAssembly.dll` SHA256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。生产路径要求 Windows x64、实际已加载模块的文件 SHA256、setter MethodInfo RVA 和内存中完整 176-byte 方法体 hash 同时符合；无 SDK 原始地址写入入口。

| setter | RVA | 官方 cache offset | 全 176-byte SHA256 |
|---|---|---|---|
| `LineInformation.set_startColor` | `0x3FB510` | `+0x68` | `094a9a412fd933be2af67e01ce61dc32cc215718aa8487018f77d9f5b1e73c82` |
| `LineInformation.set_endColor` | `0x3FB460` | `+0x78` | `d2b07dbe9ab2e2198f3e2ccbb11c06457d3121648e1840e5110fce55ce260883` |

本轮从既有 JSON 的 instruction bytes 重算上述两个 hash，均与生产常量一致。既有静态记录包括 `LineInformation.Appear@0x3FAFF0`、`LineControl.SetLineColor@0x3ED530`、`NoteInformation.Appear@0x3FCDC0` 与 `EasyPoolChild.Reset@0x3F7C00`。这仅证明固定构建的静态调用关系，不能证明屏幕效果。

## 可复现验证

在仓库根目录执行以下命令；选择一个新的 artifacts 目录保留历史。

```powershell
dotnet build tests/RizToolbox.GameAppearance.Tests/RizToolbox.GameAppearance.Tests.csproj --artifacts-path D:/riz-build-cache-next/game-appearance-reproduce-20261030 --disable-build-servers --ignore-failed-sources -p:NuGetAudit=false -p:UseSharedCompilation=false -m:1 -v:minimal
& D:/riz-build-cache-next/game-appearance-reproduce-20261030/bin/RizToolbox.GameAppearance.Tests/debug/RizToolbox.GameAppearance.Tests.exe
```

测试可选接收名称子串参数，例如 `"native graph"`。测试内使用反射组装实际私有 `NativeGraph`/`NativeBinding` 与当前进程自有对象；所有 field、root、scene 与 accessor 操作仍通过实际 `NativeRuntime` 的 export delegates。没有给生产 constructor 添加绕过 PE 准入的测试入口。该方式不覆盖真实 loaded-PE gate 或真实 Unity 游戏执行。

最终输出目录为 `D:/riz-build-cache-next/game-appearance-run-guard-20261030`；SDK 为 .NET SDK 8.0.418。`build-r4.log` 显示 0 warnings / 0 errors；`tests-r4.log` 记录 40/40。

| 检查层 | 项数 | 主要覆盖 |
|---|---:|---|
| service 与共享 coordinator | 25 | 两插件及 generic mutation 竞争、别名、歧义、旧 run/scene/对象失效、pool 所有权、外部基线恢复、失败回滚、读回不一致、配额、roots、scope/thread |
| 实际 NativeRuntime accessor exports | 10 | Color ABI、独立轨道起末色、SpriteRenderer property、声明类与参数/返回类型、offset、错误或已释放 receiver、实际 rendered getter、worker thread 拒绝 |
| 实际私有 NativeGraph | 5 | rooted matcher/scene/receiver、两次采样之间的 run 变更、Fork 的 run/scene、information 引用替换、inactive controller、unloaded/invalid scene |

## Evidence → Finding → Path

| ID | source_ref / artifact_path | content_hash（SHA256） | repro_command / 观察 |
|---|---|---|---|
| E-001 | `D:/riz-build-cache-next/game-appearance-static-20261030/appearance-evidence.json` | `5d9a29eee95b4e6a1131c1ba4ad1fa672815e5f92f3aac66546b77bb9a542fbc` | 离线记录；读取 `scopes[].instructions[].bytes` 重算两个 setter hash；`runtimeAcceptance: false` |
| E-002 | `D:/riz-build-cache-next/game-appearance-run-guard-20261030/build-r4.log` | `e413fa95ff6ff77c72f731a9f7028ec6c047ca23213b0da6434177eabc6f9348` | 上述 `dotnet build` 命令；0 warnings / 0 errors |
| E-003 | `D:/riz-build-cache-next/game-appearance-run-guard-20261030/tests-r4.log` | `0c58864d30135717c2a7e3b0559a5d04a5eaeb2ab171626b65d90b9d820f2456` | 上述 exe；40/40 passed |

证据观察时间：E-001 既有记录为 2026-10-30 10:04:44 +08:00，本轮 11:57 重算 hash；E-002/E-003 为本轮最终 r4。完整 owned-source 与日志 SHA 清单另存最终 D 盘目录。

| Finding | severity / category / status | evidence_ids | confidence / location | 结论 |
|---|---|---|---|---|
| F-001 | `n/a_re` / `other` / `validated` | E-002, E-003 | high；`GameAppearanceService` | 受控宿主中，具体对象颜色与共享 target/mutation 进入同一协调通道，旧 generation、scope 和失败路径按预期失效或恢复 |
| F-002 | `n/a_re` / `other` / `validated` | E-002, E-003 | high；`NativeGraph.Current` / `NativeBinding` | 受控 native exports 中，原生图在 run/引用/controller/scene 改变后拒绝读写，Fork 不迁移旧 intent |
| F-003 | `n/a_re` / `reverse_algo` / `candidate` | E-001 | medium；固定 build setter 与 note pool 来源 | 静态数据流与 runtime 准入常量一致；真实游戏的可见性与恢复仍待独立验收 |

P-001（`path_type=callflow`）：当前生命周期与 rooted controller/info → matcher 核对 run/scene（E-003/F-002）→ 完整来源发现具体属性（E-001/F-003）→ registry target 与 shared mutation（E-003/F-001）→ 每次实际 rendered read/write 重新验证原生图（E-003/F-002）。剩余风险为真实游戏中的可见传播、Unity 生命周期细节与停止时恢复尚未实测。

## 历史与冻结

本轮先确认旧 compile/tests 无运行进程，直接执行旧 tests 产物得到 35/35；随后在新 D 盘目录追加验证。r1 缺测试 using，r2/r3 暴露受控 fixture 曾将新增引用字段也写成 Color，修正为仅写对应 cache 并检查存储长度后，r4 40/40。失败日志保留，最终记录不覆盖历史。owned 源码冻结后由 root 继续模块与全仓集成；本报告不把该局部通过替代全仓通过。
