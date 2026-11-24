# Stock source native entry 与 completion 原型报告

2026-10-31，`flavor=null`。独立 native r4 的 **13 个进程/26920 项检查全部 actual exit 0**，每进程用实际 Dobby Prepare/Commit 挂接八个自有 source 入口。typed entry 自动保存实际参数、caller、incoming MethodInfo、parent TLS 和 native sequence；回调只能读取当前 callback 窗口，physical completion 在所有 observer 返回后的 native `__finally` 中产生。normal 模式确实经历 stock reset pitch=1，再由测试 return observer 实际调用 setter 恢复 pitch=2，但该行为不是正式 Gameplay provider/ClockPublication 联合。原始失败、旧成功版本和当前结果分别保留。生产准入为 false；没有执行真实游戏方法，也没有操作游戏/UI/账号。

授权、排除范围见 [scope](scope.md)，版本顺序见 [timeline](timeline.md)。本报告只验证 [十个 owned 文件快照](evidence/current/native-owned-inputs.json)，不将 26920 用作后续 Runtime/group 或整个工作树的当前验收。E/F/P 完整字段及所有 content hash 见 [evidence-index](evidence-index.json)。

## 1. 当前实物与独立符号证明

原件目录为 `D:/riz-build-cache-next/playback-stock-native-build-r4-20261031/`；检查原件为 `D:/riz-build-cache-next/playback-stock-native-checks-r3-20261031/`。以下身份取自 [实际 PE/PDB 一致性证明](evidence/current/native-symbol-source-proof.json)（E-SYMBOL）；三组 RSDS GUID/age 均与对应 native PDB info stream 相符，age=1。

| 实物 | PE SHA256 | PDB SHA256 |
|---|---|---|
| `RizToolbox.PlaybackSourcePrototype.dll` | `5e2944ebd2f998d84aa298252bf644a85b899143357f573cc576ed43f18337e9` | `8938d216e68c36e9d0985698ef9da05a44ea7226767f9d4014e62698eb8e00dc` |
| `RizToolbox.PlaybackStockFixture.dll` | `a9223ac80a70f4c2b19a1d7afaffb0f7bc238a55d8cf11505d95a8402e2a0e11` | `96382aefb845e799101096a8df36c5ccd3b64244129782a8fa94ea1ebd48f043` |
| `PlaybackSourceFixture.exe` | `e33596f72ec798f42d666a935e2788c9bbddf99bf2bc882d91613f01163f8357` | `6b987e5b6c4b5bd5313e1e6c2f52ac5069ab5480a5dd6b5192810e9739b87bcd` |

固定 Dobby SHA256 为 `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded`；不主张 Dobby PDB 来源。构建启用 AMD64、ASLR/NX/CET/CFG；导入、导出、disassembly、unwind 与 COFF 原件的位置及 hash 均在 index 中。依赖是本机 Windows API、MSVC runtime 和固定 Dobby；没有游戏依赖被调用。（E-BUILD、E-SYMBOL、E-IMPORTS）

五个 TU 的完整路径、独立 object hash 及 flags 在 [build-result](evidence/current/build-result.json)；C13 checksum 按 object 实际记录回读源码/头文件，5/5 object 均一致，并非仅比较同名文件。（E-BUILD、E-SYMBOL）

| 冻结 TU | 产生的 COFF | 每 TU flags |
|---|---|---|
| `PlaybackSourceContext.cpp` | `PlaybackSourceContext.obj` | `/nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Z7 /guard:cf` |
| `PlaybackStockFixture.cpp` | `PlaybackStockFixture.obj` | `/nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Z7 /guard:cf` |
| `PlaybackSourceFixture.cpp` | `PlaybackSourceFixture.obj` | `/nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Z7 /guard:cf` |
| `PlaybackStockFixture.asm` | `PlaybackStockFixture.asm.obj` | `/nologo /c /Zi` |
| `PlaybackSourceCaller.asm` | `PlaybackSourceCaller.asm.obj` | `/nologo /c /Zi` |

fixture `StockSetPitch` 为 RVA `0x19b0`、320B，实际头 `40 53 48 83 ec 30`，body SHA `3df79afe3b25d631b39faa718c87fc421a8b0ac262fed000f85e17952e7308d2`；对应 unwind 前八字节 `01 06 02 00 06 52 02 30`。`StockGetTime` 为 RVA `0x1730`、640B，头 `48 89 5c 24 08 57 48 83 ec 50`，body SHA `c493c58e3dd4d4c960a2c4a2290219d6ac36c45a1082808638d254a0744cb742`。二者 runtime-function end 分别是 `0x1af0`/`0x19b0`，未互套 prologue/unwind。八个 source 原跳板在执行前核实际 Dobby 搬移，并采用与该 fixture 六字节 PUSH RBX/ALLOC 30h 独立匹配、带动态 unwind 的 RX R11 continuation thunk；真实原 SEH 已穿过这条路径。（E-SYMBOL、E-SEH、E-SOURCE）

## 2. 实际 source 入口与 native 责任

独立协议 v1 使用 pack8：Frame 160B、Info 136B。五类 ABI 为 Void0、Single1、Double1、Ref1、Bool1；本轮安装八个入口：SetPitch、MUG-shaped Pause/Replay/Resume、PlayScheduled leaf、SetClip、Progress.Play-shaped Boolean 和 Pause leaf。static `SetPitch(AudioSource, Single)` 与 instance `set_pitch(Single)` 的两个 88B metadata getter 指向同一 physical target，不重复安装。Single 低32位、Double 全64位、reference/null、Boolean 低8位以及原 incoming MI 都直接传给原函数；无手工 Record/Begin/End 发行口。（E-SOURCE、E-BITS、E-RUN）

同一个 native owner 的通道共享 sequence、writer/source/scheduling fence 与 TLS parent。Before 中先推进对应 fence，因此 `2→2`、`2→7→2`、nested equal pitch 和 clip 同值/ABA 均有不同 native 序列；读回同值没有恢复旧票的效果。Frame 来源必须匹配实际 current scope、native owner/epoch、kind/slot/thread 和完整字段，改写一个 ArgBits 的复制 frame 在回调内被拒。（E-SOURCE、E-NESTED）

生命周期按实际 native 边界运行：

1. actual entry 记录 Entered/InFlight 与 entry 瞬间的 armed 状态，建立 TLS scope；处于 armed 的调用预留 bounded journal 行。
2. Before observer 正常或异常后，原函数仍恰调用一次；原函数正常返回才产生 OriginalReturned，原 SEH 路径产生 Abnormal。
3. OriginalReturned observer 内仍在 current scope，`PhysicalExited=false`。`after-inflight` 由另一线程读取到 `InFlight=1`、无 physical receipt，不能把该回调当退出。
4. observer 全部返回后，native `__finally` 先结束 callback/TLS authority，再减少 InFlight、增加 Exited、写只读 completion 行；此时 ReadReceipt 才成功。ReleaseReceipt 只确认并删除已存在的 exact sequence/token 行。（E-SOURCE、E-AFTER、E-RUN）

只有 actual entry 瞬间 armed 的调用拥有持久 journal 责任。Dormant 和 Withdrawn 调用仍执行原函数、推进 native fence/Entered/Exited/InFlight 并给出标记 frame，但不占新的持久行。`dormant-withdraw` 的 300 dormant + 300 withdrawn setter 调用均真实 forward、无 completion 凭据，也不会在第257次人为触发 journal 满。已进入 armed 调用的责任在中途 withdraw/fault 后保留，不能当作新 dormant 调用丢弃。（E-SOURCE、E-WITHDRAW）

Arm 仅允许 owner thread、无在途、至少一个配置槽、未 fault/withdraw 且一次成功；withdraw 只核 native private owner+epoch，允许 worker 永久撤回。`worker-withdraw` 在 owner thread 的真实 original 阻塞时从 worker 撤回，原 original 返回后的 exact receipt 仍完整；后续300次 setter 不新增 journal。foreign actual source call 则 sticky Unknown，仍保原调用与在途事实；这与只撤 authority 的 worker 操作有不同语义。（E-WITHDRAW、E-FOREIGN、E-SOURCE）

journal 容量256；未释放责任满后第257次 armed attempt 触发 sticky Unknown、不覆盖旧256行、不伪造第257次完成凭据。observer 异常被 native 独立 SEH 边界隔离并标 Unknown，原函数继续执行，未知状态不因恢复字节或再 Arm 复活。本包只证明原型状态机，不代替生产 root 释放/跨局 handoff 凭据。（E-JOURNAL、E-OBSERVER、E-SOURCE）

## 3. 十三个实际进程的原始结果

每行对应独立 stdout/stderr/exit 文件；全部 stderr 为0B，actual exit 文件均为字面 `0`。每进程 actual Dobby Prepared=8、Committed=8；这是不同进程内的八个自有 source targets，未把 GetTime/group 的 Commit 计入。完整 raw JSON 见 [current 日志目录](evidence/current/) 与 index。（E-RUN、E-EXITS）

| 模式 | checks | native Entered/Exited | Returned / Abnormal | actual exit |
|---|---:|---:|---:|---:|
| normal | 522 | 13/13 | 13/0 | 0 |
| bits | 770 | 22/22 | 22/0 | 0 |
| nested-aba | 442 | 10/10 | 10/0 | 0 |
| original-seh | 278 | 4/4 | 1/3 | 0 |
| inline-pause | 218 | 2/2 | 2/0 | 0 |
| dormant-withdraw | 12170 | 600/600 | 600/0 | 0 |
| foreign-thread | 195 | 1/1 | 1/0 | 0 |
| observer-fault | 192 | 1/1 | 1/0 | 0 |
| journal-overflow | 5053 | 257/257 | 257/0 | 0 |
| inflight | 198 | 1/1 | 1/0 | 0 |
| after-inflight | 198 | 1/1 | 1/0 | 0 |
| machine | 481 | 11/11 | 11/0 | 0 |
| worker-withdraw | 6203 | 301/301 | 301/0 | 0 |
| 合计 | **26920** | 各进程最终 InFlight=0 | 原始记录分别保留 | **13个0** |

`normal` 真正调用 native Replay/Resume：stock body 调用已挂接的 SetPitch(1) 与 Double1 PlayScheduled，leaf 记录原64位 start 和 physical parent；outer OriginalReturned observer 再实际调用已挂接 SetPitch(2)。检查 Replay media reset=0、Resume saved media=7.25、schedule=200.5、origin=193.25、对应 parent/native sequence、正常调用数和 no-op 不产生 schedule。该 observer 是测试代码；没有构造正式 Gameplay Clock、provider、七根或 SDK 值事务。（E-NORMAL、E-SOURCE）

`bits` 覆盖 Single `+0/-0`、非整数尾位、正负 infinity、quiet/signaling NaN 的原32位，Double 原64位（含 NaN）、reference/null、Boolean `0/1/2/255` 和不同/non-null/null incoming MI。native original 的实际 store 与 Frame 参数逐位相符。`inline-pause` 从 Boolean entry 实际调用 Pause leaf，MUG-shaped Pause 计数仍0，leaf parent 为该 Boolean occurrence。（E-BITS、E-INLINE、E-SOURCE）

`original-seh` 先让 setter 实际写入2再抛 `0xe2400011`，外层 caller 收到同 code，pitch original 恰一次；之后 schedule 写入后抛 `0xe2400012`，leaf 与 Replay outer 都 abnormal，最终4 Entered/4 Exited、Returned1/Abnormal3、InFlight0。这里验证指定 Windows SEH code 与正常/异常 completion 分支，不将结果外推为任意 C++ 异常类型或异常 payload 的完整比较。原始行如下（E-SEH）：

```json
{"mode":"original-seh","passed":true,"checks":278,"failures":0,"actualDobbyPrepared":8,"actualDobbyCommitted":8,"callbacks":8,"nativeEntered":4,"nativeExited":4,"nativeInFlight":0,"before":4,"originalReturned":1,"abnormal":3,"stockShapedFixture":true,"gameCodeExecuted":false,"productionAdmission":false}
```

`machine` 使用独立 MASM caller 设定并比较 RBX/RBP/RSI/RDI/R12–R15 八个非易失 GPR 和 XMM6–XMM15 十个完整128位寄存器，覆盖五种 typed ABI/八个 role；调用产生的 nested leaf 也实际进入 source。RFLAGS 检查仅为 Windows x64 的 DF=0，不主张所有 arithmetic flags 位都作了前后等值比较。（E-MACHINE、E-SOURCE）

```json
{"mode":"machine","passed":true,"checks":481,"failures":0,"actualDobbyPrepared":8,"actualDobbyCommitted":8,"callbacks":22,"nativeEntered":11,"nativeExited":11,"nativeInFlight":0,"before":11,"originalReturned":11,"abnormal":0,"stockShapedFixture":true,"gameCodeExecuted":false,"productionAdmission":false}
```

## 4. Evidence → Finding → Path

以下 finding 的 severity 均为 `n/a_re`、confidence=`high`、status=`validated`；验证范围按所列位置限定，每项具有静态源码与独立实际进程或二进制原件支撑。E-id 的 `source_ref/repro_command/content_hash/raw_excerpt` 完整保存在 index。

| Finding | location / 具体结论 | evidence_ids |
|---|---|---|
| F-CURRENT | `PlaybackSourceContext.cpp: Enter/Notify/ReadCurrent/VerifyCurrent`：凭据来自实际 typed callback TLS；复制 frame 不产生新 native occurrence | E-SOURCE、E-RUN、E-BITS |
| F-EXIT | `Exit` 与 `after-inflight`：OriginalReturned 回调仍有在途责任，physical receipt 只在 native finally 后存在 | E-SOURCE、E-AFTER、E-SEH |
| F-JOURNAL | `Enter/SetArmed/ReadReceipt`：dormant/withdrawn 不占新 journal；已进入 armed 责任保留，overflow/foreign/observer fault sticky Unknown | E-SOURCE、E-WITHDRAW、E-JOURNAL、E-FOREIGN、E-OBSERVER |
| F-ABI | `Entry*`、`StockFixtureInvoke`、`PlaybackSourceCaller.asm`：五 typed ABI 的参数/MI、非易失机器状态和指定原 SEH 路径保持 | E-SOURCE、E-BITS、E-MACHINE、E-SEH |
| F-STOCK | `PlaybackStockFixture.cpp` 与测试 `Observe/Normal`：真实 native stock-shaped nested leaf/ABA/pitch恢复2已执行；恢复者是测试 observer | E-SOURCE、E-NORMAL、E-NESTED、E-INLINE |
| F-IDENTITY | native r4 PE/PDB/COFF/C13：三个实际 PE/PDB、五个 TU 与十个 owned 固定版本可追溯；不代替后续全树 | E-BUILD、E-SYMBOL、E-OWNED |
| F-BOUNDARY | `scope` 与 r4 fixture：自有八入口不等于游戏 stock 全来源、生产 FirstInstall 或最下 engine writer；r4 无 executable DIR64 项 | E-SOURCE、E-SYMBOL、E-DIR64 |

**P-NATIVE，`path_type=callflow`**：start 为自有 native caller；goal 为获得 actual source facts 并在真正 native completion 后释放其 exact journal 责任。

1. 固定 native PE/PDB/C13 与 owned 输入 → actual Dobby Prepare/Commit 八个 source targets（E-BUILD、E-SYMBOL、E-RUN；F-IDENTITY）。
2. caller 进入 typed entry → native Before/TLS/parent/fence/参数保存 → observer 验 actual current（E-SOURCE、E-BITS；F-CURRENT、F-ABI）。
3. original native store/call → 真实 nested pitch/schedule/pause leaf → normal/SEH 分支（E-NORMAL、E-INLINE、E-SEH；F-STOCK、F-ABI）。
4. OriginalReturned/Abnormal observer 保持在途 → native finally 结束 TLS、写 physical receipt → exact release（E-AFTER、E-WITHDRAW、E-JOURNAL；F-EXIT、F-JOURNAL）。
5. residual_risks：真实 GameAssembly source 图/最下写者/FirstInstall、正式 provider/Clock 联合、共同组 ASLR+executable DIR64、跨局 handoff 与真实流程均由其他工作项各自验收；本路径终点不签这些 authority（E-DIR64；F-BOUNDARY）。

## 5. 固定十个 owned 输入

以下快照位于本目录 `sources/`，逐项 hash 与 `native-owned-inputs.json` 相同；build 时输入/frozen 源前后不变，test 时原始/冻结输入前后不变。十个 owned 仅是本任务实现与检查脚本清单；MSVC/Windows SDK 的实际编译头文件还分别列于 C13 证明。（E-OWNED、E-BUILD、E-SYMBOL）

| owned 文件 | SHA256 |
|---|---|
| `native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.h` | `397bdfb6005ec458834fe97b968861280cceea1fa0912cf05d88003529c1487d` |
| `native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp` | `be101e1eb42061f125f5dcc417ec3d9bf6310f56a0ab67efeff1a71545fa6229` |
| `native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceFixture.cpp` | `3b9ea40311c426b93d4755cad23aa33f7e524583d23af427caccfd9f517b0229` |
| `native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.h` | `6778133f868859db97343140c7bcd19509638112c2df7f76d1eba123f916d918` |
| `native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.cpp` | `71ab1e2725d46b9572dd437d0763f2a5cd974e1d9f69463d57cdbad97be954f4` |
| `native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.asm` | `615d6396f44a9c063694858058ed187c98622f3255276d9315329a960633820d` |
| `native/RizToolbox.PlaybackStockFixture/PlaybackSourceCaller.asm` | `a7b68e7f819a17f926d5cea1f71364ff6d8ce4432a8fd06029ee15627e6733a9` |
| `scripts/Build-PlaybackSourcePrototype.ps1` | `d3f421da5fc4eb44fdc8b244cc59c452d27bd91a1cad046601e7fd3e654cafb7` |
| `scripts/Test-PlaybackSourcePrototype.ps1` | `b34181b8d88b61730695cc37c6b05767a6f2162e642ae1b41de925dfef7ecf5a` |
| `scripts/Verify-PlaybackSourcePrototype.py` | `27398dab6b1b263729c11b3a8204be49c699c1c3f030f5dbac3d4bc6e94182ab` |

## 6. 失败保留与复现

r1/r2 都有 link exit0，但 ASM `PUBLIC` 未经 `/EXPORT` 导出成 DLL target，只有 getter 不能执行真实入口。checks-r1 对 r2 的八条已保存 child exit 均为 `0xc0000005`，每份 stderr 指向 target=null 和 Dobby Prepare 失败；中断留下的空日志没有 exit，不冒认完成。r3 增加显式 exports 后旧 checks-r2 为10进程/20028；当前 r4/checks-r3 才包含 worker withdraw、After 窗口与 ASM caller。这些目录与 raw 证据未改写，不合并旧成功/失败计数。（E-HISTORY）

现有 raw 结果可只读验证：

```powershell
python C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-native-20261031/Verify-Report.py
```

独立复现可在新的、原先不存在的 D 盘目录运行以下命令。脚本均要求新输出路径，旧失败/成功目录会保留。此处提供复现路径，报告阶段没有执行这些构建或26920测试命令：

```powershell
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-native-20261031/sources/scripts/Build-PlaybackSourcePrototype.ps1' -OutputDirectory 'D:/riz-build-cache-next/playback-stock-native-independent-rfresh-20261031'
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-native-20261031/sources/scripts/Test-PlaybackSourcePrototype.ps1' -BuildDirectory 'D:/riz-build-cache-next/playback-stock-native-independent-rfresh-20261031' -DobbyPath 'D:/riz-build-cache-next/playback-stock-native-checks-r3-20261031/inputs/dobby.dll' -OutputDirectory 'D:/riz-build-cache-next/playback-stock-native-independent-checks-rfresh-20261031'
python 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-native-20261031/sources/scripts/Verify-PlaybackSourcePrototype.py' --build 'D:/riz-build-cache-next/playback-stock-native-independent-rfresh-20261031' --dobby 'D:/riz-build-cache-next/playback-stock-native-checks-r3-20261031/inputs/dobby.dll' --output 'D:/riz-build-cache-next/playback-stock-native-independent-rfresh-20261031/native-symbol-source-proof.json'
```

MSVC 入口为 `C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/VC/Auxiliary/Build/vcvars64.bat`，实际命令行在 E-BUILD；Python 路径为 `C:/Python314/python.exe`，使用本机现有 pefile。无需网络或工具安装。下一采用应消费本分包 fixed native source 事实，并在各自 scope 内验证共同安装组、真实 metadata/roots/source、唯一 Clock/正式 provider 和生产首次准入；这些结果由根任务分包整合，本报告不重复发行。
