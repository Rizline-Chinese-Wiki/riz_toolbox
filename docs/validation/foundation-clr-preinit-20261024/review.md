# CLR 采用与初始化合同有限复核

复核日期：2026-10-24（UTC+8）。范围见 [scope.md](scope.md)。本复核核对实际源码、构建顺序、原始日志、三组运行结果与冻结清单，不代表游戏验收；部分被审源码由本代理实现，不能称为独立全面审计。文档采用普通技术复核结构（flavor = null）。

**最终完整构建已通过。** [构建终态](build-run.json)记录会话 57905 exit 0；[最终原始日志](build.log)的 SHA256 为 `e4876e10fbb5cc2a5e9045f721ae626947f04f7ea284c57c8d4a971a1ed8d797`，与终态及[构建汇总](build-summary.json)一致。25 套托管回归为 1515/1515；另有实际 CLR 采用 59 检查／2 个独立进程、Unknown 交错 328 检查／100 次暂停期间读取、共享生产 C init 函数 259 检查／16 个独立进程，三组均通过。这些额外检查单独计数，不计入 1515。

首次 full 会话 27623 exit 1 的[失败原日志](build-first-seh-assertion-failure.log)仍保留，SHA256 为 `ce63bc65758804c707526011f45fc25c01e2fc699e6c6851d4a706ff3a110fca`；它停在 `ready-original-seh` 的 ExceptionFlags 断言，未执行后续 Unknown、managed mapped 采用和完整托管回归。诊断会话 25780 也 exit 1，原因是同一测试错误：fixture 将 `RaiseException` 的请求 flags 0 误当成实际异常记录必为 0。[诊断结果](seh-diagnostic-fixture.json)和[诊断日志](init-seh-diagnostic.log)显示，被动 first-chance 与最终捕获的 flags 均为 128，参数个数均为 3、完整 payload 相同。最终 fixture 改为严格比较这两份实际异常记录，没有放宽成仅检查捕获异常；诊断后生产 C init 调用函数未改。定向会话 34986 exit 0 见 [targeted.log](targeted.log)，最终结论采用随后完整构建的证据。

## 证据、发现与后续路径

| 证据 | 实际观察 | 有限结论 | 下一步 |
|---|---|---|---|
| E01：[managed 子进程](../../../tests/RizToolbox.Runtime.Tests/NativeFirstInstallMappedRegression.cs)、[运行脚本](../../../scripts/Test-FirstInstallManaged.ps1)、[生产采用器](../../../src/RizToolbox.Runtime/Hooks/NativeFirstInstall.cs)及[实际 CLR 结果](managed-adoption-evidence.json) | 两个独立子进程实际通过 59 检查，happy 33、reentry 26；真实 producer 到生产采用器，双 sink 共享入口，全部 original／transport／托管观察回调为零 | F01：真实 CLR 中的配置采用已执行；没有反射构造私有 owner 或调用方伪造 Ready，元数据身份仍受控 | P01：下一阶段验证真实元数据、实际 native→CLR 回调及冷启动首帧，不能用本次零回调结果代替 |
| E02：[Unknown 发布与读取](../../../native/RizToolbox.JudgeContext/FirstInstall.cpp)、[确定交错 fixture](../../../native/RizToolbox.JudgeContext/fixture/FirstInstallUnknownFixture.cpp)及[交错结果](atomic-unknown-evidence.json) | 328 检查通过；atomic 写入后且详情 writer 仍暂停的 100 次读取全部为 Unknown／1，writesStarted 保持 3 | F02：确定交错实证覆盖 atomic 已失效、详情尚未更新时的滞后窗口；seam 仅编译入测试 EXE | P02：生产持续保留 fail-closed，不能把这一个确定交错推广为所有并发安装安全证明 |
| E03：[生产 init 调用函数](../../../native/RizToolbox.Doorstop/riz_init_call.c)、[固定类型声明](../../../native/RizToolbox.Doorstop/riz_init_call.h)、[Doorstop 构建接线](../../../scripts/Build-Doorstop.ps1)及[调用合同结果](init-call-evidence.json) | 同一生产 C 源执行 259 检查／16 进程；12 个正常或异常场景与 4 个真实 fail-fast 场景通过，SEH first-chance／caught 完整记录一致 | F03：受控 native original 下的调用一次、int 返回、bootstrap 顺序与异常传播通过；未运行真实 producer／CoreCLR 冷启动 | P03：验证实际加载链到合法 tick 的时序与失败分支 |
| E04：[首次构建日志](build-first-seh-assertion-failure.log)、[诊断记录](seh-diagnostic-fixture.json)、[最终构建终态](build-run.json)及[最终汇总](build-summary.json) | 保留两次 exit 1；修正 fixture 对 flags 的错误前提后，定向和最终完整构建 exit 0 | F04：本批最终验证通过；首轮失败未被覆盖，结论限于最终冻结输入和各 fixture 范围 | P04：保留原始失败、诊断与最终记录的对应关系 |
| E05：[源码清单](source-manifest.json)、[源码归档核验](source-archive-check.json)、[包清单](package-manifest.json)、[包归档核验](package-archive-check.json)及[前批差异](changes-from-1515.json) | 482 个源码输入与当前文件哈希一致，冻结源码归档 482 项、暂存包 22 项；对上一批新增 8、修改 7、删除 0 | F05：本批声明的构建输入及暂存产物已冻结；包未安装，归档完整不等于干净机器复现或 B19 完整发布矩阵 | P05：下一阶段沿冻结输入复现并继续独立验收 |

复现入口分别为 `scripts/Test-FirstInstallManaged.ps1`、`scripts/Test-FirstInstallUnknownNative.ps1` 和 `scripts/Test-DoorstopInitNative.ps1`。这些脚本由唯一 builder 调用，本代理只读核验和更新此文档，未运行构建、游戏或安装。三组结果已与最终缓存目录 `D:/RizToolbox-build-cache/clr-preinit-final-20261024` 的实际 `fixture-result.json` 核对：init／managed 文件逐字节一致；Unknown 归档经过 JSON 格式化，字段值一致。482 个当前源码哈希全部匹配清单，源码 ZIP、源码清单及包 ZIP 的 SHA256 分别与冻结核验记录相符。首个完整构建的失败不由文档掩盖，后续通过也不覆盖其原始证据。

## 实际 CLR 采用能证明什么

`happy` 与 `reentry` 使用独立 dotnet 进程；helper 由脚本复制到实际 Runtime 程序集同目录后加载。GameAssembly 使用 `DONT_RESOLVE_DLL_REFERENCES` 映射，只执行 helper 的生产 producer。该测试模式不得迁入游戏 Loader。

正向路径实际执行生产 `NativeFirstInstall.RequireReady` → `NativeRuntimeInvokeBridge.Adopt` → `ExactHookBroker.BindNativeVoidObservationWithMid`。happy 的 33 检查通过，核对真实 slot 31、Target/Original/Entry/epoch、三次写入、native Info 的未附着到已附着转换，两个 broker 的 sink 共享同一物理入口，并覆盖单方解绑、全解绑后重绑、重复 runtime observer、错误 target 和冲突元数据身份拒绝。禁止安装的 engine 收到 Install/Prepare/Commit 即失败，实际采用前后映像字节相同。reentry 的 26 检查通过，在 Ready 后真实重入 producer，得到 Unknown、保留原身份，并使生产采用器拒绝继续。

这些检查**没有执行任何游戏 original、init、native transport 或托管观察回调**。结果中父入口、mid、runtime-invoke 的 entered/exited/in-flight 计数为零，`transportInvocations=0`、`managedObserverCallbacks=0`；实际游戏 DLL、Dobby 和 helper 的文件哈希前后相同。MethodInfo/Class 是受控不解引用的身份载体，`metadataIdentityControlled=true`；既不证明 IL2CPP 真实元数据解析，也不证明 native→CLR 回调、ledger 捕获、GC 后游戏引用有效或游戏首帧。最终 JSON 保留 `nativeToClrCallbackVerified=false`、`actualLedgerCaptureVerified=false`、`firstInstallSafetyVerified=false` 与 `gameRuntimeVerified=false`。

## Unknown 交错的严格因果

旧窗口是：writer 已把 atomic unknown 设为 1，尚未取得 `infoLock`；reader 读到旧 `publishedInfo`，详情仍是 Ready/unknown 0。只补第二次详情拷贝不能消除此窗口，因为暂停的 writer 还未更新详情。

本次 `GetInfo` 在最终共享锁内拷贝详情，并读取 atomic unknown；已失效时输出 Unknown/1。初始化判定也核 atomic，不能仅因详情 phase 尚未更新就返回可继续的结果。该证明只覆盖 atomic 写入早于 reader 最终采样的因果；不承诺返回后未来故障不会发生。

fixture 把**同一份生产 FirstInstall.cpp** 编译进独立 EXE，仅以 `RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE` 在 atomic 写入后、writer 取得详情锁前暂停。主线程等待暂停事件后进行 100 次 `GetInfo`；每次还核 worker 未退出，期间不给恢复信号，故不能用“故障后来才发生”解释任何 Ready。最后才放行，要求重入返回 2、身份及三次写入不变、代码与 relay 不再改写、transport 计数为零。

[最终交错结果](atomic-unknown-evidence.json)中 `unknownAtomicPrecedesEveryRead=true`、`detailWriterPausedDuringReads=true`；100 次暂停期间读取均观察到 Unknown，最终 phase 4／unknown 1／writesStarted 3、重入结果 2，328 检查全部通过。该结果证明上述限定交错下没有读取旧 Ready，不证明实际游戏初始化中的全部竞争条件。

该 seam 只改变测试 EXE 的调度，不为生产增加运行时开关或导出。[独立构建脚本](../../../scripts/Test-FirstInstallUnknownNative.ps1) 定义测试宏；[生产 helper 构建](../../../scripts/Build-JudgeContext.ps1)不定义它。导出表检查是辅助证据，不能单凭“没有导出”证明二进制内部没有 seam；生产编译参数和最终源码清单也必须一起核对。

## 生产初始化函数与剩余范围

`RizInvokeIl2CppInit` 的 original 类型为实际 `int(__cdecl*)(const char*)`。一次性原子 guard 在 original 前拒绝重复、递归、无效 ready 和必要空函数；正常 original 返回后，只有 ready 为 1 且 int 非零时才 bootstrap。原始 int 直接返回，函数不以 SEH/C++ handler 包围 original 或 bootstrap。[生产接线脚本](../../../scripts/Build-Doorstop.ps1)将固定上游 `init_il2cpp` 的实际调用改成该函数；fixture 也编译这份 C 源，而不是复制另一套实现。

fixture 的 ready 和 native original/bootstrap 是受控输入；259 检查／16 进程全部通过也只证明该调用合同，不能证明真实 producer 与原 init/CoreCLR 冷启动时序。SEH 的 code、flags、参数与 payload 已按 first-chance 和 caught 实际记录严格核对，保留 flags 128，未通过改生产代码消除测试失败；C++ 异常和 original／bootstrap 的顺序也由这组受控场景覆盖。结果明确 `actualProducerExecuted=false`、`coreClrExecuted=false`、`coldStartupVerified=false`。最终构建成功仍有原始日志记录的编译 warning，不能称为零警告构建。实际冷启动、首次合法 tick/UnityEvent、真实监听与对象持根、连续 mutation/provider/取消、未来来源和全部资源退休仍须独立完成。B01–B19 不扩大勾选，RequestPlay 继续关闭，游戏验证继续遵从“实测稍后”。
