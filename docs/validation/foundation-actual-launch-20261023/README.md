# LOCAL 启动激活、首次转场与结果展示

2026-10-23。**1266/1266、25 套离线检查通过；新包已冻结暂存，未安装。** Runtime 136、Hooks 99、AudioContent 420。当前游戏安装仍为 [1021 四门包](../foundation-local-protection-host-20261022/README.md)。本批遵守用户“先继续代码，实测稍后”，没有启动、停止或操作游戏。

本批把分散的原生组件接成了可调用的生产启动协调器，并补齐受控激活、首次转场、初始化设置回执和宿主结果窗口。**Loader/模块尚未装配整条链，用户 `RequestPlay` 仍关闭；这份离线通过记录不代表 LOCAL 已能完整游玩。** 范围见 [scope](scope.md)，完整目标仍是 B01–B19，没有新增验收勾选。

## 本批接通的生产调用关系

1. [启动协调器](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)检查同一宿主的五门、边界空闲和无旧未退休局，创建完整 bundle，登记 pending session，并在原生尝试前预留既有四池责任。
2. 解析真实 prefab，捕获 globals 基线和 Awake 窗口，创建 inactive clone，分别交接 registry/factory 的 controller 根；实际参数 setter 的回执附着于同一个 launch state。
3. [受控激活](../../../src/RizToolbox.Adapter/AudioContent/NativeInactiveControllerActivation.cs)先核对五对象、参数、原场景和零次 Awake，再按禁用、脱离暂存父对象、单次启用的顺序操作。核对实际一次 Awake 及六个委托/标志的发布；未知失败保留责任，不重试。
4. [首次转场](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunSceneTransition.cs)在激活前捕获 origin 和独立 scene-manager 根，激活后调用一次精确 `StartNextScene("Level", 3f, null)`。正常 void 返回不算成功，必须取得真实新 AsyncOperation，随后观察 operation 完成、新 loaded Level、async 清空和 lock 解除。
5. 宿主帧泵等真实转场与 Unity 自己驱动的 Start/getter 回执。仅在实际初始化图、设置、所属 controller 和原生在途排空都通过后 commit；不手调第二次 Start，不制造 RunStarted。原场景卸载后，只允许已完成同局转场凭据解释暂存 parent/transform 的成对失效，持久 clone 三对象等核验仍保留。
6. [设置回执](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalInitializationSettingsReceipt.cs)比较 setter 前后五引用与速度/偏移原始位，读取游戏实际设置并验证 `MappedSpeed`、`offset / 1000f`。[结果 sink](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunResultSink.cs)接收同一 registry 的精确缓存结果，先发布 owner 再建窗口；按钮回调真正退出、控件和窗口释放成功后，才承认其 UI 消费者结束。

协调器、结果 sink 都有生产实现，但尚无 Loader 构造/调用点。sink 的 Retry/Return 是给后续命令实现的类型化回调，当前没有完整返回或重试命令。Ready 后协调器仍保留 process owner，没有把它伪装成已经退休。

## 离线验证与限界

相较 [1216 冻结批](../foundation-initialization-native-unwind-20261023/README.md)，新增 50 组检查：Runtime 5；AudioContent 45，分别为激活 10、设置 11、转场 9、结果 sink 10、commit pump 5。pump 用例验证生产共用 commit 逻辑及受控图/激活观察，没有执行整条 Begin 的游戏调用。

[完整日志](build.log)和[摘要](build-summary.json)记录 1266/1266。既有 native helper fixture 的 31261/61358、新 typed initialization helper 的 1747，以及 native-only ABI probe 的 18 次调用单独记账，没有加进 1266。完整 Runtime suite 运行了生产 sealed 初始化真实 Dobby fixture；本批没有再跑一个独立 native ABI runner，不能继承 1216 runner 为新包签发独立结果。全部这些 fixture 的 `gameRuntimeVerified` 仍为 false；Start/getter 的 original-unwind 证明不推广至既有 Play/Progress/Timeline managed 路径。

最终完整增量日志没有编译诊断行；这不等于干净重建零 warning。[Audio 转场定向构建](audio-scene-targeted-first.log)保留 15 条 warning：14 条共享 FakeNative CS0649、1 条结果 sink 测试 CS8602。没有为消除测试 warning 改动已冻结源码。

| 过程记录 | 结果及处理 |
|---|---|
| [首次 Audio](audio-targeted-first.log) | 激活测试泛型推断 CS0411，已修正后重建 |
| [第二次 Audio](audio-targeted-second.log) | 399/410，新增故障用例保留 unknown owner，耗尽同进程原有 32 配额 |
| [第三次 Audio](audio-targeted-third.log) | 四个新初始化故障用例改为隔离子测试进程后 409/410；最后一项误期待 false，实际 unknown 按契约抛错 |
| [第四次 Audio](audio-targeted-fourth.log) | 修正测试断言后 410/410；生产配额未增大，未加入 reset |
| [首轮 Runtime](runtime-text-targeted.log) / [补环境重跑](runtime-text-with-native-env.log) | 首轮 135/136 缺 native fixture 环境；补齐既有环境后 136/136，新字符串用例均通过 |
| [转场前完整批](build-before-scene.log) | 1253/1253，仅过程记录；最终证据以本目录 build.log 的 1266 为准 |

隔离子进程只执行指定的一个 Audio 回归用例，带 60 秒超时；没有隔离或操控游戏进程。unknown 原生责任的保留语义没有放宽。

## 冻结与一致性

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码 ZIP](source-inputs.zip) / [核对](source-archive-check.json) | 383 项，3,844,112 原始字节 | `2a4bb05f0e2f104975abe2476844d5d7963fbdc348b4c6519c2576b31cdee6ee` |
| [源码 manifest](source-manifest.json) | 383 项声明输入 | `d364900086bf5b229ad8ccc63cca0d4ecf08a08b7f168fd4864b48c3c9d7fc81` |
| [暂存包 ZIP](staged-package.zip) / [核对](package-archive-check.json) | 18 项，2,968,552 原始字节 | `8bd787924e7013ce2a2bb7267438e239c53c4d11e1c503f849b908751e4ca037` |
| [完整构建日志](build.log) | 25 suites | `ab2b729e680912a0ccdadddc97578d5ed254f59d1ee93d9a92102a507c60db54` |
| [转场静态证据 ZIP](scene-static-evidence-complete.zip) / [核对](scene-static-check.json) | 7 文件、7 原生范围、1895 指令 | `7739c428b5e062beb0342656c8394bd406a61e0e61848365c7dfbc84d03db18f` |

[changes-from-1216.json](changes-from-1216.json)与[差异](changes-from-1216.diff)比较两个冻结源码 ZIP，371→383 输入、24 个新增/修改文件，没有拿 Git HEAD 或旧安装源码冒充前批。[当前输入](source-integrity-check.json)、[18 项暂存](staging-check.json)、[旧 1021 安装及其 326 输入归档](previous-installation-check.json)和[原游戏三文件](original-game-check.json)分别核对匹配。源码 ZIP 只覆盖声明构建输入，不包含本轮说明文档、全部依赖或工具链，不构成 B19 干净复现验收。

[首次转场静态依据](../local-launch-scene-transition-20261023/README.md)由父代理重新读取实际 PE，逐项核对 7 范围/1895 指令；这是磁盘证据，没有调用游戏。目标 string slot 对应的恢复 metadata 条目不能独立解出文本，`Level` 的候选依据与限制在该记录中明示。初次归档与 complete ZIP 内容恰好相同，初次核对文件也保留；本批链接以 complete ZIP 为准。

## 接下来仍需实现

- 菜单相机/粒子的条件交接和恢复，TouchBlock/BackLock、转场回调的完整拥有关系与平衡。
- 活动局原生消费者真正结束后的 graph、globals、controller、bundle 及协调器退休，成功后才允许下一次事务。
- 完整 Retry/Return、返回目标与选择恢复、新局重新建图；将长寿命 UI、结果 sink、handler、bridge、五门和 coordinator 装配进 Loader/模块。
- 在用户恢复实测安排后，另用新包验证完整流程；当前离线包不能继承旧包的游戏验收。

在仓库根目录核对本批已冻结输入和包，不构建或安装：

```powershell
& C:\Python314\python.exe docs/validation/foundation-actual-launch-20261023/freeze-source.py
& C:\Python314\python.exe docs/validation/foundation-actual-launch-20261023/freeze-package.py
& C:\Python314\python.exe docs/validation/foundation-actual-launch-20261023/summarize-build.py --staged
& C:\Python314\python.exe docs/validation/foundation-actual-launch-20261023/verify-scene-evidence.py
```

审查范围见 [有限交叉审查](independent-review.md)；Evidence→Finding→Path 及时间线见 [正式报告](2026-10-23_底层-启动激活与首次转场-report.md)。
