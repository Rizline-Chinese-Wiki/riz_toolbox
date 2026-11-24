# Unity backing 离线增量阶段报告

本包保留固定 Unity Object 字节依据、旧 GetTime 反例，以及当前 native-r3 的 19 项组件、runtime16、metadata21、5 模式168项 fault/foreign 检查、2 模式2089项 ABI 检查原件。证据约 6.6 MB，包括逐项核验的 932 输入原始源码 ZIP。正式八 SDK 尚未取得可接受的 aggregate/actual exit0 终态，worker 旧尝试为 runner timeout 未完成，foundation/符号仍在 SDK 门后等待。整体状态为 **pending**，没有合入主目录产品代码，也没有开放 production Speed/Seek。

## 授权与范围

本包依据[case scope](../../../work/riz-toolbox-unity-backing-20261103/scope.md)中的会话授权，仅保留已有 owned-fixture 离线证据并审查 case。没有安装/启动游戏、使用 UI/账号、执行游戏方法/DllMain或重跑测试矩阵。所有旧失败原件保持原字节；`passed=false` 的历史失败和未终态默认值均按各自实际含义记录。阶段工作项和变化见[workitems](../../../work/riz-toolbox-unity-backing-20261103/workitems.md)、[追加 timeline](../../../work/riz-toolbox-unity-backing-20261103/timeline.md)，机器状态见[current-package-status.json](current-package-status.json)。报告采用通用逆向结构（flavor=null）。

## 静态依据

固定 AMD64 image SHA-256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，dump 为 `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb`。保留完整相关方法的 raw bytes、RF/xdata、逐指令汇编及 Object/AudioSource/AudioClip 元数据片段；只重新核对原文件哈希与已固定字节，没有复制完整 image/dump或调用原 probe。导入/导出上下文同样保留：10 个导入 DLL、228 个导入符号、386 个导出。

`GetInstanceID@0x153F1C0` 完整 RF 为 267 bytes，读取 wrapper `+0x10` 的 `m_CachedPtr`、已存在的 staticFields/offset，再读取 backing+offset 的 Int32。`op_Implicit@0x15409B0` 也读取 wrapper `+0x10`；`.cctor` 把 offset 写为 -1，观察方不能为消除 -1 调用初始化。官方 `il2cpp_class_get_static_field_data@0x3590` 的完整 8 bytes `48 8B 81 B8 00 00 00 C3` 只读取 class+0xB8，不调用初始化。简化 dump 本身不能证明全部 static/byref/declaring-class 属性，运行期精确元数据验证仍是必需合同。

## 已保留的实际结果

| 证据组 | 已观察的窄范围结果 | 未扩大声明 |
|---|---|---|
| 旧反例 | 相同 wrapper/class/关系下 backing pointer/ID改变，旧 helper 仍用旧 policy；原失败 runner也保留 | 不是当前修复失败，也不擦除旧失败 |
| native-r3 19 | 8 active、8 actual owned Start pending first/transfer、3 reader/Poll；正常边界退 raw、Unknown0并能新 Start恢复 | 是 native 组件；不是正式 SDK 或生产运行 |
| runtime16 / metadata21 | frozen R2 的两个进程实际 exit0；metadata实际读取自有 Unity布局，21 checks、13 cached reads/14 static reads、roots0 | 不把旧 pitch/length 位置冒充 cachedPtr，也不宣称 GC历史 |
| fault/foreign5/168 | fresh 子进程各 exit0；source/clip坏ptr由 native SEH拒绝，foreign/错误MI不污染current，真实worker拒绝owner reader | Python CFUNCTYPE 是良性原/reader stub，不是 native SEH替代 |
| ABI2/2089 | normal1078、machine1011，actual wait/exit0；当前 r3 helper与八角色owned fixture | 独立旧八角色ABI组件，不替代20成员正式SDK验收 |
| formal SDK r1 | 五场景旧失败和stdout/stderr/来源保留；fake继承错误后修正已单列 | 不删除或把历史结果改为通过 |
| formal SDK r2 | 932输入冻结、修正layout、八模式driver14232/observer4044的进度快照 | 没有整体验收终态，因此仍 in_progress |
| worker sidecar | build/4产品PE-PDB一致性资料及旧run-r2 timeout/exit1原件保留 | runner在Speed前终止，不归为已完成产品断言失败；后续consumer待实际 |
| foundation/符号 | 已准备脚本、one-shot15780等待SDK实际0门的原件 | 本文档任务没有启动它，也没有签通过 |

原生 backing watch 自己在 SEH 下读取 wrapper/native 内存，精确绑定 native owner、epoch、PlayerWatch、phase/ordinal、source/clip exact class。合法变化advance独立 source fence并关闭旧 writer/Clock；实际新 Start和source-open才能授予新 watch，随后 actual Pause/readback形成 successor Clock。pending backing原因是独立认证badge，不能借用resource-write badge。readClock普通失效先用绑定源 Poll归类，稳定backing的真实reader错误仍为Unknown。

Unknown情形中 `BackingInfo phase2/current1` 可能仍是旧数据投影，因为该读API不把owner Faults/Flags纳入current计算；写权限实际由 Source Unknown和Single inactive双重阻断。receipt仍可读、retire被拒绝只表示保留责任记录，不是GC root保留或清理证明。

## Evidence 索引

各 Evidence 的 `source_ref` 指回原件，`artifact_path/content_hash` 固定 case 内的组清单；每个清单再列所有原字节成员的原路径、保存路径、size、SHA-256。完整索引见[retention-index](../../../work/riz-toolbox-unity-backing-20261103/evidence/retention-index.json)。

| Evidence | 原件主题 | 组清单 SHA-256 |
|---|---|---|
| [E-001](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-001.md) | 固定 853d/092c Object 静态布局与 RF 原字节 | `deb4bec494d427990f9d0848f8627820b58df083b33e893e544d3831712293a7` |
| [E-002](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-002.md) | 旧 PlayerWatch 与实际 GetEntry 反例原件 | `f5024616af3119504e7db423e3776093913609f95cb25d522ec0b6082d0c51d6` |
| [E-003](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-003.md) | native-r3 两 helper 与 13 源码构建绑定 | `06ad0dc4fcb9ca9904f1edf083e45e437d5d419dc73333e03bed351817fb1300` |
| [E-004](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-004.md) | native-r3 十九项组件终态 | `be955476ed3409f006ac2c2d507fba81263f84119dc265ef3da7fab60de02689` |
| [E-005](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-005.md) | 正式 R2 frozen source 的 runtime16 终态 | `75fcf8479255279b8a98dcf675839bae3ba77b477e4688ab446627c52b8b6ed5` |
| [E-006](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-006.md) | 实际 owned Unity layout metadata21 终态 | `b5a09aed9b00d82c585916583e1c554e4dc25a405353221e5752f2a42fe4421f` |
| [E-007](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-007.md) | 新增 native fault/foreign 五模式168检查 | `747ef3397530966c5a7e76dcfcf2c9c771dc3d0ffda87afb99bdfb19b6fcb47c` |
| [E-008](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-008.md) | 当前 r3 helper 的八角色 ABI 两模式2089检查 | `dc4efba1b61b0edbace03508bb32154cb29406bb7d93225344655c5bfe713838` |
| [E-009](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-009.md) | formal SDK r1 历史失败原件 | `93e0cab5df076b6b3c13e5ef296a60d741314dbb3b2f8d3c210d7eddb96a9ca1` |
| [E-010](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-010.md) | 932 输入的正式 R2 源码冻结与 ZIP | `d61d7d691d272e999579191a30efd61baeb209621c4fb1c01c70d13fa8a6f4fd` |
| [E-011](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-011.md) | formal 八 SDK 尚未整体验收的进度快照 | `89fc65871d58ff5a83370b0684b63d594b50d746c374f59d239f8666377c0910` |
| [E-012](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-012.md) | worker sidecar runner timeout 未完成原件 | `9871829262f0f6e27057e3599be1d2f38a593b16c689cf0b5bf6cc95b6af9e90` |
| [E-013](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-013.md) | foundation 准备与 one-shot 等待门 | `d6cd906eb32617a9f4f794d874f16d833d3e46ac0d6c42b114773a7c2073a8ab` |
| [E-014](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-014.md) | 真实 journal counter/Applied 测试合同 | `788b99de2a3b9348491029c32143f45b6c2372d8afc435031e96b6def49cdd40` |
| [E-015](../../../work/riz-toolbox-unity-backing-20261103/evidence/E-015.md) | owned Unity wrapper/backing 与真实 CALL 布局证明 | `ef068a11c0c0c47630ed05913187a2d8e7e0716028bdff3fc2711af2a7c44f58` |

## Findings

### F-001 静态 cachedPtr/offset/ID 读取链有固定文件依据
- title: 固定 Object 身份读取链
- severity: n/a_re
- category: reverse_algo
- status: validated
- confidence: high
- evidence_ids: [E-001, E-006]
- location: fixed image GetInstanceID@0x153F1C0/op_Implicit@0x15409B0；NativeRuntime.UnityObjectBacking
- impact: 能按固定布局独立读取身份，并在运行期使用精确metadata拒绝未初始化或错误声明；不调用游戏方法初始化。
- repro_steps: 运行case的notes/verify_retained_evidence.py E-001核验原件；查看RF raw bytes/asm与metadata21终态。
- remediation: 继续保留 runtime static/byref/declaring-class/stability 验证。
- optional_attack: n/a，离线功能验证无攻击路径。

### F-002 新 native backing watch 覆盖已测正常边界和 pending 恢复
- title: 已测 native 生命周期组件闭合
- severity: n/a_re
- category: design
- status: validated
- confidence: high
- evidence_ids: [E-002, E-003, E-004, E-015]
- location: PlaybackUnityBackingWatch.inc；SingleReturnContext.cpp；pending first/transfer receipts
- impact: 旧反例得以定位；r3未pump即撤旧policy，合法变化保留Known并由actual owned Start重捕获。
- repro_steps: 核验19个原始test-result与r3 build/source13哈希，不重跑旧probe。
- remediation: 正式SDK/worker/foundation仍按独立workitem等待终态。
- optional_attack: n/a

### F-003 已测 fault/foreign 与 ABI 保持组件合同
- title: native fault 隔离与责任保留
- severity: n/a_re
- category: design
- status: validated
- confidence: high
- evidence_ids: [E-003, E-007, E-008]
- location: Source.CheckUnityBackingLocked/ReadUnityBackingSample；SingleReturn.PolicyReceiver/Dispatch
- impact: native坏指针被SEH归为stickyUnknown，foreign/错误MI不污染current；worker在owner-reader前拒绝，原ABI与once/raw保持。
- repro_steps: 阅读五个fresh wait/exit/result和两ABI actual终态；用只读证据核验脚本验证全部保留字节。
- remediation: 仅按实际Source/Single权限判断，禁止把retained receipt projection当GC或写授权。
- optional_attack: n/a

### F-004 整体 SDK 与 foundation 尚不能签通过
- title: 持续中的整体验收边界
- severity: info
- category: other
- status: candidate
- confidence: high
- evidence_ids: [E-009, E-010, E-011, E-012, E-013, E-014]
- location: functional-r2；managed-unity-worker-r1；validation-continuation-r1
- impact: case元数据通过与窄组件成功不能替代八SDK aggregate/actual exit0、worker consumer及foundation/符号终态。
- repro_steps: 只读当前保留snapshot及workitems，不把progress或passed默认false当最终验收。
- remediation: lead收齐原进程和新consumer实际终态后追加Evidence/timeline，另建final review；本轮不合主。
- optional_attack: n/a

## Paths

### P-001 原件到已测 native 身份保护调用链
- title: 只读静态依据到owned组件验证
- path_type: callflow
- start: 固定853d/092c的m_CachedPtr与offset读取链
- goal: 证明当前r3在已测路径中先撤旧权限，再允许已认证新Start恢复
- steps: |
    1. 静态raw/RF/metadata定位来源（E-001，F-001）。
    2. 旧GetEntry反例揭示wrapper比较不足（E-002，F-002）。
    3. r3 source13/build绑定原生watch与共同consumer路径（E-003，F-002）。
    4. 19项正常/挂起恢复、runtime16、metadata21和owned布局证据核验（E-004 E-005 E-006 E-015，F-002）。
    5. fresh fault/foreign与独立ABI验证保留once/raw/责任边界（E-007 E-008，F-003）。
- residual_risks: 未观察到的pointer+ID双ABA、lower writer、完整GC/生命周期均未排除。

### P-002 pending验收的证据追加路径
- title: 先等待实际终态再结案
- path_type: callflow
- start: 932输入正式R2冻结与未终态进度
- goal: 保留当前工作而不伪造整个handoff完成
- steps: |
    1. 保留r1失败与932输入源码冻结，journal断言合同独立固定（E-009 E-010 E-014，F-004）。
    2. 八SDK actual aggregate/observer尚未接受，维持WI-011 in_progress（E-011，F-004）。
    3. worker旧timeout按未完成保留，等待新consumer终态（E-012，F-004）。
    4. one-shot15780只在SDK实际0门后继续，文档任务不启动foundation（E-013，F-004）。
    5. 只读strict hash review签证据结构/fixity；产品合入决定仍pending（E-010 E-011 E-012 E-013）。
- residual_risks: SDK/foundation/worker未终态不能靠时间、进程存活、metadata review PASS替代。

## 仍未证明

本增量不排除两次观察间 source/clip 的 pointer 与 instance ID 同时ABA，不覆盖任意未知engine/lower writer、Unity native销毁和GC的全部历史，也不宣布完整generation封口。production首次执行、真实Speed/Seek、任意effect、B01–B19仍单列未完成。没有使用复制的pair、epoch或bool发行native权限，没有旧兼容分支；性能优化后置，功能以实际正确行为优先。

## 复核与下一步

只读保留件完整性命令：

```powershell
& 'C:\Python314\python.exe' -B 'C:\Users\71957\Desktop\riz_toolbox\work\riz-toolbox-unity-backing-20261103\notes\verify_retained_evidence.py'
```

只读case审查命令：

```powershell
& 'C:\Python314\python.exe' -B 'C:\Users\71957\.agents\skills\reverse-skill-router\case-review\scripts\review_case.py' 'C:\Users\71957\Desktop\riz_toolbox\work\riz-toolbox-unity-backing-20261103' --strict --verify-hashes --format json
```

当前preparation review结果保存在case的 `report/preparation.review-attempt*`，后续warning/error修正需新attempt，不覆盖旧审查。review只判断范围/证据链/哈希，不把WI-011/012/013提升为done。lead收到后续原始终态后再追加新Evidence和timeline，建立final review；主目录代码仍未合入。

preparation 只读严格哈希审查：PASS（0 errors / 0 warnings）；[最新审查](../../../work/riz-toolbox-unity-backing-20261103/report/preparation.review-attempt2.json)。全部276个原字节副本另有独立哈希核验。该结果不完成仍在进行的SDK/worker/foundation工作项。

## 2026-11-03 13:46 新增实际证据

新启用 Unity backing watch 的七 ABI 夹具已22进程／12678检查实际通过，含7稳定、7 source pointer、7 clip pointer和1真实OS SEH。七typed channel加一个构造器前置通道实际Dobby8/8；原body参数/返回rawAL、nonvolatile GPR/XMM/RSP、完整物理退出和源身份撤回有实读。SEH先核Abnormal＋PhysicalExited receipt，再按broker策略MarkFault；不冒称helper自己凭异常自动完成该策略。E-016签此native范围，不签SDK或生产。

正式R2的stable已actual0／27／Applied1／roots0；source及clip恢复各actual1／31，失败点是测试将公共`Clock.Read().Success`当成原生调速权限。现有`GameplayService.Read`合同保留独立game/DSP/host读数，真正权限由NativeDescriptor、provider、具体操作支持与lease代次控制。旧失败保留（E-017），仅新副本两个测试改查旧native descriptor为null、SetSpeed不支持、旧lease TargetExpired、native policy关闭，并保留原时钟、writer/prepared、fence和实际新Start后的调速正向断言。fresh build0、四产品DLL/PDB八文件逐字节同R2（E-018）；新的完整恢复仍待终态，不能靠这个编译通过验收。

主目录仍915／README原hash。932源码新整树`foundation-r2`由稳定SDK实际正向、watch七ABI和同产品字节明确准入，只签独立managed／distribution／Release／symbol范围，不能替代仍待完成的SDK恢复。旧one-shot15780仍只等原R2全成功门，不改它或伪造旧aggregate。worker原consumer300s超时失败保留，continuation-r3自然wait。完整generation／双ABA／未知lower／production／Seek/effects／B01–B19仍未完成。

### F-005 Unity watch 启用下七 ABI 与真实异常物理退出已验证

- severity: n/a_re
- status: validated
- evidence_ids: E-016
- confidence: high
- location: native-unity-watch-seven-abi-r1/attempt-r1
- notes: 22 actual processes/12678 checks; component native scope only, seven ABIs plus required constructor channel.

### F-006 公共时钟读数不等于原生调速权限，恢复验收仍在继续

- severity: n/a_re
- status: candidate
- evidence_ids: E-017, E-018
- confidence: high
- location: GameplayService.Read; UnityBackingChecks/UnityBackingPendingChecks isolated correction
- notes: 保留stable实际成功与source/clip原失败；产品输入及八PE/PDB一致，纠正测试后仅编译已过，完整恢复尚未验收。

## 2026-11-03 13:51 新源码构建与回归已实际完成

E-019：`foundation-r2`以修正测试合同后的932输入新freeze编译、运行并独立核原件。28套2636/2636、solution0errors/56既有warnings、独立双作者SDK14、六组件Release实际0；新symbol verifier实际0，11对PE/PDB对应1032个真实SHA256源文档。observer持原driver44320句柄记录actual0，symbol continuation12468只等待这次build，不重复旧矩阵。

源码ZIP、每个实际child命令/日志/终态、两作者与SDK包、Release及功能PE/PDB和符号文档已按原字节保留。此范围签新managed／distribution／build／symbol结果；`functionalAcceptanceComplete`与`nativeSdkRecoveryAcceptance`均false。两个测试合同修补与原R2相比不改488产品/51native输入，旧失败和旧scope继续保留。SDK重捕获、worker完整自然终态和主目录合入仍未完成，不以本2636替代它们。

## 2026-11-03 14:00 SDK worker 实际自然终态

E-020：consumer2340与driver16068自然wait actual0，observer46312持原生双句柄记双actual0。正式双插件SDK先实际SetSpeed1.5，真实构造器worker线程在原写入前撤有效Clock／native rate；原GetTime raw17.25恰一次，旧policy Applied增量0／Clock reader增量0，worker NativeRuntime cached/static读取均0。owner之后旧provider／descriptor／lease失效，原生物理入口已退出、Source和Single stickyUnknown，最后实际7根和native backing／owner责任保留至进程退出，FakeNative未Dispose。41检查／20实际hook，journal只有最初一条Applied，拒绝不追加。

两个公共Clock.Read仍成功、原生descriptor却已撤回，是通用时间读数与原生调速权限分离的实际证据；没有把保留数据投影当权限。原runner300s超时的run-r2仍false，新scope只补自然未完consumer，不重编译成功build；四产品8个DLL/PDB逐字节同R2，5对PDB531文档及488产品/51native源对应当前C候选。完整source/clip恢复、新Start后的SDK调速仍待终态，主915未合入，production/Seek/全B保持未完成。

### F-007 正式 SDK worker 失效撤权同时保留原生根责任

- severity: n/a_re
- status: validated
- evidence_ids: E-020
- confidence: high
- location: managed-unity-worker-r1/continuation-r3
- notes: 41实际检查/自然双退出0；Source与Single stickyUnknown，未恢复旧原生权限，7根保留至进程退出；不声明正常恢复或production完成。

## 2026-11-03 14:02 managed 重捕获链路实际完成

E-021：为尽早区分初始化移交与耗时的完整SDK事务，在独立933测试副本跑真实managed路由。仅新增一个测试和dispatch，488产品/51native不变，四产品DLL/PDB八文件同字节。23实际检查通过，driver35352及observer22028记录actual0：source底层pointer更换后未pump即撤旧GetTime policy、原调用恰一次；实际AL1 yield／AL0 final Start完成新七根捕获并释放旧根；source-open保持Dormant，后续actual Pause建立同run更高代次唯一Clock，20个物理hook保持；owner正常释放后roots0。

该范围不构造正式SDK也不执行SDK调速，因此仍必须收修正后的完整六个SDK恢复场景及原host-only first-ID的实际终态。旧SDK失败和新worker自然成功都保留，不能拿route23代替SDK恢复。主目录915未改，修正候选C932的47路径精确差分（17新增30修改）与改前ZIP已在`main-delta-preparation-r2`实际核好，仅准备未应用，保留Lifecycle Dispose修补。未执行旧ApplyR1，不使用它应用新候选。

### F-008 actual Start与Pause已能重捕获新Unity backing并正常释放旧根

- severity: n/a_re
- status: validated
- evidence_ids: E-021
- confidence: high
- location: managed-recapture-route-r1/functional-r1
- notes: 23实际检查/20同物理hooks/新唯一Clock/最终roots0；明确SDK rate write未由本scope证明。

## 2026-11-03 14:35 pending 测试阻塞条件已实际定位

E-022：原正式pending两项actual1/check27，停在测试未持住phase6，并未到修改backing步骤。独立无SDK事务的原／新条件对照实际27检查通过：旧Unity watch被替换后count仍1，714次callback里虽有241次新source/clip读取，Progress＋count>1从未触发，原初始化因此正常completed=true／phase7。新watch对象identity＋source/clip读取边界实际持住phase6／一个GetTime borrower，释放后同一候选正常source-open／roots0。产品无须放宽。

最终候选现在另隔离为`pending-trigger-correction-r3/source`932，只在已验C候选上再改一个测试文件PendingChecks的触发和新watch身份断言；488产品/51native不变，新build0及四产品8DLL/PDB字节同。两个修正pending SDK的driver48200／observer11924／child46824、47884在跑。旧待启动五项launcher41980尚处waiting且没有子测试时取消，外部保留actualwait -1和原因，未杀任何真实测试。新launcher20104持已有clip driver8916句柄，只在它actual0后复用r3成功build跑source-pointer/source-ID/clip-ID三项；不重复已过clip或已启动pending。

8916/21348的clip恢复已完成真实新Start、唯一Clock、原模块provider恢复和新代次独占lease，正在第二次有值Speed，仍须其自然成功终态。旧R2整体false不可改绿；hostOnly first-ID原项已通过其初始化恢复段仍跑正式SDK，不重做它。2636/SDK14/六Release/11PDB1032只签其原C源码，后加测试文件变动通过新standalone编译/产品同字节与具体新SDK范围衔接，不能声称又跑了一遍整树或已完整合主。

### F-009 pending的watch身份与采样边界必须独立于watch数量

- severity: n/a_re
- status: validated
- evidence_ids: E-022
- confidence: high
- location: UnityBackingPendingChecks test-only trigger; managed-pending-boundary-probe-r1/functional-r2
- notes: 旧count1→1使测试未阻塞；新identity/source-clip真实持住phase6，27实际检查。完整SDK正向继续，产品未变。

## 2026-11-03T09:05:13.742669+00:00 | lead | SDK recovery closure

E-023：八个正式 SDK 路径已自然退出0，共449项检查。每项真实20物理hook、journal和roots0；原R2／Clock合同修正／pending触发修正三个不可变测试范围逐项归属，488产品及八PE/PDB字节相同。旧R2整体false保留；本结果不代表又重跑整树。主目录仍915，下一执行47路径准确合入及fresh检查；完整generation／lower／production／Seek／effects／B01–B19仍未完成。

## 2026-11-03 17:30 当前 Unity backing 增量已合入并离线验证

E-023／E-024：八个完整 SDK 场景449检查均自然退出0，准确47路径（17新增30修改）已应用到主目录，主932／488产品／51native与最终候选逐项相同。fresh solution0errors／56既有warnings、runtime16＋metadata21、六组件Release均actual0；13实际PE/PDB对应1052份SHA256源码。新构建与候选DLL的差异仅限逐项列出的MVID／PDB标识／构建时间戳，所有其他PE字节完全相同；没有声称原PDB字节相同。已通过的solution/短检查/Release未因验收脚本错误重跑。

原主验证的范围清单／README外置假设、严格字节一致断言，以及C盘generated profile是D盘junction别名的symbol plan错误都保留原false；本次组合只使用各真实成功子阶段。READMEmain原hash保持，未安装或启动游戏。整体generation／双ABA／未知lower／production／Seek／effects／B01–B19仍未完成。下一单SetPitch icall的工作仅在独立X候选，不能借本批449验其后改源码。
