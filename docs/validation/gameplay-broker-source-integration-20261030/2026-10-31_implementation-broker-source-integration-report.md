# GetTime、调度来源与非空 Play 联合实现报告

当前固定树的 27 套托管回归 **2465/2465**、六组件 Release、解决方案 **0 error / 0 warning**、独立 SDK 两插件 **14/14** 均 actual exit0。新增 GetTime broker 与完整代码 profile 可组合工作，owned native producer 的私有 occurrence/schedule/writer 可由具体 provider 接入正式玩法事务。Play 非空联合已重编当前源码，通过真实自有 native Update/Execute/Add/source/reference 调用。

这些结果完成本批离线实现和限定验证。**完整 N2、生产 provider、任意 Seek 与 B01–B19 未完成**；真实 Speed/Seek 和首次生产准入保持关闭。本批不安装/启动游戏，不操作 UI/账号，不创建新的可安装游戏候选。`flavor=null`；授权和映像副本边界见 [scope.md](scope.md)。目录沿用 2026-10-30 的批次名，首次整合观察时间为 `2026-10-31T03:12:30.785174+00:00`，最新hash回核时间见摘要。

## 功能和缺口

| 本批路径 | 实际实现与证据 | 保留的边界 |
|---|---|---|
| GetTime broker | 唯一地址 registry 的私有 owner/lease；实际 Dobby Prepare/Commit、精确 metadata/ABI、完整原正文、patch/relay/slot/尾部、原跳板、19B R11 等价回原跳板和 Windows unwind；在途、SEH、withdraw、异常隔离 | 单目标隔离安装；D-only 返回 helper 原型，未替换正式 V7 helper；不签首次生产准入 |
| 完整 clock profile | `GameplayClockCodeProfile` 保留固定五正文 pin；仅 GetTime 使用 broker lease 承认实际 patch，验证原640B；其余 producer/converter/Progress consumer完整匹配。NativeProfile、Backend 可消费私有 lease，ClockPublication支持此真实 lease | 映像只作代码验证，未运行其中 GetTime、Progress、DllMain 或导入初始化；官方metadata/真实图未在该专项执行 |
| occurrence/provider | 独立 native owned calls生成私有 Pause/Replay/Resume、实际 PlayScheduled double 实参及 Returned、pitch/source序列；actual NativeRuntime accessor独立七根与图读回；stock pitch=1后恢复/readback选中速率；正式 Backend/Service消费此具体provider | NativeRuntime由受控 metadata/runtime fixture 驱动；只覆盖 owned lane，裸 Runtime/Unity外写、真实 stock/inline hooks、生产 return authority尚缺；默认模块未装配它 |
| Play非空联合 | 当前core-r3编译 actual Update24→Execute19→Single31→Play25→Add20×2，20 source行/10事务、83 TLS引用；相同Tween两次独立occurrence首次退出仍Pending，第二次才可借ordinary completion interval | source/graph metadata与admission/death受控；Play19/20、Execute21站点未执行；不签生产、退休、Unregister或根释放 |

`NativePlaybackActualSource` 的调度 DSP 来自游戏 `DspTimeService.get_DspTime`。一般 clock snapshot 的原始 `AudioSettings.dspTime` 保持独立来源；post-origin只做交叉读回，不能倒推已执行 schedule。

## 已修复的实际反例

1. **consumer恢复字节后旧hook复活**：映像r4中，局部consumer正文检查先抛异常，使broker未观察到改变；恢复字节后profile再次通过。现在先验证private lease，使broker立即sticky fault，再做局部完整body校验。最终r6 consumer独立进程验证恢复字节仍拒绝。
2. **首次snapshot接受已污染代码**：r5Runtime的真实D-only反例把Dobby `.text` padding RVA `0x3870` 的CC改CB，盘SHA未变、该字节未执行，原逻辑仍发lease并Verify通过。现在首次封印前对全部executable virtual bytes按pinned raw+zero tail+唯一bounded AMD64 DIR64 relocation逐字节核对；不mask IAT或任意外hook。preCapture Dobby/helper/source尾/consumer脏字节均拒绝，合法ASLR通过。
3. **合法邻接内存改变导致假撤回**：旧MemoryFact比较VirtualQuery整个region的BaseAddress/RegionSize；要求的88B属性未变，但邻接split令region `12288→8192`时被拒。现在只保护要求区间，逐当前子区核AllocationBase/AllocationProtect/State/Protect/Type，区域边界仅用于包含和推进。邻接split/merge、实际heap/StringBuilder grow/free正向通过，覆盖第二页保护变化仍拒。
4. **worker Dispose丢七根**：旧Source/ActualSource先置disposed，错线程root释放抛出后仍清空root list；主线程重试提前返回。现在两层在任何状态/list变化前核owner线程；实际worker拒绝后free-attempt和七根均不变，主线程可继续验证并恰好释放一次。

此外，早期net6 Stream HashData、合法ASLR ImageBase规范化、实际Dobby六字节搬移及原FF25跳尾的Windows展开问题都分别保留诊断目录；最终使用精确MOV/PUSH语义与独立sealed R11 tail。实际Dobby原跳板持续封印；调用回原走有Windows展开证明的等价跳板。原游戏完整调用/异常仍需后续真实候选验收。

## 当前树验证

| 检查 | 实际结果 | 权威原件（D:/riz-build-cache-next/ 下） |
|---|---|---|
| core-r3 | Audio1001、coordinator28、receiver10；actual exit0 | `foundation-broker-source-core-r3-20261031/test-result.json` |
| full-r2 | 27套2465/2465；所有build/test exit0；Gameplay175、Runtime317、Hooks210、Audio1001、Single65、相机33 | `foundation-broker-source-full-r2-20261031/test-result.json` |
| release-r2 | 六DLL；actual exit0、0 error、1条既有generated nullable warning | `foundation-broker-source-release-r2-20261031/managed-build-manifest.json` |
| solution-r1 | actual exit0、0 error/0 warning | `foundation-broker-source-solution-r1-20261031/test-result.json` |
| sdk-r1 | 两个独立生成插件经正式loader/host，14/14、actual exit0 | `foundation-broker-source-sdk-r1-20261031/test-result.json` |
| mapped-r6 | 6进程34检查；unhooked7、normal7、head/tail/consumer/original各5；原游戏original/DllMain均0调用 | `gameplay-clock-code-mapped-r6-20261031/test-result.json` |
| broker-r3/r7 | 29进程427检查、14 actual Dobby Commit、14前置拒绝、1 memory-ranges；304条freeze、22实际PDB源、5 managed/2 native PE/PDB对 | `gettime-broker-checks-r3-20261031/`、`gettime-broker-freeze-r2-20261031/` |
| owned-r4 | 175/175=原146+新29；native/managed/tests均exit0；8owned与688snapshot输入前后相同 | `gameplay-occurrence-provider-r4-20261031/` |
| Play current-r1 | 仅1个duplicates模式，native/managed/process exit0；802文件、704proof/live输入、94native、4overlay全核；17实际PDB文档匹配 | `single-play-current-source-r1-20261031/` |

各计数范围不同，不合并为一个总测试数。当前27套中的Gameplay175已经包含owned检查，不再另加175。Performance11为已有功能回归，本批没有开展性能基准/压力研究。六份变更PowerShell脚本已实际AST parse通过；完整Build.ps1的历史native/安装矩阵没有重跑。

整合校验器实际exit0，回核 **4836个唯一文件，其中736条当前工作树输入**。它校验core→full/release/mapped、full→solution/SDK、core→Play的manifest SHA关联，所有编译输入/主要输出，broker freeze全部304条及22份当前PDB源，owned七根相关8份当前源，Play802文件及两个source archive。字段和结果见 [test-result.json](test-result.json)；精确路径、大小、SHA及不同范围见 [evidence-index.json](evidence-index.json)。

旧core-r1/r2、full-r1、release-r1、映像r1–r5、broker旧r5/r6与owned旧r1–r3均保留。它们只说明当时源码/实际失败，不验最终当前树。旧Play r4和旧runner/报告未修改；current-r1只为当前Runtime修复重验同一模式，不重复计算新功能模式，也未重跑旧instant/七模式/退休专项。

## Evidence → Finding → Path

E-CORE/FULL/RELEASE/SOLUTION/SDK/MAPPED/BROKER/FREEZE/OWNED/PLAY 对应 [integration-inputs.json](integration-inputs.json) 的同名manifest；全部hash见index的artifacts。source_type=file/log，observed_at采用各manifest的实际时间，整合回核时间如本报告开头。各分包报告保留更细raw CALL/record/exit/PDB/COFF证据。

| Finding | 状态 / category / severity / confidence | evidence_ids、位置及影响 |
|---|---|---|
| F01 单目标GetTime与完整profile可组合 | validated-controlled / other / n/a_re / high | E-MAPPED/BROKER/FREEZE；CodeProfile、NativeProfile、ExactHookBroker.Playback、ClockPublication。故意detour后不删除原consumer/fullbody证明；候选发布仍需当前私有lease |
| F02 封印前与封印后实际改变不再冒签或复活 | validated-controlled / other / n/a_re / high | E-MAPPED/BROKER/FREEZE；ExecutableSeal.File/MemoryFact、CodeProfile验证顺序。合法loader/邻区变化可通过，目标属性/字节变化sticky失效 |
| F03 私有owned来源能驱动正式玩法事务 | validated-controlled / other / n/a_re / high | E-OWNED/FULL；ActualSource、OwnedCallTransport、OccurrenceRateProvider、Backend/Service。真实owned double参数/Returned、写者序列、选择速率恢复读回和错线程清理已成立 |
| F04 当前源码的actual Play非空联合正向成立 | validated-controlled / other / n/a_re / high | E-PLAY/CORE/FULL；正式Single/Play readers、native 24/19/31/25/20。两个相同指针的occurrence独立，首次退出不完成另一项 |
| F05 生产完整来源和跨局组合未实现 | validated-boundary / other / n/a_re / high | E-BROKER/OWNED/MAPPED及当前源；单目标image seal、controlled-only工厂、Publication ctor/Withdraw/retained registry。不能据本批打开真实Speed/Seek或全B项 |

P01（callflow）：实际Dobby owner→私有lease→完整五body/profile→lease-backed ClockPublication→native CAS/readback→prepare rate/restore；E-MAPPED/BROKER/FREEZE，关联F01/F02。仅owned native originals有执行证据，映像模式原游戏函数不调用。

P02（callflow）：native owned occurrence Before→actual NativeRuntime Stop/time/DSP→stock pitch=1→实际PlayScheduled double/Returned→独立选中pitch写票/readback→Clock.ObserveScheduled→正式Backend/Service事务与审计；E-OWNED/FULL，关联F03。缺任何正常退出或当前fence即撤回，不能从origin补事实。

P03（callflow）：actual Update24/Execute19→Single31 TLS/两个正式reader→Play25→Add20两次append/store/barrier→私有transfer[2,3,4]→两次bounded Update/source/ref→第一次Pending、第二次ordinary interval；E-PLAY/CORE，关联F04。rootFree/unregister均0。

## 下一实施与验收

按 [N2-STOCK-SOURCE-NEXT](../../../.docs/foundation-expansion/N2-STOCK-SOURCE-NEXT.md) 从共同broker安装组起步。单目标GetTime封印不能靠调整顺序与其他stock/foundation hooks共存；必须承认实际私有成员凭据并对每个patch/original继续验证，不能删全映像校验或由caller提供mask。

然后补真实MUG与已证Progress.Play内联Pause、实际PlayScheduled Double1、SetPitch同址alias/最下共同writer及clip/source替换、七根/局代次、GetTime publication与最终不可变Clock的构造/arm顺序。当前Bridge.Withdraw保留exact旧publication，Clock新建revision=1，物理broker保留地址；跨Retry/run/source必须有私有session/revision条件移交与实际drain，不能清票或重置revision冒充新局。真实首次准入仍另列。

Play未执行站点、生产admission/metadata/death、未来来源关闭与完整LOCAL仍未完成。任意Seek继续单列音符/判定/Hold/协程/输入/结果完整重建；相机/N1/退休/N4沿用成果。真实性能和游戏实测按用户要求继续后置，禁止以更大fixture计数代替对应真实验收。

## 复核

从仓库根重核本批原件（不重新执行旧测试）：

```powershell
& C:/Python314/python.exe docs/validation/gameplay-broker-source-integration-20261030/seal-integration.py --plan docs/validation/gameplay-broker-source-integration-20261030/integration-inputs.json
```

这会刷新整合回核时间，保留各原始测试结果。重跑执行需给每个runner全新不存在的D目录，并按core→full/release/mapped/Play、full→SDK/solution依赖顺序；命令参数见对应scripts和分包报告。新实验不覆盖本批输入计划/原件，也不能把历史成功manifest与新源码拼接。整合源码已冻结，本报告更新不修改编译输入。
