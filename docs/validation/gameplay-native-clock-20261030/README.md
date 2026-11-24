# N2 媒体时钟及速率参与者离线报告

2026-10-30，普通逆向分析，`flavor=null`。本批实现了可独立使用的媒体秒时钟、准确的 `Single()` 返回边界、两个实际写入/读回的速率参与者，以及同一修订的原生策略发布 seam。通用事务执行器由根任务实现，本批新增执行器测试与正式 GameplayService 的成功、恢复、未知副作用审计。实际游戏没有启动或安装，账号和凭据没有操作。真实原生 Speed/Seek 的支持标记继续为 false。

范围与授权见 [scope.md](scope.md)。最终归档目录为 `D:/riz-build-cache-next/gameplay-native-clock-r4-20261030/`，精确文件/hash、工具和重现入口见 [evidence-index.json](evidence-index.json)。该目录采用 r8 冻结基础和 8 个明确 Gameplay overlay；它不包含同时在建的 Runtime return bridge 或根任务的新 publisher 实现，不能当作当前全树验证。

## Evidence → Finding → Path

| Evidence | 来源与复现 | 完整性 |
|---|---|---|
| E01 | 既有 `gameplay-native-values-static-r1-20261030/evidence-r3/{manifest,metadata,functions}.json` 及 7 个定向 ASM；可用 `Get-Content -LiteralPath <source_ref>` 复核 | 每个原件的 SHA-256 在 evidence-index 中；固定 GameAssembly SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` |
| E02 | r4 `build.log`、`tests.log`、`test-result.json` | index 记录完整 SHA-256；实际编译和运行退出码分别为 0/0 |
| E03 | r4 `source-before.json`、`source-snapshot.zip`、8 个 overlay 清单 | 原冻结源码及 overlay 在构建后再次核 hash；index 记录归档完整 SHA-256 |

| Finding | severity / confidence / status | location / evidence |
|---|---|---|
| F01：stock raw GetTime 不能再直接乘 rate | n/a_re / high / validated-static | `MUGMusicPlayer.GetTime@3FBB10`；E01 |
| F02：Replay/Resume 会主动强制 pitch=1，必须作为同步事务边界 | n/a_re / high / validated-static | `ReplayScheduled@3FBE30`、`ResumeScheduled@3FC060`；E01 |
| F03：已证明的 null MethodInfo 消费者必须按固定调用点授权 | n/a_re / high / validated-static | `Progress.Update@400FE0`：`401216 xor edx,edx`，`401218 CALL`，return=`40121D`；E01 |
| F04：本批低层时钟/参与者和事务审计在受控离线环境可闭合 | n/a_re / high / validated-controlled | 新 `NativePlayback*`、通用执行器与 GameplayService；E02/E03 |
| F05：任意 Seek 仍缺完整重建及消费者退出路径 | n/a_re / high / candidate-unimplemented | Judge/CheckHold/Hold 的独立列表、协程及活跃音符状态；E01 |

P01（`path_type=callflow`）：精确 run/source graph → 全体只读捕获与基线/authority 核验 → AudioSource pitch 私有 writer 票写入 → 实际 pitch 读回 → Clock 同修订 native policy CAS/读回 → 托管时钟发布 → 有值确认及共享审计（F04）。任一步失败逆序尝试所有已进入的参与者；恢复与独立 baseline 读回分开。

P02（`path_type=callflow`）：精确原 `GetTime` 正常完成 → 保留原始 float bits 与真实 incoming MethodInfo/return PC → 完整 binding 匹配 → 有证的 fixed null-MI caller 或 exact nonnull MI → 独立 DspTimeService 锚点媒体秒 → finite Single 返回（F01/F03）。异常原调用、错对象、错代次或未授权 null caller 均不变造原返回。

## 固定消费语义

`GetTime` 的播放分支使用 `DspTimeService - startPlayTime`，没有乘速率。它与 `AudioSource.time` 的差超过 ±`0.10000000149011612` 秒时会重新写 origin 并回退到 source time。后者随 pitch 前进，故 `(stockRaw-anchor)*rate` 会在回退后重复乘速率。本批采用 `mediaAnchor + (dspNow-dspAnchor)*rate`；暂停固定实际 source time，clip 末端限制上界，非有限或 DSP 回退撤回。Scheduled 的 DSP anchor 就是实际 start DSP；start 前保留原版不缩放的预卷，start 后使用 rate。

`Progress.Update` 用 `GetTime - _offset` 更新 `_nowTime`，再写 `nowRealTime`、`nowSyncedTime`，并通过 `RealTimeToChartTime` 写 `nowTime`。本批只声明这条已证明的直接消费者。Judge.Update 从 `LevelInformation.nowSyncedTime(+B8)` 写 `_nowTime(+64)`，CheckHold 维护自身协程状态与 hold 的 real/end 时间，Hold.Update 从 `nowTime(+B0)` 和 `speed(+98)` 取几何输入；这些静态间接消费者证据没有被改写成“全部判定路径已接新时钟”。

Replay 先 Stop，再 origin=`DSP+delay`，pitch=1，PlayScheduled。Resume 保存 source time、Stop、set_time，origin=`DSP+delay`、pitch=1、PlayScheduled，再 origin 减去读回的 source time。新 clock 的 `ObserveScheduled` 只接收原生调度完成且所选 pitch 已实际恢复/读回后的事实；它本身不调用或伪造原生调度。

返回 ABI 为 Windows x64 instance `float(self, MethodInfo*)`：RCX=self，RDX=incoming MethodInfo，XMM0 low32=返回。它与现 `void(float)` Single 事件 ABI 不同。fixed Progress caller 真实传入 RDX=0，必须原样交 original；解析出的非空 MethodInfo 仍是元数据身份，不冒充 incoming 值。`NativePlaybackGetTimeCallProfile` 在安装 hook 前核旧 profile，回调时重验固定 `Progress.Update` 正文。被 detour 改写的 GetTime 入口、原跳板及在途责任由另一路 Runtime broker 负责。

## 实现与验证

- `NativePlaybackClock`：run/scene/controller/source generation、controller/info/progress/player/source/clip/metadata MI 全绑定；单调修订和私有 rate change；暂停/播放/预卷、原生 policy CAS/实际读回、条件恢复和撤回。
- `NativePlaybackGetTimeBridge`：保原 float bits，区分原异常与正常完成，固定 null-MI caller，有限媒体秒替换。原生 authority producer 尚未由本批提供。
- `NativePlaybackRateParticipants`：read-only 捕获暂停态 source、progress、offset、nullable origin、调度及 authority revision；实际 pitch 先写/读回，再发布 clock。`INativePlaybackPitchWrite` 必须连续观察所有 writer；同值外写和 2→7→2 ABA 都使旧票失效。setter 写后抛仍保留 attempt 责任。
- `GameplayPreparedValueControl` 的测试直接调用生产执行器，不复制另一套调度器；覆盖 all-prepare、全基线前置、逆序恢复、失败继续、原异常、单位/数值确认、origin 失效、回调 Dispose、once-only 及正式服务审计。

最终 r4 为 **131/131** Gameplay 检查（原 79 + 执行器 17 + NativePlayback 35），0 warning/error，实际 build/test exit 0/0。包括成功 rate→scheduled clock、部分 setter 恢复、pitch ABA 拒绝、native publish 写后失败、错误读回、撤回、恢复失败及 audio 独立恢复。没有游戏/native helper 执行或可闻输出证明。

历史保留：r1 为 122/122；r2 只因新增 service 测试的 target-typed `new` 遇到 Execute 两个重载而编译失败，随后显式写 `GameplayControlRequest`；r3 实际 build/test 均 0 且 131/131，但 runner 的摘要 regex 没接受 CRLF，`passed=false` 原件保留。r4 修 runner 后重新冻结，不修改前述原件。

## 仍需完成的原生路径

1. 游戏原生 `INativePlaybackRateAccess`：精确独立根、same run/scene/source、完整 schedule occurrence fence、实际 DSP/media/progress/offset/origin、连续 pitch writer/source observer。没有这些事实不能签私有 writer 票。
2. Runtime exact Single-return authority 与原生策略 publisher 的联合编译/机器验证，随后把本批同 revision seam 接入正式模块。fallback 回 stock 只保原调用语义；在 audio 仍为改动 rate 时，它不是同步成功，必须撤回/停止并保 unknown 责任。
3. 当前只读 `GameplayClockNativeProfile.VerifyCurrent` 仍包含原 GetTime 正文 hash；hook 安装后该只读 profile 会明确失效。生产完整时钟/转换服务需要由 broker 证明 detour、原跳板及原正文的专用 profile，不能简单跳过校验。
4. Seek 还需实际 audio schedule/position、note active/destroyed/cursor、Judge 等待列表/计数、在途 CheckHold 协程、hold renderer/line 状态、结果/挑战计时及 input 的完整准备/重建/恢复。只 set_time 或清一张列表不能实现任意定位。

复现受控冻结：对 r4 的 `source-snapshot/tests/RizToolbox.Gameplay.Tests/RizToolbox.Gameplay.Tests.csproj` 执行 `dotnet build -c Release --configfile <r4>/NuGet.Config -p:UseSharedCompilation=false`，再用 dotnet 运行对应 `bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll`。源 snapshot zip 和 index 可独立核对；本批不是当前全树、发行候选或真实游戏验收。

Timeline：复核既有 r3 消费证据 → 明确 DSP 锚与 instance Single() → 补 fixed null-MI caller → 实现时钟/参与者及执行器测试 → 补连续 writer ABA 票 → 122 首轮 → 同 revision publisher seam及正式服务审计 → 131 最终冻结。
