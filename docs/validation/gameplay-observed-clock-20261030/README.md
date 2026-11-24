# Gameplay observed-clock 冻结验证

2026-10-30 的 `r8` 冻结候选已完成独立编译与 **79/79** 受控 Gameplay 检查，编译为 **0 warning / 0 error**。其中 19 组专门验证 observed-clock 与实际 run 关联；整个 Gameplay 测试集已移除基于 Sleep 的时间等待，使用可注入单调时钟表达捕获耗时与失效边界。

本次证据只证明托管服务及受控后端行为。未启动真实游戏、未操作 UI、未执行 native fixture、未安装；`RuntimeVerified=false`，音频延迟未知，真实 SetSpeed/Seek 仍为 Unsupported。功能使用与 API 语义见 [Observed clocks](../../Gameplay-observed-clocks.md)。

## 冻结源边界

构建来源为 `D:/riz-build-cache-next/foundation-single-v7-core-r1-20261029/source-snapshot` 的稳定基线，加下列 15 个 owned overlay 文件。没有把其他 agent 正在编辑的 root 模块、Appearance、Single、native 或打包文件从共享工作树冻结进来。

| 输入来源 | 文件数量 | 范围 |
|---|---:|---|
| 稳定基线 | 376 | SDK/Core/Runtime/Adapter 的其他源码与四个项目文件，以及 Directory.Build.props、global.json、dependencies.lock.json |
| owned overlay | 9 | `src/RizToolbox.Adapter/Gameplay/*.cs` |
| owned overlay | 2 | `src/RizToolbox.Sdk/GameplayContracts.cs`、`GameplayClockObservationContracts.cs` |
| owned overlay | 4 | `tests/RizToolbox.Gameplay.Tests` 的三个 C# 文件及项目文件 |

共 391 个输入。每个文件的输入路径、来源、相对路径、实际 snapshot 路径、SHA-256 及字节数均在 [source-before.json](evidence/source-before.json) 中。编译前已经保存源码 zip，并对 snapshot 源文件持只读共享锁；编译及检查后复核全部 input、snapshot 与 runner 哈希。`inputHashesUnchanged`、`ownedSourceHashesUnchanged`、`frozenSourceHashesUnchanged` 均为 true。

`r8` 原始证据目录为 `D:/riz-build-cache-next/gameplay-observed-clock-r8-20261030`。这里保存了同字节的便携证据副本：

| 证据 | 作用 |
|---|---|
| [test-result.json](evidence/test-result.json) | passed=true、checkCount=79、实际可执行文件/参数/exit code、所有输入与产物哈希 |
| [source-snapshot.zip](evidence/source-snapshot.zip) | 实际参与本次编译的 391 个源输入，不包含旧 obj/bin |
| [build.log](evidence/build.log) | 原始 dotnet build 输出；exit code 0，0 warning/0 error |
| [tests.log](evidence/tests.log) | 实际测试子进程输出；exit code 0，79/79 |
| [Freeze-ObservedClock.ps1](Freeze-ObservedClock.ps1) | 可重新从稳定基线与当前 owned overlay 生成新冻结目录的 runner |

源码 zip SHA-256：`fd758fd07111e08d990d33633c041ae87323cba5a949d88e24a91c2c980294c9`。runner SHA-256：`76672e3dbb460b5eb4077b9470d65bffb9afef46b635209b9b8cdab6a19818f9`。

## 已验证的行为

| 证据组 | 结论 | 实现路径 |
|---|---|---|
| Warmup、Conversions、LatestWindow | 三次连续真实 Read 后形成窗口；以非 1x 的实测速率双向换算三个秒域，只允许最新端点内插，旧 snapshot 不被修改 | `GameplayClockObservation.Observe/Convert` |
| RequestBounds、Expiry、Policy | 非法域、非有限值、越界及非法年龄在适当边界拒绝；request 仅缩短策略寿命；大 MaxAge 不能复活旧段 | `GameplayService.ConvertObserved`、`GameplayClockObservation.Inspect` |
| PauseResume、Identity、Discontinuities、Jump、HostFailure | 局/任一控制器替换、暂停恢复、缺失/倒退/间隔过大/host 非有限及残差突变使旧段失效；不能把跳变记成真实控制 | `GameplayService.Capture`、`GameplayClockObservation.Observe` |
| MissingSamples、Residual | 缺失、错误单位及 NaN/Infinity 不构成观测模型，仍保留其他独立源；小速率变化保留实际残差与估计不确定度 | `GameplayService.Read`、`GameplayClockObservation.Observe` |
| FreshValidation | warm 后不再 Read 即改变 offset/delay、fingerprint/source、progress/chart/pause 身份、进度倒退/跳变或进度单位/可用性时立即拒绝并要求重新 warm；不读 DSP、不增加 history | `GameplayService.ConvertObserved`、`GameplayCapture.ReadObservedClockState`、`GameplayClockObservation.Convert` |
| RunBinding、RunBindingBoundaries、RunBindingPublication | 新 controller/info 不得继承旧 sampler RunId；sampler 追上后同局恢复；read/preparation/dispatch/publication 处关联失效或抛异常不能发出错误关联 | `GameplayService.Capture/SameCapture`、`GameplayNativeBackend.CaptureValue.CurrentGraphMatches` |
| Reentry、Guards | 转换不补采 game/DSP，主线程/取消/作用域与同步重入守卫保持生效；getter 内切局不能发布旧模型 | `GameplayService.ConvertObserved` |
| 原有 60 组 Gameplay 检查 | chart/progress 原转换、独立读数、控制租约与有值事务受控行为通过；真实后端 Unsupported 没有提升为 Supported | `Gameplay.Tests/Program.cs`、`ValueTransactions.cs` |

此次继续已有实现时修复了 mapping 读取异常被 nullable playing/paused 比较误清除、未知采样状态误报目标过期、两次 Read 之间换源/修改参数仍接受旧模型、以及新 native 图误继承旧 sampler RunId 等实际边界。native 当前性同时核验 controller→information、information→progress 与 progress→information，观测来源身份另含 chart/pause；未取得可选 root 时不引入额外字段读取依赖。

这里的稳定基线不包含其他 agent 的 root 接线修改；受控用例向服务构造函数注入 `matchesCurrentGameplayRun`，证明 gate 的失配和正向恢复。生产 `GameplayModule/RizlineAdapter` 传入 `GameEventState.MatchesCurrentGameplayRun` 的接线属于 root 的整树冻结验证范围，本页不声称已单独覆盖它。

## 复现冻结源码

以下 PowerShell 命令使用这里的 zip 重新编译，输出到一个全新目录。依赖为本机 .NET SDK 8（global.json 允许 latestFeature）；不需要游戏、UI 或 native fixture。必须保留新的输出目录名，避免混入其他构建产物。

```powershell
$recheck = 'D:/riz-build-cache-next/gameplay-observed-clock-frozen-recheck-20261030'
if (Test-Path -LiteralPath $recheck) { throw 'Use a fresh output directory.' }
Expand-Archive -LiteralPath 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/gameplay-observed-clock-20261030/evidence/source-snapshot.zip' -DestinationPath "$recheck/source"
dotnet build "$recheck/source/tests/RizToolbox.Gameplay.Tests/RizToolbox.Gameplay.Tests.csproj" -p:RestoreIgnoreFailedSources=true -m:1 -p:UseSharedCompilation=false -o "$recheck/bin"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet "$recheck/bin/RizToolbox.Gameplay.Tests.dll"
if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
```

`r1` 遗留快照的 result 为 passed=false 且没有 build/test 结果，不能作为完成证据。`r2` 修复两项失败后 74/74；`r3` 实际为 75/75，但 runner 对 Windows CRLF 的 summary 匹配失败，`r4` 修复后完成冻结。root 复审提出 fresh 来源与 run 关联边界后继续实修；`r5` 因新增测试的重载类型歧义编译失败，`r6` 修正后 78/78，`r7` 加 publication 异常与不推进历史检查后 79/79；`r8` 保留可选 source root 的独立读取边界后再次冻结、编译、执行并完成哈希复核。只有最新 `r8` 被本页作为最终候选。
