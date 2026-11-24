# Progress Clip 资源来源连续性审查

本次建议只补 `ProgressControl.set_Clip` 这个已知资源引用写入口的私有连续性凭据。它改的是下一次 Start 使用的 `IResourceReference<AudioClip>`，不会立即改当前 `AudioSource.clip`。当前 Start 和真实 set_clip 已有换源前屏障，本次没有复现“纯资源 setter 导致当前播放错误”。若只给 setter 加全局 Source fence、撤当前 Clock，却没有新引用可正常继续捕获的正向链，收益只是保守拒绝，不能算本合同完成。实现须等当前 T/formal-r2 收尾封存。

## 范围与状态

[scope.md](scope.md) 记录授权和离线边界；`flavor=null`。本报告只完成静态审查，`functionalValidationComplete=false`、`productionAdmission=false`。未改产品、主仓或冻结输入，未跑行为fixture或新长测，未执行游戏原函数/DllMain/游戏进程/UI/账号。原SDK进程不在本审查操作范围。时间线见 [timeline.md](timeline.md)。

输入映像为 `D:/riz-build-cache-next/playback-direct-audio-managed-r1-20261102/mapped-r4/image/GameAssembly.dll`，SHA `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。旧dump原件为 `C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs`，SHA `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb`；与既有writer静态包一致。工具为本机pefile/capstone；版本及PE完整导入/导出表在 [pe-survey.json](pe-survey.json)。

## 核实的机器码与消费路径

| 目标 | 固定证据 | 语义与边界 |
|---|---|---|
| Progress backing field | dump98148：`IResourceReference<UnityEngine.AudioClip> <Clip>k__BackingField //0x30` | 资源引用，不是当前AudioClip；实例字段，不是标量时间。 |
| setter4016F0 | `48895130 4883c130 E96328E6FF`，13B，SHA `bb8036f89480f121c1bd96fd6bcd7c739d4d45f2bb58f562a0fb49b974aab390` | `MOV [RCX+30],RDX; ADD RCX,30; JMP263F60`。RCX=self，RDX原引用，R8 incoming MI；本体不读取MI。 |
| file RF | 完整113821行异常表对这13字节无覆盖 | 仅静态file no-RF；新Dobby/thunk和动态RF/展开尚须owned fixture实际证明。 |
| 屏障 | 263F60→2F5750→320B20 | 导出`il2cpp_gc_wbarrier_set_field@284100`搬RDX→RCX、R8→RDX，经2F4910写`[RCX]`后同样到320B20；终端按字段地址标记位并`lock cmpxchg`。可证同一GC写屏障，不应再额外补一次屏障。 |
| Start state1 | 404AD8读`[RSI+30]`；404AEE调用2EF0；结果RAX→RDX；404B01调用1511A20 | 新引用影响实际下一Start选出的AudioClip。2EF0为通用调用辅助；`get_Resource`名称按资源metadata和数据流推断，未执行getter或加载资源。 |
| 等待谓词 | 400F91也读取+30并经2EF0后检查结果 | 等待过程也消费当时的资源引用，不应缓存setter前指针代替真正重读。 |
| 共享入口 | dump35个声明共享4016F0 | 其余包括Exception.HelpLink、UI、网络字段等34个声明；必须按实际根对象筛选，不可按RVA使所有调用污染播放。 |
| 现物理组 | 18source＋GetTime，共19独立RVA，无4016F0 | 401700是offset38的另一入口。下一只新增一个物理成员；不能重复hook同址声明。 |

完整指令和原始字节见 [fixed-bodies.asm.txt](fixed-bodies.asm.txt)、[fixed-bodies.json](fixed-bodies.json)。metadata摘录及35声明见 [metadata-excerpts.json](metadata-excerpts.json)。E8/E9扫描没有发现4016F0直接引用；getter的两个直接引用属于共享的其他类，不是Progress消费证据。该负扫描不排除间接调用/内联store，也不签stock实际调用过setter。`Start@404AF8`清R8D只能证明下游AudioSource.set_clip用空MI，不能证明此资源setter的MI历史。

## 为什么这是下批窄功能，而非当前播放修复

当前七根独立保留Controller/Information/Progress/Pause/Player/AudioSource/AudioClip，`NativePlaybackActualSource.VerifyGraph`检查Progress+38、Progress+28、Player+10及AudioSource.get_clip，没有把Progress+30资源引用作为当前播放依赖。因此单独setter发生后当前clip仍可继续正确播放。现有Start入口以及真实AudioSource.set_clip会在真正换clip前推进原source边界。静态证据没有表明现Clock因此漏撤，不能称已复现bug。

本批的正向收益应是：为这个**已知入口**发行一次真实资源写入观察、按Progress生命周期维护资源修订；旧的future-capture/initializer/source-open候选能准确过期，新的候选能在实际Start读取新reference后成功。纯setter时，现有独立current-audio Clock仍可读且可执行本身合法的rate命令。到实际Start/set_clip才按已有规则结束旧current票，并经过实际capture/readback/handoff恢复唯一fresh Clock。资源setter本身不发行当前source identity或generation。

owner-thread正常完成且native group保持Known时，必须撤当前票的条件是它确实绑定了已结束的资源修订，或者实际Start/set_clip/其他现有source边界已经发生；当前票只绑定既有七根current AudioClip时，仅撤依赖旧resource的future-capture票。matching worker仍走既有native-only故障路径，真实全组Unknown必须由owner pump撤当前native Clock及所有依赖该组的权限，不能以资源字段未直接换clip为由忽略Unknown；更细worker故障域隔离未证、也不在本批。本批不建议把正常资源revision混进owner.Source/Writer/Scheduling。新role必须在Router的通用“外部leaf使producer失败”路径之前分流，并保留真实PhysicalExit责任。

## 唯一建议合同及实施落点

完整可机读合同是 [next-function-contract.json](next-function-contract.json)，包含精确metadata/ABI/body、17个实施文件、7组验收及未证边界。

可直接复用 `ProgressRelevantLocked` 的active/pending真实Progress根＋class筛选，且与in-flight/source-open同锁；不需另做caller传入的地址名单。watch本身不包含+30资源revision：新增一个正交私有资源连续性状态，保留SourceV4/PlayerWatchV2/ProgressWatchV1原wire，必要时另发版本明确的private credential。相同Progress在active/pending移交时延续修订；不同Progress和同址其他类透明原样通过。长期持有资源引用的捕获票必须独立RootHandle，或证明只持诊断地址且从不解引用失去保留的指针。

安装阶段锁定真实声明MI、字段偏移、实例/void/构造泛型引用类型和RVA。现`RequireReference`硬编码type-code18，不可直接拿它核泛型实例type-code21；不能把AudioClip类当作IResourceReference<AudioClip>参数类。运行时判相关性不要求incoming MI精确或非空：该本体无论R8是什么都会写current self+30。null、实际MI、无关nonnull MI均按真正self分类，原参数位不变。

本体与已合MusicPlayer叶子只差字段偏移及E9位移，现`NativePlaybackProgressPlayerLeafProof`明确硬编码38。因此新offset30必须另有私有精确profile/证明，不能放宽为任意offset/tail/hash。新Dobby搬移须独立证明首8B、target+8继续、空展开、RX/只读元数据和持续篡改拒绝。新role追加19；完整新profile为19source、20物理成员（含GetTime）、21metadata声明（含pitch别名）。历史fixture/pin及旧证据保留。

## 最小但有意义的owned验收

1. 实际13B setter＋own barrier、真实Dobby group安装：Before看到旧+30值，资源修订已推进；随后原store和屏障各恰好一次，void/MI/引用原位保持，SEH与PhysicalExit真实完成。
2. current self的null/精确/无关nonnull MI，null引用，同值、A→B→A、写后SEH均使旧resource凭据不可恢复。只有owner-thread正常完成且组Known的纯setter保持独立current Clock/rate/prepared权限；matching worker真实Unknown必须撤当前Clock。
3. 同class foreign Progress共用current AudioSource、无关class共享入口，owner/worker及嵌套原调用都透明通过；foreign自身及无关child零journal/token/fence/initializer污染。若foreign original故意再调用watched current ProgressClip或current AudioSource leaf，该related child必须parent0独立计数、产生自己的journal并推进正确resource或现source/writer/scheduling fence，不能要求该用例全局零变化。需要n3 ForwardIgnored清TLS并在SEH finally恢复。
4. four-root host、seven-root active/pending、退休/ABA/晚retire都验证；setter打断依赖旧resource的capture时不得让旧candidate source-open。实际exit/drain后允许新捕获，不以永久NotReady作为通过。
5. 正向消费者必须真正从Progress+30重读newRef，调用fixture newRef.get_Resource，再走真实set_clip child；证明实际Start此时才撤旧current票，并完成新的source-open/readback、唯一Clock及合法命令。测试不可只记录计数或主动构造新binding绕过消费者。

以上是**待执行验收**。本次只运行18项静态文件断言，结果 [static-verification-result.json](static-verification-result.json)；不把它签成native/Runtime/SDK功能通过。

## Evidence → Finding → Path

| Evidence | source_ref / 内容 | 可重现与hash |
|---|---|---|
| E-imports/E-exports | 原映像PE survey：228导入、386导出；含gc/文件/加载器/同步依赖，完整名称在JSON | `python inspect_static.py`；本地artifact hash见artifact-index.json |
| E-body | 原映像setter、屏障、Start/predicate，完整body/异常表排除 | 同命令；fixed-bodies.json/asm；原映像SHA见上 |
| E-meta | dump98128–98175与91685–91728、35同址声明 | 同命令；metadata-excerpts.json；dump SHA见上 |
| E-source | 29份选读源/既有报告逐项hash、关键行号 | `python build_review.py`；source-manifest.json，不称整树freeze |
| E-xref | 可执行区E8/E9候选及所在RF的指令边界核对 | `python inspect_static.py`；direct-reference-scan.json |
| E-static-check | 本次18项静态断言 | `python build_review.py`；static-verification-result.json |

| Finding | status / severity | evidence_ids | location / confidence | 结论 |
|---|---|---|---|---|
| F01 | validated / n/a_re | E-body,E-meta | 4016F0 / high | 已知共享资源引用叶写入口；GC屏障路径可证。 |
| F02 | validated / n/a_re | E-body,E-meta,E-source | 404AD8→404B01 / high | 下一Start消费资源引用；当前AudioClip不立即变化，已有实际换源屏障。 |
| F03 | validated / n/a_re | E-source,E-xref | Hooks目标表/ActualSource / high | 此入口不在当前19物理成员；负xref不代表stock从不调用。 |
| F04 | candidate / n/a_re | E-body,E-meta,E-source | 新ProgressClip资源凭据 / high | 只有正交资源连续性＋新引用成功重捕获才是本批功能收益；未实施/未行为验收。 |

P01 (`path_type=callflow`)：资源setter4016F0修改Progress+30（E-body/F01）→GC屏障263F60/2F5750/320B20（E-body/F01）→下一实际Start404AD8加载该资源引用、404AEE取得资源（E-body/E-meta/F02，getter命名为静态推断）→404B01进入已有AudioSource.set_clip边界（E-body/E-source/F02）→后续捕获应依据新resource连续性票（E-source/F04，待实施/执行）。P02：纯setter→当前AudioSource/七根未变→独立current Clock保持；future旧resource票过期→真实Start/newRef正向重捕获（F04合同，非本轮actual结果）。

## 原件及完整性

当前选读Hook声明SHA `4f6a99e7c83ed3efe86227bcd77058a90307ff2099c3e289b597f6ceabad0881`；group目标表SHA `175c72a6f256af9ede52855191f851e5a56578fc11f46909612f556a7c62a1b3`；Progress watch native SHA `9b644b1b217ca980440b72e66882312f16f9158e699e1e138cebb5af3cab5f64`；ActualSource SHA `5029e3dc3b5f85e2f2a8c429278e71308e157277bacd154fc97c3c92d46491c0`。所有选读源路径、hash、行号见source-manifest.json；本目录逐件hash见artifact-index.json。报告仅引用当前T n3状态背景，不重新验T，也不把仍运行的SDK矩阵视作故障。

剩余边界：未证实际stock setter caller/MI历史、任意inline/reflection/直接字段writer、未知engine lower、完整resource/source generation；未启production provider，未做任意Seek/effect或B01–B19整体完成声明。新功能不得借旧n3的SDK/ABI通过代签更改后的helper。
