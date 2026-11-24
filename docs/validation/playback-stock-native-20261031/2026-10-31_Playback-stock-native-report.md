# Stock source native 原型与执行区 DIR64 验证报告

2026-10-31，`flavor=null`。当前独立 native **r6 的 13 个进程、27037 项检查全部 actual exit 0**，每进程实际 Dobby Prepare/Commit 八个自有 source 入口。新增 `.text` 中的真实绝对地址由 COFF `ADDR64`、PE `DIR64`、实际非首选地址映射后的值三层证据确认。原型 helper 协议、TLS、fence、journal 与原异常流程未改；新增运行检查只核 fixture 的映像身份和 relocation。r4、r5 以及更早失败均单独保留。真实 GameAssembly 方法与其 DllMain 执行数均为 0；生产准入为 false。

授权和边界见 [scope](scope.md)，顺序见 [timeline](timeline.md)。本包不提供正式 current provider；normal 中恢复 pitch=2 的执行者仍是测试 return observer。八个 source wrapper 的实际 Dobby 检查不能代替真实游戏各目标的独立搬移、展开证明，也不能把共同组的两个成员写成九个。

## 1. 当前实物与来源

原件是 `D:/riz-build-cache-next/playback-stock-native-build-r6-20261031/` 与 `D:/riz-build-cache-next/playback-stock-native-checks-r6-20261031/`。完整实际输入、冻结输入、PE/PDB/COFF、日志、exit 和历史输出均有 [artifact-index](artifact-index.json) 中的路径、字节数、SHA256；索引显式排除自身。报告及索引另外由 [native-report-integrity](native-report-integrity.json) 封存，同样不含自身 hash。十个 owned 源和脚本的当前 hash 与 [sources-r6](sources-r6/) 快照见 [native-owned-inputs](native-owned-inputs.json)。

| 实物 | PE SHA256 | PDB SHA256 |
|---|---|---|
| `RizToolbox.PlaybackSourcePrototype.dll` | `a96160f46ca952ed0b6097478dc44efff840127d52d65d3ea43e5ba01c314083` | `15f263de00b083621c29c49bf343c9d5e1d347c12940c3d2b8fcc7f917e2e98b` |
| `RizToolbox.PlaybackStockFixture.dll` | `cc45d2a47f99180443fa1d2a839c0567c66cd7e7bacdb7a59c9af346d53c29fc` | `e5f5a1dbd4ce401b3fd3d7da572b621fc9dd12e5ed4aa59d24a5710ea4b4f7d5` |
| `PlaybackSourceFixture.exe` | `e47a3f32523b073fc4bac8ab3c27c74c9786cb2bbefa59d6811b1a539290b1ce` | `164fc51996d20e99b64f14c71fd3021e6008593df13c45d9774373a02c0ea45c` |

固定 Dobby SHA256 为 `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded`，不主张其 PDB 来源。三个 PE 的 RSDS GUID/age 与真实 native PDB info stream 相符；五个实际 object 的 C13 checksum 逐项回读编译源码与头文件，均一致。此为实际 PE/PDB/COFF 证明，不能由文件名相同代替。（E-BUILD、E-SYMBOL）

五个 TU 的路径、object hash 和每 TU flags 在 [build-result](evidence/r6/build/build-result.json)。三个 C++ TU 使用 `/nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Z7 /guard:cf`；两个 MASM TU 使用 `/nologo /c /Zi`。link 使用 `/DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF`，DLL 另有 `/DLL`。这些是请求的构建 flags，不据此宣称所有运行时防护已做独立攻击验证。

### 导入、导出与目标

[实际 imageSurveys](evidence/r6/build/native-symbol-source-proof.json) 保存四个映像的完整 import/export 列表；dumpbin 原件也保留。helper 依赖 KERNEL32、VCRUNTIME140 和 CRT runtime，12 个导出；fixture 另有 VCRUNTIME140_1/heap，24 个导出；runner 使用 Windows 动态模块、内存、线程、异常和读取自身冻结 DLL 头所需文件 API及 CRT；Dobby 的静态 import 为 KERNEL32、7 个导出。本包加载的是自有 helper/fixture 和固定 Dobby，未加载或调用 GameAssembly。自有 DLL 的正常 CRT 初始化与禁令中的真实 GameAssembly DllMain 是不同范围。（E-IMPORTS）

`StockGetTime` 为 RVA `0x1730`、640B，头 `48 89 5c 24 08 57 48 83 ec 50`、body SHA `c493c58e3dd4d4c960a2c4a2290219d6ac36c45a1082808638d254a0744cb742`。`StockSetPitch` 为 RVA `0x19b0`、320B，头 `40 53 48 83 ec 30`、body SHA `3df79afe3b25d631b39faa718c87fc421a8b0ac262fed000f85e17952e7308d2`。两者 end 分别为 `0x19b0`/`0x1af0`，SetPitch unwind 前八字节为 `01 06 02 00 06 52 02 30`；均与 r4 相同。新增 anchor 未改变这两个 body/prologue/unwind。（E-SYMBOL、E-SOURCE）

## 2. 新增的真实执行区 relocation

ASM 在全部 `PROC/ENDP` 之后定义 `StockExecutableDir64Anchor LABEL QWORD`，内容为 `DQ OFFSET StockSetPitch`；Build 显式 `/EXPORT:StockExecutableDir64Anchor,DATA`，没有新增可调用原型，也未改正式 V7 helper。

| 层 | 实际证据 |
|---|---|
| COFF | `PlaybackStockFixture.asm.obj` 的 executable `.text$mn`，anchor offset `0x740`；唯一 relocation 为 `IMAGE_REL_AMD64_ADDR64=1`，symbol index 28=`StockSetPitch`，原始 addend=0 |
| PE 文件 | anchor RVA `0x1e70`，位于 executable `.text` 且在所有 runtime-function 范围外；唯一对应 base relocation 为 `IMAGE_REL_BASED_DIR64=10`；磁盘 preferred base=`0x180000000`，保存值=`0x1800019b0` |
| 实际映射 | 每个 r6 进程均确认 `MEM_IMAGE`/RX `.text`、实际 base 不等于磁盘 preferred、anchor 值等于该映像实际 `StockSetPitch` 地址。normal 记录 base=`0x7ff8f2b00000`、anchor=`0x7ff8f2b019b0` |

独立 Python verifier 只解析文件，不负责映射执行；实际加载证明来自十三个 native 进程。两者证据分别记录，不把静态解析称为实际执行。新增每进程九项身份/头/页/重定位检查，因此 27037=原有26920+117；增加的数字不代表新增117种 source 行为。（E-DIR64、E-RUN、E-RELOCATION）

## 3. 保持的 source 协议与责任

独立协议 v1 仍为 pack8，Frame160B/Info136B，五种 ABI（Void0、Single1、Double1、Ref1、Bool1）、八个 role、owner8/slot16/journal256。static `SetPitch(AudioSource,Single)` 与 instance `set_pitch(Single)` 的两个 metadata alias 共用一个 physical target。Single低32位、Double全64位、ref/null、Bool低8位、incoming MethodInfo 与 caller/TLS parent 均按实际入口保存和转发，没有手工 Begin/End 发行口。（E-SOURCE、E-BITS）

Before 先推进 writer/source/scheduling fence；同值、pitch/clip ABA、nested equal 均保留新的 native sequence。current 验证限定真实 TLS callback 窗口、owner/epoch/kind/slot/thread 和完整 frame；改写 ArgBits 的复制 frame 被拒。Before 或 OriginalReturned/Abnormal observer 内没有 physical receipt；observer 全部返回后 native `__finally` 才结束 TLS、减少 InFlight、增加 Exited 并写 exact receipt。原异常保留原 code 和原调用次数，observer fault 由独立边界隔离，不替换原异常。（E-SOURCE、E-BITS、E-AFTER、E-SEH）

只有 entry 时 armed 的调用预留持久 journal。Dormant/Withdrawn 调用仍 forward、推进 fence/Entered/Exited，但不占新行；已进入 armed 的责任在 withdraw 后仍完整保留。Arm 限 owner thread、无在途且一次成功；永久 withdraw 核 private owner+epoch，允许 worker。overflow 第257次不覆盖旧256行；foreign、observer fault 和 overflow sticky Unknown，不能再 Arm 复活。（E-SOURCE、E-WITHDRAW、E-OVERFLOW）

八个实际 source targets 均先核 fixture 六字节 Dobby 搬移和 continuation，再使用本 fixture 独立匹配且有动态 unwind 的 RX R11 thunk。该证明仅适用于这些自有 wrapper，不把 SetPitch/GetTime 的形状外推给真实游戏其他入口。

## 4. 十三个独立进程

[完整 test-result](evidence/r6/checks/test-result.json) 与逐模式 stdout/stderr/exit 均保留；每进程 Prepared=8/Committed=8，全部 stderr 为0B、exit文件为字面 `0`，最终 InFlight=0。这里没有 GetTime/group Commit 计数。（E-RUN、E-EXITS）

| 模式 | checks | Entered/Exited | Returned/Abnormal | exit |
|---|---:|---:|---:|---:|
| normal | 531 | 13/13 | 13/0 | 0 |
| bits | 779 | 22/22 | 22/0 | 0 |
| nested-aba | 451 | 10/10 | 10/0 | 0 |
| original-seh | 287 | 4/4 | 1/3 | 0 |
| inline-pause | 227 | 2/2 | 2/0 | 0 |
| dormant-withdraw | 12179 | 600/600 | 600/0 | 0 |
| foreign-thread | 204 | 1/1 | 1/0 | 0 |
| observer-fault | 201 | 1/1 | 1/0 | 0 |
| journal-overflow | 5062 | 257/257 | 257/0 | 0 |
| inflight | 207 | 1/1 | 1/0 | 0 |
| after-inflight | 207 | 1/1 | 1/0 | 0 |
| machine | 490 | 11/11 | 11/0 | 0 |
| worker-withdraw | 6212 | 301/301 | 301/0 | 0 |
| 合计 | **27037** | 各进程分别核验 | 原始结果独立保存 | **13个0** |

normal 实际经过 Replay/Resume→SetPitch(1)→Double1 PlayScheduled→测试 return observer SetPitch(2)，核 Replay media reset=0、Resume saved media=7.25、schedule=200.5、origin=193.25 与 physical parent；stock no-op 不产生 schedule。inline-pause 从 Bool entry 实际进入 Pause leaf，MUG-shaped Pause 计数仍0。bits 核 Single ±0/尾位/infinity/quiet与signaling NaN、Double raw64、ref/null、Bool0/1/2/255 和 incomingMI。original-seh 对写后抛 `0xe2400011/0xe2400012` 的实际 caller code、原调用次数与 abnormal completion 核验；不外推任意 C++ 异常 payload。machine 使用 MASM caller 核八个非易失 GPR、XMM6–XMM15，RFLAGS 只核 Windows x64 DF=0。（E-NORMAL、E-INLINE、E-BITS、E-SEH、E-MACHINE）

## 5. Evidence → Finding → Path

[结构化 evidence-index](evidence-index.json) 给出每条 E 的 source_ref/repro_command/content_hash/raw_excerpt，以及每条 F 的 severity/category/status/evidence_ids/location/impact/confidence。此处所有 finding 均 severity=`n/a_re`、confidence=`high`、status=`validated`，范围限定为该自有原型。

| Finding | 结论 / location | evidence_ids |
|---|---|---|
| F-CURRENT | `Enter/Notify/VerifyCurrent`：当前票来自实际 typed callback/TLS；复制 frame 不产生新 occurrence | E-SOURCE、E-BITS、E-RUN |
| F-EXIT | `Exit`/`after-inflight`：Returned 回调仍在途，physical receipt 只在真实 finally 后出现 | E-SOURCE、E-AFTER、E-SEH |
| F-JOURNAL | `SetArmed/Enter/ReadReceipt`：armed-only持久责任、worker撤回与旧责任保留、Unknown不复活 | E-SOURCE、E-WITHDRAW、E-OVERFLOW |
| F-ABI | typed entry/MASM caller：五 ABI raw参数、非易失状态、指定原SEH保持 | E-SOURCE、E-BITS、E-MACHINE、E-SEH |
| F-STOCK | 测试 fixture 的真实 nested leaf/ABA/reset恢复2已执行；恢复者是测试 observer | E-SOURCE、E-NORMAL、E-INLINE |
| F-DIR64 | `.text$mn`ADDR64→PE.text DIR64→实际非preferred映射值闭合；不改 target body/unwind | E-SOURCE、E-DIR64、E-RELOCATION、E-RUN |
| F-IDENTITY | 三个PE/PDB、五个COFF/C13与十个owned固定版本可追溯 | E-BUILD、E-SYMBOL、E-SOURCE、E-EXITS |
| F-BOUNDARY | 自有八入口不等于游戏stock全图/完整enginewriter或正式provider；共同组结果另包验收 | E-SOURCE、E-SYMBOL、E-HISTORY |

**P-NATIVE，path_type=callflow**：自有 caller→实际 Dobby八source entry→Before/TLS/fence/raw参数→original和nested leaf→Returned/Abnormal observer→native finally→physical exact receipt/release。各步分别挂 E-BUILD/E-SYMBOL、E-SOURCE/E-BITS、E-NORMAL/E-INLINE/E-SEH、E-AFTER/E-WITHDRAW。终点只获得该原型事实，不签真实 GameAssembly source/roots/run/Clock/provider authority。

**P-DIR64，path_type=callflow**：ASM `DQ OFFSET`→object ADDR64 symbol→link磁盘preferred值和PE DIR64→native加载非preferred地址→RX `.text` anchor读取与实际target比较；静态链挂 E-SOURCE/E-DIR64，动态链挂 E-RELOCATION/E-RUN。residual_risks：此结果提供共同组验证的真实fixture输入；共同组是否持续保留完整file+reloc检查由其独立结果决定。

## 6. 保留的失败与限制

r1/r2 的 link exit0 没有导出 ASM DLL target，checks-r1 八条已记录 child 为 `0xc0000005`；中断后的空日志无 exit，不补终态。r3/checks-r2 的旧10进程/20028、r4/checks-r3 的13进程/26920均独立保留；r4没有 executable DIR64 项，不能据r6补写其覆盖。最初四份r4文字报告原样保存在 `history/r4-reported/`；当时引用但未生成的本地证据文件不当作既成产物。（E-HISTORY）

r5编译 exit0，已经生成真实 `.text` DIR64；新检查错误地将该机加载后内存头的 ImageBase当成磁盘preferred，13进程均仅新增两断言失败、actual exit1，结果完整保留。独立 verifier 当时也错误地只接受 object `.text`，实际 MASM贡献为 `.text$mn`。r6改为读取冻结DLL文件头与接受确切 `.text$mn`，在新目录完成成功；没有覆盖r5或把其exit1算为通过。（E-HISTORY、E-RELOCATION）

本包不证明生产FirstInstall、真实七根/run/source生命周期、跨局交接、最下层enginewriter或直接字段写覆盖、唯一Clock构造/正式currentprovider、真实Speed/Seek和完整B01–B19。真实Pause/Replay/Resume/Progress.Play/AudioSource.Pause/PlayScheduled/set_clip的正文、搬移与展开须分别限定；共同组两个成员不能因八个ownedwrapper测试而扩写成九成员。没有执行游戏安装/启动/UI/账号、没有安装工具、没有修改正式V7/helper或root checkpoint。

## 7. 复现与只读检查

本次实际执行下列三个命令；外层 actual exit依次为0、0、0。输出路径是本次唯一新目录，脚本会拒绝覆写已有目录/证明文件。

```powershell
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File 'C:/Users/71957/Desktop/riz_toolbox/scripts/Build-PlaybackSourcePrototype.ps1' -OutputDirectory 'D:/riz-build-cache-next/playback-stock-native-build-r6-20261031'
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File 'C:/Users/71957/Desktop/riz_toolbox/scripts/Test-PlaybackSourcePrototype.ps1' -BuildDirectory 'D:/riz-build-cache-next/playback-stock-native-build-r6-20261031' -DobbyPath 'C:/Users/71957/Desktop/riz_toolbox/.tools/bepinex-789/BepInEx/core/dobby.dll' -OutputDirectory 'D:/riz-build-cache-next/playback-stock-native-checks-r6-20261031'
& 'C:/Python314/python.exe' 'C:/Users/71957/Desktop/riz_toolbox/scripts/Verify-PlaybackSourcePrototype.py' --build 'D:/riz-build-cache-next/playback-stock-native-build-r6-20261031' --dobby 'C:/Users/71957/Desktop/riz_toolbox/.tools/bepinex-789/BepInEx/core/dobby.dll' --output 'D:/riz-build-cache-next/playback-stock-native-build-r6-20261031/native-symbol-source-proof.json'
```

第三方复现时将所有build/check/output参数换为新的绝对D路径；若使用已冻结源码，将三个脚本路径改到本目录的 `sources-r6/scripts/`。MSVC入口为 `C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/VC/Auxiliary/Build/vcvars64.bat`；Python为本机既有3.14，pefile版本及实际模块/解释器hash在E-SYMBOL中。IDA工具清单为不可用，遵守不安装约束，使用既有dumpbin和独立解析器；没有声称完成IDA反编译。

现有报告与全部原件只读复核命令：

```powershell
& 'C:/Python314/python.exe' 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-native-20261031/Verify-Report.py'
```
