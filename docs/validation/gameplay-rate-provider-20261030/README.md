# N2 正式原生玩法 backend 的速率 provider 采用

2026-10-30，`flavor=null`。本批把已有的实际 pitch→独立媒体时钟事务接入 `GameplayModule`、`GameplayNativeBackend` 和正式 `GameplayService`。离线受控 provider 经真正的 NativeRuntime 元数据、根、字段读数和 `runtime_invoke` 音频 setter 完成了正向事务、恢复及未知副作用审计。新增 15 个行为场景与原 131 个 Gameplay 检查共 **146/146** 通过，实际 build/test exit 为 **0/0**，0 warning/error。

本批没有安装真实 provider，也未安装或启动游戏、操作账号、运行 native helper。默认 `RizlineAdapter` 不传 provider，原生 Speed/Seek 均继续关闭。完整原生 schedule occurrence、连续 pitch-writer observer、生产返回 authority 的联合来源仍是下一依赖；Seek 的完整重建与恢复仍未实现。范围与授权见 [scope.md](scope.md)，精确路径/hash 见 [evidence-index.json](evidence-index.json)。

## 已交付的正式控制路径

`GameplayModule` 提供可选内部 `INativePlaybackRateProvider` 装配口并透传到 `GameplayNativeBackend`。provider 没有 `production=true` 或同类准入开关。其 Probe 只描述固定能力；每次读取都会反映 transport 撤回，不能缓存为某个 run 的就绪事实。

Backend 的 SetSpeed 可用性同时要求自己所需的字段与方法元数据完整：原控制前置字段、Progress→LevelInformation 反向图和控制器活性。provider 支持不能遮住这些缺失。Seek 仍独立拒绝，provider 只能开放 `PlaybackRateMultiplier` 单位。

正式服务采集当前局、独占租约和请求 run/generation 后，Backend 的 Capture.Prepare 将实际已独立 root 的 controller、information、progress、pause 图交给 provider。返回的 source/clock 必须匹配该图、请求 run/generation 及自身完整绑定。每次准备、写入前后、确认、恢复及读回均重新验证捕获图、活性、source、clock 对象、完整 binding 和 provider authority；sample 也须持有同一 binding。

通过核验的 source 直接进入既有 `NativePlaybackRateParticipants`，先实际写入和读回 AudioSource pitch，再发布独立时钟。所有参与者仍使用 `GameplayPreparedValueControl` 与正式服务的审计及条件恢复。一次准备资源只能释放一次；getter/捕获/准备失败也纳入清理。修复了 Plan.Capture 清理中一个 Dispose 抛出后阻断其余独立资源的问题，保留原准备异常。

provider 必须自行持有并持续验证实际 scene/run/source/roots、精确 GetTime return authority、所有 stock Pause/Replay/Resume occurrence 和全部 pitch writer。它还须处理从服务之外发生的 stock 调度与强制 pitch=1，或撤回并保留未知副作用责任。本批接口与受控实现不能取代这些尚未提供的原生事实。

## Evidence → Finding → Path

| Evidence | source_ref / repro_command | content_hash |
|---|---|---|
| E01：正式 Backend/Service/provider 源码 | r2 `source-before.json` 与 `source-snapshot.zip`；对其中精确文件执行 `Get-FileHash -Algorithm SHA256` | manifest `c8faf11a987fdb0dd5e36889c5ec1b2cc96c6b66a59afbfca436b7501341efe0`；zip `d77c0ebe6f7a1aa84ba9a8286f2b31f0d5eb2823338c0396731f111dc2bbcbee` |
| E02：真实离线编译与行为运行 | r2 `build.log`、`tests.log`、`test-result.json`；复现命令见下节 | 各原件完整 hash 见 index |
| E03：默认关闭及限定来源 | r2 `NativePlaybackProviderChecks.cs`、`FakeNative.Gameplay.cs`、`NativePlaybackRateProvider.cs`；测试 `DefaultUnsupported`、`ServiceAdoption`、`MetadataGate`、`DynamicCapability` | 文件 hash 见 r2 manifest 与 index |
| E04：当前 Module 装配源码 | `src/RizToolbox.Adapter/Modules/GameplayModule.cs`；本批 metadata sidecar 独立冻结该文件 | hash 见 index；**未编入 r2 隔离构建** |

| Finding | severity / confidence / status | evidence_ids / location |
|---|---|---|
| F01：有 authority 的受控 source 已可经真正 Backend 和正式服务消费现有实际 participants | n/a_re / high / validated-controlled | E01/E02/E03；`GameplayNativeBackend.CaptureValue.Prepare`、`NativePlaybackRateProviderAdoption` |
| F02：provider 撤回即时关闭可用性，单局捕获失败不伪造 metadata 不支持或已发生修改 | n/a_re / high / validated-controlled | E01/E02/E03；`GameplayService.Operations`、`GameplayNativeBackend.CurrentProbe` |
| F03：变更后的图/source/clock 不可确认或覆盖后继；失败恢复与未知状态分开记录 | n/a_re / high / validated-controlled | E01/E02；新增 `ChangedGraph`、`ChangedSource`、`SetterRecovery`、`StopDuringSetter` |
| F04：原生完整 producer 与生产采用仍未成立，默认 Speed/Seek 继续关闭 | n/a_re / high / validated-source-boundary | E01/E03/E04；`GameplayModule`、默认 `RizlineAdapter` 装配及 provider 合同 |

P01（`path_type=callflow`）：正式 `PluginHost` 或 service view → 当前局与独占 lease → Backend 新鲜 Capture/Prepare → 精确 rooted origin → provider source/clock/binding 复核 → actual pitch `runtime_invoke` / readback → 独立 clock participant → 正式服务确认与共享 audit。关联 E01/E02/E03、F01/F02。

P02（`path_type=callflow`）：实际 pitch setter 已改变并抛出 → 逆序尝试 clock 与 audio 恢复 → 各自独立 baseline readback → 已恢复则记录 Restored；若 graph/source/clock 已换代则拒绝旧票写入并记录 SideEffectsUnknown、撤回本 generation 的该动作。关联 E01/E02、F03。残余依赖为 F04，不能把受控结果提升为真实游戏支持。

## 复现与冻结边界

最终目录：`D:/riz-build-cache-next/gameplay-rate-provider-r2-20261030/`。405 个编译输入、13 个明确 live overlay 均在运行后再次核 hash 不变。r2 基于既有 [native-clock r4](../gameplay-native-clock-20261030/README.md) 冻结依赖；本轮没有修改 Publisher、Runtime Single-return bridge、AudioContent 或 snapshot scripts。

r2 只验证真正 Backend/Service/provider 及其依赖。当前 `GameplayModule` 的三处透传变更独立冻结在 metadata sidecar 中，交由当前全树构建验证；r2 基础的旧 `RizlineAdapter` 仍为旧 4 参 Module 装配，不能混编新 Module 后声称是当前整树。r1 保留了这条明确的 CS7036 装配诊断和失败摘要，未把它当作通过。r2 不加入兼容重载，不降低新 Module 的精确局匹配要求。

直接重现冻结输入：

```powershell
$case = 'D:/riz-build-cache-next/gameplay-rate-provider-r2-20261030'
& 'C:/Program Files/dotnet/dotnet.exe' build "$case/source-snapshot/tests/RizToolbox.Gameplay.Tests/RizToolbox.Gameplay.Tests.csproj" -c Release -m:1 --configfile "$case/NuGet.Config" -p:UseSharedCompilation=false --nologo
& 'C:/Program Files/dotnet/dotnet.exe' "$case/source-snapshot/tests/RizToolbox.Gameplay.Tests/bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll"
```

工具：Windows PowerShell 5.1.26100.9711、`.NET SDK 8.0.418`；NuGet 来源清空，复用本机已安装 framework packs，不访问网络。runner 原件和来源冻结见 index。`FakeNative` 四个通用基文件通过 csproj Link 复用，未复制分叉；仅新的 Gameplay partial 添加本批控制图和实际 setter。受控 native exports 使用自有内存、委托和元数据，不证明真实游戏 ABI、音频可闻输出或 GetTime detour 的机器行为。

Timeline：读取 N2 时钟/事务及原生依赖边界 → 补正式 Module/Backend/provider 与动态支持 → 加完整 Backend 实际导出 fixture 和 15 场景 → r1 保留旧基础装配失败 → r2 明确隔离 Module，146/146 与来源 hash 冻结通过 → 交根任务做当前整树构建。
