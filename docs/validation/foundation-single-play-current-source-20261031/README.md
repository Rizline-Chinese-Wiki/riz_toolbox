# 当前 core-r3 源码上的 Play nonempty duplicates 重验

新 `single-play-current-source-r1-20261031` 已从 root 最终 core-r3 的完整 source proof 复制并编译当前源码，native build、managed build 和唯一 `automatic-duplicates` 进程均 **actual exit 0**，strict 合同通过。704 个 root snapshotFiles、704 个对应 live inputs、94 个当前 native 输入及4个 overlay 的 hash 全部核实，最终802文件的 archive 和前后快照一致。该重验是当前 Runtime/GetTime 修复后的同一个 duplicates 模式，**不增加覆盖模式数**；未重跑旧 instant、七模式或退休矩阵。

旧 [r4 报告](../foundation-single-play-nonempty-20261030/README.md) 与 D 盘原件保留。旧 r4 664文件不能验收当前源码；本页只为当前 proof 建立新的来源、编译和执行证据，不覆盖旧结果。范围见 [scope.md](scope.md)，实际过程见 [timeline.md](timeline.md)，完整路径/hash在 [evidence-index.json](evidence-index.json)。`flavor = null`。

## 采用的当前来源

显式 proof 为 `D:/riz-build-cache-next/foundation-broker-source-core-r3-20261031/test-result.json`，SHA-256 `2f81d0004e84309af6c56bb7987c62be04d1c370f55e59c0c9f5b4d9a44f54bd`。新 runner 的 `--source-manifest` 为必填参数，没有旧快照 fallback；先验证 kind、passed、snapshotCaptured、runnerExitCode=0、child.passed，再复制并核全部 snapshotFiles，并核对应 live inputs 在执行前后都符合此 proof。

managed 当前来源704项来自该 proof；native 不在 root managed proof 中，所以另外从当前仓库复制 JudgeContext 的全部94个 `.cpp/.h/.hpp/.inl/.asm/.def` 输入并留存独立清单。三个实际执行 fixture 逐字节复用旧 r4；第四个 overlay 是新 mandatory-manifest runner。旧 runner 在新 work 中仅保留作来源证据，没有改写或运行旧 runner。7项测试接线变换只作用于新 D 快照并逐项保存 exact old/new 与 before/after hash，repository 的 `src/tests` 未修改。

| 来源/结果 | 实际证据 |
|---|---|
| root snapshot/live inputs | 704/704，全部复制前、复制后、运行后 hash 核验 |
| 当前 native 源/header/ASM/include | 94项全部冻结并核运行后 hash；实际逐 TU flags/源hash均记录 |
| D-only overlays | 3个 r4 fixture 同字节 + 1个新runner；独立origin清单保留旧4overlay来源 |
| 最终 source tree/zip | 802文件；source-before=source-after，ZIP每项逐字节hash复核 |
| managed build | actual exit0，26个既有 fixture warning，0 error；以清空packageSources的NuGet.Config构建 |
| 当前编译源码证据 | Runtime/Adapter/AudioContent.Tests 三个实际portable PDB的17份source document SHA-256 与冻结来源完全相等 |

PDB复核包括实际 operation broker、正式 Single/Play reader、source/reference reader、当前 MemoryFact 修复文件 `NativePlaybackExecutableSeal.cs`、`ClockPublication`、`ActualSource`、`OccurrenceRateProvider` 和 D-only tests。当前 `NativePlaybackExecutableSeal.cs` 的 root proof、snapshot 与实际 Runtime PDB document SHA均为 `fa076cc45ca96f2d61f045fcab0e66a6c5c06a11eca555083950b72f0d3464b2`。这是当前代码被编译的来源证明；本 duplicates 进程没有执行 GetTime detour，不用它替代独立 GetTime 行为验收。

## 同一正向链的实际结果

实际 `Update24 → Execute19 → Single31 → Play25 → Add20×2`，随后两个 bounded native Update 各消费一个 duplicate。native Update/Execute 各3次、Single/listener/Play各1次、Add2次，所有lane entered/exited匹配，InFlight/Unknown/EnterFaults/ExitFaults=0。20条真实source记录/10个完整事务，83次实际TLS `ReadCurrentNotice`，5个direct reference roots/81次root后class验证；所有reference scopes正常关闭，unknown/fault/rejected-read=0。

| 阶段 | native ready count/version | admission1 / admission2 QueueExited | ordinary completion interval |
|---|---|---|---|
| listener/Play返回及受控death后 | 2/3 | false / false | 不可借用 |
| 第一个真实bounded Update退出occurrence4 | 1/4 | false / true | **仍 Pending** |
| 第二个真实bounded Update退出occurrence3 | 0/5 | true / true | **可借用并验证** |

private child1 transfer仍引用actual PlayObservation3、sourceEpoch1/cutoff1及`[2 InstantWork,3 admission1,4 admission2]`，同Tween两个admission occurrence保持独立。source14实际标签后的字节为E8、actual Execute Caller=relayReturnPc+5，Single/reader真实执行，不补造frame、source row、occurrence或完成条目；无人工Begin/End或SetReady。

执行站点仍仅Update的0/2/6/14；PlayOnStart/PlayOnComplete19/20和Execute21实际记录均为0，不扩大为这些站点覆盖。metadata/source注册、graph metadata/generation/effect输入、managed admission lease与death仍受控；5个reference Root由受控RootHandle后端承载，9次store后CALL是复用自有native barrier model，不验证真实IL2CPP GC或Unity死亡。Production/source/reference均false，native admission验证false。未安装/启动游戏，未操作GUI或账号，未签Ready、retirement、Unregister或root release。

## 复现与证据

新执行观察时间：`2026-10-31T02:58:09.153608+00:00`。新原件目录 `D:/riz-build-cache-next/single-play-current-source-r1-20261031/`。复现输出必须原先不存在；若当前 live 已不同于指定 proof，runner会直接拒绝，必须由root再冻结对应新proof，不能退回旧源码。

```powershell
C:/Python314/python.exe D:/riz-build-cache-next/single-play-current-manifest-work-20261031/run-current-duplicates.py --source-manifest D:/riz-build-cache-next/foundation-broker-source-core-r3-20261031/test-result.json --output D:/riz-build-cache-next/single-play-current-source-replay-20261031
```

source/ZIP/PDB只读复核脚本为新work的 `audit-current-source.py`，本次actual exit0，没有额外测试进程；其输出E-CURRENT-008含17份实际PDB document hash。三个build/test actual exits、命令、stdout/stderr、generated build.cmd/NuGet.Config hash均在E-CURRENT-001。

| Evidence | 原件，相对新输出目录 | SHA-256 |
|---|---|---|
| E-CURRENT-001 | `test-result.json` | `fa0d31b58d4dd699205dca821323f7ab53a56b425e9087886af0358523612c20` |
| E-CURRENT-002 | `source-snapshot.zip`，802完整源输入 | `cdf7e8243fca742eaa02b69128e10a85cd6de09cdb00df05fe884f7f88aacd5b` |
| E-CURRENT-003 | `source-before.json` = `source-after.json` | `1cb102fbae7739f6bebf4fb9bfdd2cde6a18ea5db1e5b8393035f1c56bf47771` |
| E-CURRENT-004 | `source-proof-copy-inputs.json`，704当前proof来源 | `763fb95986beaefc794eef84f07b3eba215ed64298caa13e3b1f1b10ad0c6eb6` |
| E-CURRENT-005 | `native-current-inputs.json`，94当前native来源 | `3c987f38b449456af382e2e7acfecd8470842532d2540cf7871486b29f0614f9` |
| E-CURRENT-006 | `overlay-inputs.json`，新4overlay | `2586902696652e936d9f42186ef8b3e0218ba00595187bc29033be76fb43d53a` |
| E-CURRENT-007 | `automatic-duplicates.stdout.log` | `d000dbf98a0b9014ca7782c9a8a4b105a2bfa98d2000c06b26041d0f27ed4dfa` |
| E-CURRENT-008 | `post-run-source-audit.json`，source/zip/PDB独立核验 | `e052035eb062a352bcec736b19687d13493cc8fd5ae9b958a3c7309512973dd9` |

新native DLL SHA `9a080ba602220f9f155e0f1b739a3b86f5387323748ce83ac1c6e594f030f680`，测试程序集SHA `79aef3b1313d8fdddb70e63044008385555a04769b569f968af0ad0961f1cd71`；实际文件路径/大小以及三个assembly的PDB SHA详见索引。observed_at取执行时间，source_type=file/log，linked_workitem=`WI-SINGLE-PLAY-CURRENT-SOURCE`，repro_command为上述新runner。

F-CURRENT-001：status=validated，category=other，severity=n/a_re，confidence=high，evidence_ids=[E-CURRENT-001,E-CURRENT-002,E-CURRENT-003,E-CURRENT-004,E-CURRENT-005,E-CURRENT-006,E-CURRENT-008]。位置为 mandatory-manifest runner及当前Runtime/Adapter实际PDB。当前root core-r3源码与native输入已独立重新编译；同一actual outer/nonempty/Add/queue正向在该来源上通过，不能借旧r4绿灯代表新源码。repro_steps为上述命令；remediation=n/a。

F-CURRENT-002：status=validated，category=other，severity=n/a_re，confidence=high，evidence_ids=[E-CURRENT-001,E-CURRENT-007,E-CURRENT-008]。位置为actual lanes24/19/31/25/20、正式Single/Play reader、私有transfer和source occurrence账本。两个同指针admission仍逐项等待，首次退出Pending、第二次才取得ordinary completion interval。受控与未执行边界如上，retirement仍未证明。

P-CURRENT-001：path_type=callflow；start=core-r3全部snapshotFiles及94当前native输入；goal=当前编译来源上的同一duplicates合同。步骤为显式proof/hash复制 → 4overlay/7项D-only变换 → 实际native/managed构建 → actual Update/Execute/Single/Play/Add×2 → 两次native queue退出与原账本验证 → archive与17份PDB document hash核对。每步关联E-CURRENT-001/002/004/005/006/007/008，结论对应F-CURRENT-001/002；剩余production、实际Unity/IL2CPP、完整LOCAL与B01–B19边界未由该模式消除。
