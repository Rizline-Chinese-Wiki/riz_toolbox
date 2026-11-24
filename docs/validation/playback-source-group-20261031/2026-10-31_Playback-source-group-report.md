# 私有 GetTime + SetPitch 共同安装组验证

2026-10-31，`flavor=null`。本包验证 broker 私有两成员组合、既有 GetTime owner 采纳、完整映像持续封存、实际 native 责任及全组撤回。主验证为 **19 个独立进程 / 941 项检查**，新增 consumer/reader 故障验证为另一份 **2 个独立进程 / 62 项检查**；各 fresh managed build、runner 与全部 child actual exit 均为 0。两份结果使用同一 core source proof 和同一 native r6 输入，生产源码一致，只有各自明确说明的单个测试文件 overlay 不同。未执行游戏代码；当前成果只闭合 S1 两成员子项，foundation 联合首次安装及后续完整 N2 仍未成立。

授权、offline 工具边界和 work items 见[范围](scope.md)；关键先后关系见[时间线](timeline.md)。失败原件及成功原件均保留，新结果没有覆盖旧目录，也没有重跑旧 19/159 return、instant 七模式、camera、N1 或 retirement。

## 实现结果与两个修复

`ExactHookBroker.Playback.Group` 通过保留 registry 发行不可复用 group epoch，两份 lease 指向组中的实际 GetTime/SetPitch owner。安装依次执行原始映像预验、两成员 Prepare、原映像再验、逐成员 Commit/readback、整体验证、arm 和最终验证。SetPitch 的实例 `set_pitch` 与静态 `SetPitch` metadata 对应同一物理入口；通用 binder 不能接管该专用 ABI。已有 GetTime 只能由真正的私有 installation owner 被采纳，且必须在无 active policy、actual in-flight 为零的边界；采纳保留原 entry/original，不重复 Commit。

完整 seal 保留 pinned file SHA、SEC_IMAGE 页身份、所有非成员 immutable/executable 区域及准确 AMD64 DIR64。成员区间由真实 owner 的私有原正文、已签实际 patch、未改尾部、relay/slot、Dobby relocation、独立 R11 thunk/unwind 及 metadata 组合；没有 caller mask/span/Ready 准入口。SetPitch 的 `PUSH RBX; SUB RSP,30` 与 GetTime 的六字节搬移分别校验，各有独立 unwind，未将 GetTime 形状外推给其他目标。

本次收尾修复两处实际问题：

1. grouped GetTime 的 `ReadActivity()` 原先在 clean withdraw 后调用 active verifier，会把正常活动读取升级为 sticky fault。现在直接读取 group 的保留活动视图，两个成员 native 计数仍汇总，只有真实物理证据或 native Known 丢失才故障。
2. `NativePlaybackSingleReturnBridge.ReportFault()` 原先只调用 r4 native `MarkFault`；该入口置 Unknown，却不清 `authorityActive`。现在在 bridge 与 Publish 共用的原 `_gate` 内 once-only 标 fault，并用实际保留的 `_published.Revision` 做 native conditional withdraw。私有 publication 对象不清空，不用公开 current 数值重建票；foreign 新 generation 不能被该旧 revision 清理。后续 Publish 仍因原 sticky fault 拒绝，native in-flight 责任没有归零捷径。

源码和实际编译文件绑定见 E-SOURCE/E-SYMBOLS/E-FAULT-SYMBOLS。failure → fix 的联合反例见 E-FAIL/E-JOINT：旧 joint r1 已使三个 managed 对象 inactive，却仍留下 native active 标志；同 core 的 joint r2 保留原门槛，4 模式 / 84 项通过。该 84 项属于联合 profile 分包，不并入本包计数。

## 两份实际执行范围

| 主 19 模式 | actual checks | 实际行为 |
|---|---:|---|
| normal | 30 | 两个 actual Prepare/Commit、实际 GetTime policy/原调用、四次 setter alias/同值/ABA、clean withdraw |
| adopt-broker | 31 | 真正先有 GetTime broker owner 的正向采纳；同 entry/original/lease，没有新造 DTO 凭据 |
| deferred-dispose-stream | 328 | 300 次新增 actual setter；总 Entered=Exited=304；callback Dispose 请求超过 256 journal 容量仍实际 drain |
| withdraw-inflight | 32 | actual setter 阻塞中，group/GetTime/SetPitch 活动均 Known、InFlight=1；clean withdraw 保留该责任，真实返回后才为 0 |
| source-seh | 30 | 独立原跳板/unwind 路径保留 native `0xe2400011`、original 恰一次；全组 sticky 且 native policy 关闭 |
| GetTime patch/tail/original/Dobby/relay/unwind | 6 × 35 | 任一成员物理变化撤双方；恢复原字节不复活，raw stock 返回保持 |
| pitch patch/tail/original/Dobby/relay/unwind | 6 × 35 | 同上，SetPitch 各自身证据分别破坏 |
| metadata / outside executable | 2 × 35 | alias metadata 或组外 executable 变化不能被成员组合掩盖 |

19 进程合计 actual Prepare **38**、Commit **38**；adoption 进程的 GetTime Commit 已发生于其合法既有 owner，采纳阶段保留它，只新增 SetPitch 安装。每进程两次物理安装均被实际 engine counters 读回。941 中有 19 项新增只读 ASLR/DIR64 组合检查，其余 922 为冻结 core 的原测试检查。

每个主进程从磁盘 PE 读取 preferred base（6442450944），记录真实 module base，均有非零 ASLR delta。r6 的 DATA export `StockExecutableDir64Anchor` 位于 `.text` RVA `0x1e70`，其实际指针等于 SetPitch RVA `0x19b0` 的已重定位地址；该 anchor 在两成员 body 之外，仍被完整 executable 比较覆盖。不能从 loader 已改写的 mapped Header.ImageBase 反推磁盘 preferred。实际地址在[每进程 stdout](evidence/group19/checks/test-result.json)中保留；静态 DIR64/PE/PDB/COFF/C13 原件见 E-NATIVE。

| 新增独立故障进程 | actual checks | 实际行为 / actual Prepare、Commit |
|---|---:|---|
| source-callback-fault | 43 | 同进程两份相同 fixture、不同 module/epoch，分别在实际 Before 与 Returned observer 抛一次。每个故障 setter original 恰一次、原参数/MI 保留、正常 physical receipt 可释放；随后一次 dormant setter 继续恰一次原调用而不发行新观察。两组各 2/2，进程合计 4/4 |
| clock-reader-fault | 19 | 先由实际 GetTime 使用有效 native policy；下一 GetTime original 正常返回后，actual clock reader 抛一次。返回仍是原 `3f` raw bits `1077936128`，source 不发行新 authority；该进程 2/2 |

两新增进程共 actual Prepare **6**、Commit **6**。三种实际故障边界结束后，native `AuthorityActive`、group/GetTime/SetPitch 的 Active 均为 false；actual retained activity 为 0，重复验证及后续 dormant 原调用不能复活。该 62 项有独立行为源码、PE/PDB、stdout/stderr/exit 与 wrapper，不改写或重算原 19 模式。

## 源码、PE/PDB 与输入身份

两份 build 分别从同一 core proof 拷贝 720 个 frozen inputs，独立构建 `RizToolbox.PlaybackSourceGroup.Tests.csproj`。项目为取得现有内部测试访问而使用 `RizToolbox.Gameplay.Tests` assembly 名；它是独立 group 测试程序，不是主 Gameplay 全套检查。每份验证五个实际 managed PE 的 CodeView GUID/stamp/age 与相邻 portable PDB ID，再逐项校验全部 **451** PDB document SHA-256：436 个固定输入、15 个生成文件。

主 build 唯一 D-only `Program.cs` overlay 只读磁盘 preferred、实际 module 与 exported DIR64，并输出地址；原功能断言保留。新增 fault build 另用完整、明确的单 `Program.cs` 测试行为 overlay，只运行两种新增模式。两个 overlay 的 baseline 都来自同一 core Program，SHA `2f77023074d783a2273f94dd5b9451c970c292173383266b17b03c7f4d1e1dca`；实际主 Program 为 `c489a5bcf29906ba8f86a5bd9c82b5889458a2137bff40317d80f0a78c4bdf88`，fault Program 为 `255b987dc6bda386243ff2d3788a146a101516fa63d3ba5829c951c0b20665ff`。不宣称测试文件与 core 字节相同；全部生产文件仍与 core 相同。相应压缩源、before/after 清单及 overlay 原件在 case evidence 中保留。

| 实际输入 / 结果 | SHA-256 |
|---|---|
| shared core proof | `8e3778772f18adcae23c2df42714f603e0c72d4e093920fbf298772083b59017` |
| native r6 build proof | `6534334fbe9432ffb8086c0dd3156259b00f29894bb01caf48779e3171e3eeff` |
| Stock fixture DLL | `cc45d2a47f99180443fa1d2a839c0567c66cd7e7bacdb7a59c9af346d53c29fc` |
| source helper DLL | `a96160f46ca952ed0b6097478dc44efff840127d52d65d3ea43e5ba01c314083` |
| GetTime r4 helper DLL | `90b6133cf6ecd98dd00cd34472d81cd91493419ea4cb06b0aa59ffb0f7f0b355` |
| Dobby DLL | `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded` |
| 本次 Runtime DLL（两份 build 同值） | `31431e2be0bfa3add1145d336c787154360984c844d6a1b8cfee34e0722ee1b8` |
| 19 模式 wrapper | `f7408747f40c41614f6a1b6dc069ebc438176b2bbeefc256fa2da51cd4e16e1c` |
| 2 模式 wrapper | `ef75a4418b2f8aedf0f513640bb75424aa7081a1beb9a20b8783340da146e885` |

native 全 package、GetTime r4 package 与 Dobby 均拷贝到对应 D 盘构建目录并固定前后 hash；主包 73 个 native inputs，新增故障包 74 个（多一份相同 fixture）。r6 native package 的逐 TU flags、COFF/C13、PE/PDB、imports/exports、relocations/unwind 原件独立保留；正式 helper V7 未改变。E-IMPORTS 保存两个 r6 DLL 的实际 imports/exports 视图；这是依赖/接口身份记录，未从导入表推断本包之外的动态行为。

[独立 post-copy audit](evidence/fixity-result.json) actual exit0：114 个 case-local 原件副本及 D 原件、共 2600 个唯一文件身份逐 hash 通过；两份 scope 的四个生产 PE/PDB 完全同值，所有 child stderr 均为空。核验[脚本](evidence/runners/Verify-Evidence.ps1)同时确认两个编译树各只有已声明的单个测试文件与 core 不同。

## Evidence → Finding → Path

完整 `source_ref`、原件 hash、case-local artifact、时间与命令记录见[证据索引](evidence-index.json)。

| Evidence | 原件 / case artifact | 支持范围 |
|---|---|---|
| E-SOURCE | [主 wrapper](evidence/group19/test-result.json)、[实际编译源](evidence/group19/compiled-source-before.json)及同 hash after | 同 core、单只读 overlay、固定 native 输入，非生产准入 |
| E-GROUP | [19 child 结果](evidence/group19/checks/test-result.json)及 19 份独立 stdout/stderr/exit | 941 项、双安装/采纳/stream/withdraw/SEH/14 tamper 的实际行为 |
| E-SYMBOLS | [主 PE/PDB/source proof](evidence/group19/managed-symbol-source-proof.json) | 实际五 PE 与 451 document 的 identity/checksum |
| E-FAULT | [新增 wrapper](evidence/faults/test-result.json)、[两 child 结果](evidence/faults/checks/test-result.json) | 62 项、Before/Returned/reader 真异常、raw original、全组/native sticky 撤回 |
| E-FAULT-SYMBOLS | [新增 PE/PDB/source proof](evidence/faults/managed-symbol-source-proof.json)、[行为源码](evidence/faults/overlay/Program.behavior.cs) | 新行为 overlay 与实际五 PE/PDB 的准确归属 |
| E-NATIVE | [r6 build](evidence/native-r6/build-result.json)、[native 符号/源 proof](evidence/native-r6/native-symbol-source-proof.json)、[fixture relocation](evidence/native-r6/RizToolbox.PlaybackStockFixture.relocations.txt) | 已固定的自有 ABI、独立展开及 `.text` DIR64；不将 native 13 模式计数搬进本包 |
| E-IMPORTS | [fixture imports](evidence/native-r6/RizToolbox.PlaybackStockFixture.imports.txt)、[exports](evidence/native-r6/RizToolbox.PlaybackStockFixture.exports.txt)、[helper imports](evidence/native-r6/RizToolbox.PlaybackSourcePrototype.imports.txt)、[exports](evidence/native-r6/RizToolbox.PlaybackSourcePrototype.exports.txt) | DLL 实际依赖与执行/只读接口锚点 |
| E-FAIL | [旧 joint r1 failure wrapper](evidence/failure/joint-r1-test-result.json)、[原 pitch-tail 错误](evidence/failure/joint-r1-pitch-tail.log) | actual exit=1；保留 native active 标志问题的反例 |
| E-JOINT | [同 core 的 joint r2](evidence/joint-r2-test-result.json) | 原门槛修复后 4 模式 / 84，所有游戏原调用/DllMain 为零；计数另列 |
| E-FIXITY | [post-copy audit](evidence/fixity-result.json)及[核验脚本](evidence/runners/Verify-Evidence.ps1) | 2600 个唯一实际文件身份、两个单 test overlay、生产 PE/PDB 同值、实际 stderr 为空 |

| Finding | severity / category / confidence / status | evidence_ids / location / 影响 |
|---|---|---|
| F-GROUP-1：私有两成员共同安装及真实先有 owner 采纳已在受控映像成立 | n/a_re / other / high / validated | E-SOURCE, E-GROUP, E-SYMBOLS；`ExactHookBroker.Playback.Group`、`Playback.Owner`；两租约不能从 scalar DTO 发行 |
| F-GROUP-2：任一成员或组外物理证据变化会全组 sticky，恢复字节不复活 | n/a_re / other / high / validated | E-GROUP, E-SYMBOLS, E-NATIVE；`NativePlaybackExecutableSeal`、成员 owner；保留完整 file/DIR64 和各自原跳板/unwind |
| F-GROUP-3：clean withdraw 与实际责任分离，stream/blocked call 能真实 drain | n/a_re / other / high / validated | E-GROUP, E-SYMBOLS；`Playback.Live.ReadActivity`、group retained activity、source bridge；Known 不因正常撤回被抹去，也不造零在途 |
| F-GROUP-4：fault 的 native policy 撤回缺口已按私有 publication 条件修复 | n/a_re / other / high / validated | E-FAIL, E-JOINT, E-FAULT, E-FAULT-SYMBOLS；`NativePlaybackSingleReturnBridge.ReportFault`；真实 consumer/reader 故障仍保 original 与 completion，并撤双方/native policy |
| F-GROUP-5：foundation/生产联合准入仍不成立 | n/a_re / other / high / validated-source-boundary | E-SOURCE, E-NATIVE；private group 只有两具体 owner，完整 V7 foundation pre-Prepare/unwind 凭据缺失；本项为源码/范围结论，无生产动态验证 |

`P-GROUP`，`path_type=callflow`：固定 core + native PE/PDB/file 输入（E-SOURCE/E-SYMBOLS/E-NATIVE）→ 私有 registry targets/owner + group epoch（F-GROUP-1）→ 两成员 Prepare/Commit 与 exact composition → 两 lease + 最终 Clock/native publication 的实际 GetTime/SetPitch 调用（E-GROUP）→ 任一物理或 consumer/reader 故障（E-GROUP/E-FAULT）→ native private revision conditional withdraw + 双成员 sticky + 真实 physical completion/drain（F-GROUP-2/3/4）。残余依赖为 F-GROUP-5，以及完整 stock/writer/run/root/inline/handoff/真实消费者覆盖。

## 复现与限制

完整 frozen scripts 已复制到[主构建 runner](evidence/runners/group19-build-and-test.ps1)与[新增故障构建 runner](evidence/runners/faults-build-and-test.ps1)。以下命令会新建目录；已有同名目录必须保留，重跑时选另一个原先不存在的目录。

```powershell
$case = 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-source-group-20261031'
$pwsh = 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
$core = 'D:/riz-build-cache-next/foundation-stock-source-core-r1-20261031/test-result.json'
$native = 'D:/riz-build-cache-next/playback-stock-native-build-r6-20261031/build-result.json'
& $pwsh -NoProfile -File "$case/evidence/runners/group19-build-and-test.ps1" -SourceSnapshotManifest $core -NativeBuildManifest $native -GroupRunner "$case/evidence/group19/Test-PlaybackSourceGroup.ps1" -OutputDirectory 'D:/riz-build-cache-next/playback-group-repro19-20261031'
& $pwsh -NoProfile -File "$case/evidence/runners/faults-build-and-test.ps1" -SourceSnapshotManifest $core -NativeBuildManifest $native -GroupRunner "$case/evidence/faults/Test-PlaybackSourceGroup.ps1" -BehaviorProgram "$case/evidence/runners/fault-behavior.cs" -OutputDirectory 'D:/riz-build-cache-next/playback-group-reprofaults-20261031'
```

这是当前机器保留离线依赖/原件时的复现命令；不安装工具，不下载游戏，不将新 output 当旧结果。全部 actual stdout/stderr/exit、构建输出、source before/after 和 PE/PDB identity 都写入新目录；出现失败也保留 manifest。

当前未证明 foundation 合法成员共同首次写入、真实 IL2CPP metadata/run/root/source 的完整发行、最下共同 writer/clip/field/engine 路径、stock/inline occurrence 与 schedule 联合、连续两局 logical handoff或实际音画/结果流程。unknown/foreign 新 policy 的合法 successor 交接不在此实现；本次条件清理只持有本 bridge 原票，不重认未知 generation。完整 N2 与生产 FirstInstall 均未完成，默认真实 Speed/Seek 继续关闭。

完成检查：已保留失败原件；scope/timeline/Evidence/Finding/Path 已列；两 overlay 的实际源与 PE/PDB 绑定已固定；raw logs/exit 和 native 输入 hash 已保存；未以本包计数代替其他分包或生产验收。
