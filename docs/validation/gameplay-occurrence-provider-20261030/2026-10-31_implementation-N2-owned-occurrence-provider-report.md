# N2 owned occurrence/source/writer provider 离线实现报告

本包交付具体 native owned producer→私有凭据→实际 accessor→`INativePlaybackRateProvider`→正式 Backend/Service 的可执行路径。Pause 用实际 post-Pause source time 同步 Clock；Replay/Resume 在 native producer 内捕获真实 `AudioSource.PlayScheduled(double)` 调用参数及正常 Returned，实际读回 stock pitch=1 后恢复选中 pitch，再与 DSP、media、origin 独立读回交叉验证。native pitch/source 序列在实际 setter 前推进，同值 owned 外写、owned ABA 和同 clip setter 均使旧私有票失效。2026-10-31 又修复了独立审查发现的 worker Dispose 根泄漏，受控 provider source 与 direct accessor 两层均在状态变更前拒绝错线程清理；owner-thread 重试验证七个根恰好释放一次。

`flavor=null`。授权、offline 范围及未修改的其他 owner 文件见 [scope.md](scope.md)，历史与失败目录见 [timeline.md](timeline.md)。本包没有真实 stock hook、直接 Runtime/Unity 全 writer 覆盖或 production GetTime authority；真实 Speed/Seek 默认继续关闭。未安装/启动游戏，未操作 UI、账号或 B 账号，独立 fixture 不加入发行。

最终新目录 `D:/riz-build-cache-next/gameplay-occurrence-provider-r4-20261031/` 的 native build/managed build/tests actual exit 为 **0/0/0**，**175/175**（既有 146 + 本包 29）且零警告/错误。688 个完整 frozen 输入与八个明确 owned overlay 构建前后 hash 均相同，独立 seal 随后再次核对全部 688 项及实际 native PE。source zip SHA-256 为 `04f1af6ff9ddb40105c1761a710cbb1b81ffbd791aab2ccbd5a49c1fb3016aed`；actual owned DLL SHA-256 为 `88bacc51b2c759c3c650a87a8a10f73f2ae38f37e553a18203037aed2737b7a8`。精确清单与小型同字节副本见 [evidence-index.json](evidence-index.json) 和 [test-result.json](evidence/test-result.json)。

## 实际实现及 producer 边界

`NativePlaybackActualSource` 独立持有 controller/information/progress/pause/player/audioSource/clip 七个根。每次访问核完整图、根目标/精确 class、控制器/source 活性、clip 和 GetTime MethodInfo；实际读取 DspTimeService、source time/pitch/playing、clip length、progress/offset/chart 与 stock nullable origin。nullable tag/value 仅按已有固定证据的 player+0x18/+0x20 投影，不给 `NativeRuntime` 增加任意 nullable 或 struct ABI。

`PlaybackOwnedFixture.cpp` 是独立 DLL，模拟固定 stock ordering，并通过 managed callback 调用上述实际 accessor 的 `NativeRuntime` 音频/DSP方法。native 内的 epoch/source/occurrence/pitch 序列、Active 与 Returned 由该 producer 自己维护；managed `Fence` 构造函数私有，发行方法自己实际调用或读取 native export，没有接收 caller record/bool 的 sourcefact 入口。`PlayScheduled` witness 记录的 double 位与实际 callback 参数是同一值，stock post-origin 只做交叉核验；Resume 的 post-origin=`actualStart-actualMedia`，不会倒推为实际调用参数。

`NativePlaybackOwnedCallProducer` 消费这些私有凭据。新的 occurrence 在进入 stock stub 前使旧 paused source fence 失效，任何异常、缺失/跳过 schedule、reset/readback 不符、图变更、DSP 回退或 prepared clock 竞争均撤回并保留未知同步责任。正常 schedule 完成后，用独立 native Before 票执行实际 pitch 恢复，读回 pitch/media/origin，再调用既有 `Clock.ObserveScheduled`。Pause 完成后用实际 source time 调用既有 `ObservePause`，该完成凭据才允许 provider Capture 当前 paused source。

pitch 写票持有 native Before 的确切 attempt sequence，即使实际 setter 改值后抛出，仍可由原票验证和条件恢复。重入 setter 产生的后继 sequence 不会被误认成本票。source setter 在调用前推进独立 source fence；同 clip setter 也撤回原 source authority。

这些是 **owned native calls 的完整受控覆盖**。裸 `NativeRuntime` 或真实 Unity/game writer 绕过 owned lane 时，同值/ABA 无法被本 observer 看见；专门边界用例实际执行了这类写入并保留结果。两个 sealed 类只提供 `CreateControlledForTests` / `AdoptControlledForTests`，没有 production=true 或 caller completeness 开关。Probe 明确记录 controlled-only 覆盖，默认真实模块不装配本 provider，不能据此签生产支持。

## worker Dispose 修复

旧 `Source.Dispose` 与 `NativePlaybackActualSource.Dispose` 先将 `_disposed` 置位。worker 上 native root 的 owner-thread 检查抛出后，旧 `ReleaseRoots` 仍清空根列表，主线程再试时因 disposed 提前返回，七个未释放的根永久失去责任。这是真实实现缺口，不是 fixture 边界。

修复让 provider Source 在任何 disposed 变更前调用 owner 的线程检查；actual accessor 自己保存构造线程，并在 `Open`、`Dispose` 和 `ReleaseRoots` 开始处检查，保证直接 accessor 调用也不能丢根。两项实际 worker 回归都验证：WrongThread、原七根数量与 `FreeHandleAttempts/FreedRoots` 均不变、主线程 authority/graph 仍可读、主线程 Dispose 恰好新增七次 free、重复 Dispose 无新增 free。r3 原目录保留，仅新 r4 结果验证修复源码。

## Evidence → Finding → Path

Evidence 的精确路径、SHA-256、命令、实际退出码和 PE 导入/导出见 [evidence-index.json](evidence-index.json)。小型同字节副本在 [evidence](evidence)；完整 source zip、CPP/OBJ/PE/PDB 原件在 D 盘冻结目录。

| Evidence | source_ref / repro_command | 观察及完整性 |
|---|---|---|
| E01：固定语义证据 | `D:/riz-build-cache-next/gameplay-native-values-static-r1-20261030/evidence-r3/{manifest,metadata}.json` 与 Pause/Replay/Resume、Dsp、PlayScheduled、pitch、Progress ASM；`Get-FileHash -Algorithm SHA256 -LiteralPath <source_ref>` | 只读复用固定 GameAssembly `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；各文件 hash 在 index，不重跑逆向或游戏 |
| E02：修复后输入与执行 | r4 `source-before.json`、`source-after.json`、`source-snapshot.zip`、`test-result.json`、native/managed/tests stdout/stderr；复现命令见下节 | 新 D 目录，保存全部实际输入、逐 TU flags、actual exit 和前后 hash；r3 不作为修复验收 |
| E03：独立 owned native PE | r4 `native/RizToolbox.PlaybackOwnedFixture.dll`、CPP/OBJ/PDB/`build-result.json`；`seal-evidence.py --run <r4>` | 实际 AMD64 PE32+，六个明确 owned exports；PE hash、完整导入/导出与工具产物 hash 在 index |
| E04：真实受控正向与边界 | r4 `tests.stdout.log` 中 `OWNED-SCHEDULE`、`OWNED-BOUNDARY`；同冻结 `NativePlaybackOccurrenceProviderChecks.cs` | Replay 100.375、Resume 100.25，读回 rate=2；naked writer coverage=false、productionAdmission=false 明确输出 |
| E05：worker Dispose 回归 | r4 `NativePlaybackActualSource.cs`、provider 与两项 worker Dispose 检查；同 r4 测试命令 | actual worker 拒绝、主线程可重试、各七个根恰好释放一次；源 hash 在输入清单 |

| Finding | severity / category / confidence / status | evidence_ids / location / impact |
|---|---|---|
| F01：私有 native occurrence 与 actual schedule 参数已可驱动现有 Clock | n/a_re / other / high / validated-controlled | E01/E02/E03/E04；`Run`、`Scheduled`、`NativePlaybackOwnedCallProducer.Run`。通过 owned native actual callback 获得实参及 Returned，缺失或异常时不发布同步 |
| F02：private writer/source fence 拒绝 equal/ABA 后继且能恢复原 attempt | n/a_re / other / high / validated-controlled | E02/E03/E04；native `Pitch`、`SetClip`、`Fence.WriteActual`、private `PitchWrite`。实际 setter 前记 native Before，旧票不覆盖后继 |
| F03：concrete provider 经正式 Backend/Service 消费已有 participants | n/a_re / other / high / validated-controlled | E02/E04；`NativePlaybackOccurrenceRateProvider` 与 service consumption/partial setter 回归。实际 pitch→Clock→审计路径闭合，原异常/恢复/unknown 分开 |
| F04：worker Dispose 不再丢失独立七根责任 | n/a_re / other / high / validated-controlled | E02/E05；`Source.Dispose`、`NativePlaybackActualSource.Dispose/ReleaseRoots`。错线程任何状态变更前拒绝，owner retry exactly-once；修复旧 r3 缺口 |
| F05：完整真实 native producer 与生产采用仍未成立 | n/a_re / other / high / validated-boundary | E01/E02/E04；factory/Probe、`NakedWriterBoundary`、`DefaultBoundary`。真实 stock hooks、直接外 writer 和 return authority 缺失，真实 Speed/Seek 仍 false |

P01，`path_type=callflow`：当前 private paused occurrence/source → concrete provider Capture 独立七根 → 正式 Backend 源/run/clock 再验 → native private Before→actual `runtime_invoke set_pitch`→实际读回→既有 Clock participant→正式 Service/audit。依据 E02/E03/E04，关联 F02/F03。残余风险为 F05 的生产完整来源。

P02，`path_type=callflow`：native owned Replay/Resume occurrence Before → actual Stop/time/Dsp → stock pitch=1 的 native Before/Returned → native 同一 double 的 `PlayScheduled` 实参/Returned → actual DSP/media/pitch/origin → private selected-pitch 恢复 attempt/readback → 既有 Clock.ObserveScheduled。依据 E01/E02/E03/E04，关联 F01/F02；异常或跳过路径撤回，不能借 stock origin 补事实。

P03，`path_type=callflow`：worker Source/direct accessor Dispose → owner-thread 检查拒绝且状态/七根/free-attempt 不变 → 主线程 authority/graph 验证 → 主线程清理七根 → 二次 Dispose 无额外 free。依据 E02/E05，关联 F04；仅新修复源码冻结提供此结论。

## 复现及验证口径

本机使用已安装 Python 3.14、pefile、MSVC 2019 BuildTools、PowerShell 7 与 .NET SDK 8.0.418。native builder 使用 `/W4 /WX /O2 /Z7 /std:c++17 /EHsc /LD`，只编译新增独立 CPP，不更改或链接正式 helper。NuGet 来源清空；Program 只在本独立 snapshot 注册新 All，live Program 注册由根任务负责。688 个 snapshot 输入依赖既有全树 core 冻结及八个明确 overlay，本包不证明同期其他 owner 的最新 live 改动。

从仓库根用 fresh 不存在的 D 盘目录复现完整冻结：

```powershell
& C:/Python314/python.exe docs/validation/gameplay-occurrence-provider-20261030/run-checks.py --output D:/riz-build-cache-next/gameplay-occurrence-provider-replay-20261031
& C:/Python314/python.exe docs/validation/gameplay-occurrence-provider-20261030/seal-evidence.py --run D:/riz-build-cache-next/gameplay-occurrence-provider-r4-20261031
```

仅重建独立测试 DLL：

```powershell
& (Get-Command pwsh).Source -NoProfile -File scripts/Build-PlaybackOwnedFixture.ps1 -OutputDirectory D:/riz-build-cache-next/playback-owned-fixture-replay-20261031
```

运行标准 Gameplay 测试时必须将 `RIZ_PLAYBACK_OWNED_FIXTURE` 指向这个独立 DLL；缺失时新 All 明确报错，不静默跳过。完整固定 r4 的命令、退出码、输入/输出 hash 以 index 和 `test-result.json` 为准。r1 runner 路径失败、r2 170 和 r3 173 的旧目录均未覆盖或删除。
