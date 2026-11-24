# Tween 工作退出与最终根释放

日期：2026-10-28。范围：固定 Steam Windows x64 构建、实际 CutOut 的 TweenInfo 来源，以及对应 TweenManager 工作记录。本文是下一批实现设计；文中的源点补丁、事务重放、死亡凭证和最终释放尚未作为已完成能力发布。当前 admission 批次已冻结，root 报告核心离线回归 885/885 通过；本次分析没有构建、安装或操作游戏。

**实现结论：增加 Update 源点事务，把每个入队 occurrence 作为独立工作记录；同时修复死亡尾项不清数组、第二条 ready 激活路径绕过 TweenInfo 存活检查的问题。记录真正退出、源被实际封口、具体对象死亡和派生效果归属完成后，才能消费私有 interval 调用 native Unregister，再分别释放每个持有者的根。** 增加 `idle`、`isOver` 或场景卸载判断，均不能代替这些动作。

## 当前已经具备什么

当前生产路径在激活前借入实际 persistent target，分别保留 TweenInfo、目标组件、曲线、三个子事件和全部已知效果参数。实际静态 AddTween delegate、具体 AddTweenToWaitingList MethodInfo、manager、playing 数组、scheduled List、ready Stack 及完整 backing capacity 都被独立保根和双读校验。

Play、AddTweenToWaitingList、ExecuteTween、Update、Awake 的五个真实原生入口已有同步 Before/After。每次 Add 的实际追加有独立 admission ID 和最初 slot；同指针的重复项不会合并，但跨 Update 的位置目前仍会变成 Ambiguous。已安装的永久 exact-class dead policy 跳过 Play/Add/Execute 中死亡 TweenInfo；实际 Seal 只关闭新的 Play/Add，已接纳的活任务继续执行。Skipped=8 单独记录，不发行入队或正常 After。

当前 `CanRetireWork()` 保持 false，Runtime 没有可调用的 Unregister/释放接口。新批次的目标是提供真正的退出路径，再替换这个未实现状态；不能单独将它改成 `TrySeal()`。

### 本批接线状态

当前确定的 native V1 是 **22 sites / 43 bindings / 192-byte record / 96-byte info**。原生 relay 在真实指令位置记 journal，CLR 通过非消费 `ReadRecord` poll 读取；本批没有同步 Attach callback export。record 的 `operation` 为 Update=0、Play=1、Execute=2，两个 sequence 字段依该实际 operation 解释，独立 Play 不伪挂 Update。额外五个 event site 是下文两个 Update onStart、Play onStart/onComplete、Execute onComplete。

因此下文的同步含义是“journal 在真正 source Pre/Post 当时形成”，并不声称 CLR callback 已同步安装。CLR reader 只能绑定调用发生前就已独立 Root 的对象；未知 journal 裸指针不能晚 Root 来补采用。实际 publisher/source Ready receipt 尚未接好时，生产 Bind 必须拒绝，只有明确的 controlled reader/replay 回归可运行。`configured`、`metadataBound` 或调用方提供的 UpdateFrame 都不能授予生产权限。

## 证据、发现与实施路径

| ID | 实际证据 | 内容与限制 |
|---|---|---|
| E1 | `D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll` | SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。本文所有 RVA 只适用于此文件。 |
| E2 | `D:/riz-build-cache-next/tween-listener-source-r1-20261027/evidence.json` | SHA-256 `561b3225017a6ef99d23edb0881960b67e291e7a24638b94c2edd6226a279a2a`。含五函数有界反汇编、资产原始字节、完整 CutOut listener 解析和两份 TweenInfo 解析。线性反汇编夹有 jump table，不能把表数据当指令。 |
| E3 | `D:/riz-build-cache-next/tween-operation-input-r2-20261028/profile-facts.json` | SHA-256 `fc23cf5e87b82368b9a14ff7d2418cf162bb1b166fad235005ffa60686c86a4e`。实际 RF 范围与完整 body/unwind prefix；Play 3576、Add 268、Execute 5908、Update 5172、Awake 322 字节。 |
| E4 | 当前 `NativeLocalTweenWorkOwner.cs`、`NativeLocalTweenBackend*.cs`、`Hooks/NativeTweenAdmission.cs`、`native/.../TweenAdmission.cpp` | 实际保根、admission、五入口、Skipped 和未实现的完整退休边界。源码说明当前行为，不是下一批已实现证明。 |

| Finding | Evidence → Finding → Path |
|---|---|
| F1：重复项无法由总数量唯一归属 | E2 的两次 TryPop、尾项搬移、scheduled 换位删除 → 相同 Tween 指针有多个合法位置历史 → P1 按源 slot 重放，保留 transient occurrence，最终核整个实际向量。 |
| F2：死亡尾项可能永远留在容量尾部 | E2 `39962F → 399636 → 39967F` 绕过 `39966A` 清 slot → workingCount 缩小后不再访问该引用 → P2 在实际死亡分支走原清 slot/GC barrier，并补受核尾部清理。 |
| F3：只挂 Execute 不覆盖 Update 内联效果 | E2 ready 增长路径 `3996B8 → 399721 → 399D41`，以及两个内联 start-value 分支 → Update 可在 Execute 之前读写 Tween 字段、调用 onStart → P3 观察 ready 激活与实际子事件 CALL；死亡项在任何内联效果之前丢弃。 |
| F4：原调用返回、源 Seal、根释放是不同事务 | E2 onComplete 在 `3984C2`，`isOver=true` 在 `3984C7`；E4 admission seal 仅阻新发行 → callback 可重入新增工作，旧任务也未必退出 → P4 私有完成 interval 绑定实际队列退出、派生责任、死亡和原生排空，最后才 Unregister/free。 |

静态输入复查可在仓库用以下只读 Python 调用，不需要运行游戏；它打印现有输入文件的 SHA，并重新检查固定 RF/body。不会改写已有证据。

```powershell
@'
import hashlib, importlib.util
from pathlib import Path
script = Path('scripts/inspect-tween-operation-profile.py')
spec = importlib.util.spec_from_file_location('tween_profile', script)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
facts = module.inspect(Path(r'D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll'))
assert facts['passed']
for row in facts['operations']:
    print(row['method'], hex(row['beginRva']), row['bodyBytes'], row['bodySha256'])
for name in [r'D:/riz-build-cache-next/tween-listener-source-r1-20261027/evidence.json',
             r'D:/riz-build-cache-next/tween-operation-input-r2-20261028/profile-facts.json']:
    print(name, hashlib.sha256(Path(name).read_bytes()).hexdigest())
'@ | C:/Python314/python.exe -
```

## P1：用真实 source slot 驱动事务重放

### 记录模型

增加专用 `NativeTweenSource` native transport 和 Runtime typed bridge。五个入口继续负责整个函数的 physical generation；新源点只在其真实 native Update parent 中同步发行。它不能从公开 `NativeTweenWorkGraph` 或 caller bool 建立生产观察权。

每个源点携带实际 parent operation epoch/sequence、thread、depth、site ID、Pre/Post、child sequence、source/transport PC、manager、容器/数组、实际 item、源/目标索引及真实调用结果。配置必须绑定完整 pinned body、实际 native publication 和每个 patch 的真实 RF/unwind。回调保留的对象先独立 Root，再检查 class/metadata；不把没有根的 journal 指针留到后续 pump 后解引用。

标签不是 Tween 指针：

```text
OccurrenceId = owner-private monotonic ID
Admission = (实际 Add sequence, source lease slot/epoch, manager generation, OccurrenceId)
Location = (manager generation, container object, backing-array generation, slot index)
UpdateTransaction = (实际 Update epoch/sequence/thread, start graph, labelled vectors, transient pops, ordered source steps)
```

Before 图为每个实际位置建立标签；同指针位于两个 slot 时产生两个 occurrence。native Add 的追加产生一个新标签。初次看见的既有项另记 `ObservedExisting`，不伪造 Add 的 issuer。foreign 项也要有位置标签，以便压缩/换位可重放；这不赋予它本局效果所有权。

TryPop 成功后，标签先移入对应 `TransientPop`。onStart 在写 playing 之前执行，这段 transient 仍是工作责任。写入 playing 后转到新 slot；拒绝激活或 exact-dead 丢弃时给该 transient 一个明确的退出原因。暂时复制产生两个引用位置时，两位置指向同一 occurrence，直到原位置被清除；不能把 copy 当第二次 admission。

### Update 的全部关键源点

下表列出实际指令或 CALL 地址；CALL 后地址用于配对。最终 relay 的 overwrite span 必须由 native inspector 重新解码并核合法控制流，不能直接把表中每一行当作可独立写入的五字节区域。`R14=manager`、`ESI=当前/空洞索引`、`R15D=旧 playing 尾索引` 是该函数体内的实际寄存器约定。

| Site | 实际 RVA / 完成位置 | 实际来源与标签动作 |
|---|---|---|
| Update 起始边界 | `398D60`；旧 working 读取 `398E12` | 现有 Void0 physical parent；初图保完整容量，旧上界为 workingCount−1。 |
| ready pop A | CALL `398F18` → `398F1D` | `RCX=ready Stack`，`RDX=&[RBP+67]`；真实 bool 返回决定是否弹出原 top slot；新标签进入 transient A。 |
| ready pop B | CALL `3996B8` → `3996BD` | `RCX=ready Stack`，`RDX=&[RBP+77]`；进入 transient B。 |
| start-value A 拒绝 | store `398EF3` → `398F00` | `RBX=transient A`；设 isOver 后重新取栈。显式退出该 occurrence；不能等到函数 After 才猜它消失。 |
| start-value B 拒绝 | store `3998BD` → `399DBD` | `RBX=transient B`；同样记录拒绝激活，不宣称任何已执行子效果结束。 |
| onStart A | CALL `399565` → `39956A` | `[RBP+67]=TweenInfo`，`RCX=[TweenInfo+88]`；子事件归 transient A。 |
| playing 安装 A | store `3995BB`，barrier CALL `3995CB` → `3995D0` | `RBX=array`，`ESI=目标 slot`，`RDI=transient A`；标签从 transient 转入 playing。 |
| 正向 Execute | CALL `3995DA` → `3995DF` | `RCX=R14`，`RDX=实际 TweenInfo`，源 slot `ESI`；既可能旧 playing，也可能刚安装的 A，按当前标签表区分。 |
| 尾项死亡判断 | CALL `39962F`，branch `399636` | `RBX=playing[R15D]`；现代码 Unity-null 时跳到 `39967F`，没有清引用。P2 修复此路径。 |
| 尾项显式清除 | store `39966A`，barrier CALL `39967A` → `39967F` | `RCX=array`，索引 `R15D`；旧引用退出该 slot。 |
| 尾项 Execute | CALL `399785` → `39978A` | `RDX=RBX`，源 slot **R15D**；不能套用正向的 ESI。 |
| 尾项搬入空洞 | store `3997E1`，barrier CALL `3997F1` → `3997F6` | `RBX=array`，源 `R15D`，目标 `ESI`，`RDI=实际尾项`；先 copy 标签。 |
| 搬移后清旧尾 | store `399815`，barrier CALL `399824` → `399829` | `RDX=array`，**RCX=递减前旧尾索引**；ESI 已增、R15D 已减，不能误用修改后的寄存器。 |
| onStart B | CALL `399D41` → `399D46` | `[RBP+77]=TweenInfo`，`RCX=[TweenInfo+88]`；子事件归 transient B。 |
| playing 安装 B | store `399D97`，barrier CALL `399DA7` → `399DAC` | `RBX=array`，目标 `ESI`，`RDI=transient B`。 |
| 增长段 Execute | CALL `399DB6` → `399DBB` | `RDX=[RBP+77]`，源 slot `ESI`。 |
| working 提交 | store `399E04` → `399E08` | `[R14+3C]=ESI`；只有这里才更新 field。在此之前 nested Execute 图中的 workingCount 可能落后于实际活 slot。 |
| scheduled → ready A | CALL `399E97` → `399E9C` | `RCX=ready`，`RDX=RBX`，scheduled 源索引 `ESI`；Push 成功才增加 ready 位置，scheduled 旧引用等待后续删除。 |
| scheduled → ready B | CALL `399F7D` → `399F82` | `RCX=RDI=ready`，`RDX=RAX=实际 item`，scheduled 源索引 `EBX`。 |
| scheduled RemoveAt B | CALL `399F98` → `399F9D` | `RCX=scheduled`，`EDX=EBX`；按实际删除索引重放移位，不按指针查找。 |
| scheduled 末项填洞 | CALL `399FD8` → `399FDD` | `RCX=RDI=scheduled`，`EDX=ESI=目标`，`R8=RAX=源 EBX 项`；copy 标签，保留原位置直到 RemoveAt。 |
| scheduled RemoveAt last | CALL `399FFA` → `399FFF` | `RCX=scheduled`，`EDX=实际 size−1`。 |
| scheduled RemoveAt current | CALL `39A02C` → `39A031` | `RCX=scheduled`，`EDX=ESI`。 |
| Update 正常结束 | 实际 Void0 After；epilog `39A031..39A062` | 提交前以完整数组、logical size/version、container identity、manager generation 和 transient 已清空核重放结果。异常出口只有未完成责任，没有正常 commit。 |

两条 scheduled Push、set_Item、三条 RemoveAt 是实际调用形状的反汇编结论。此设计只在固定 CALL source 上包围它们，不按 dump 的 generic 占位 RVA 安装全局 generic hook。

### 如何提交与如何处理重入

每个 source Pre/Post 都必须与同一 native physical parent、同一 site、child sequence、真实 continuation 对应。raw array store 与原 IL2CPP GC write barrier 必须作为连续 replay 段保留；不得在 store 已做而 barrier 未做时进入 CLR。CALL source 的 post 读取真实返回结果，不重放原调用。

Update commit 前重放每一步，比较最终实际完整向量。`[TweenA#1,TweenA#2]` 必须能区分 pop 后执行的是哪个 admission；重复指针不会再自动把两者都标 Ambiguous。相反，未配对 native source、未知外部写入、错误索引或最终向量不符应保留该实际事务为 Unknown，而不是选择任意映射。

onStart/onComplete 重入的 Play/Add 是子 physical generation，它只为自己追加新标签。外层随后置 isOver 或清旧 slot，不得退休新 admission。Awake 替换容器时先关闭旧 manager generation 的 field alias，保留旧 Update frame 和旧 backing roots；新 generation 单独建图。旧 resize array 的历史引用不是仍在调度的队列，不应要求它每个旧 cell 都被擦成零；须证明它已失去实际 manager field alias、源点/current frame 不再借用，再退休其观察根。

## P2：真正清理死亡项

### 死亡 playing 尾项

当前 `399636` 的 Unity-null 分支直接去 `39967F`，使非 null 的 dead wrapper 躲过原 `39966A` 清 slot。native source relay 应在该真实分支上区分：

- managed null：保持原分支。
- exact TweenInfo 且永久 policy 核 `m_CachedPtr==0`：转入原 `39964E` 的 bounds/store/barrier 清 slot 路径，再继续 `39967F`。
- 未知 class、不可读或其它分支：保持原路径并记录 Unknown；不猜死亡。

这里不能只无条件改一个短跳偏移，因为那会同时改变 null/未知 class 的处理。publisher 应核原 branch 和所重放控制流，复用原 store 与 GC barrier；新 journal 记录实际 `DeadDiscard`。

另外，在 Update 正常结束的受核 cleanup 段检查当前 playing 数组 `[workingCount, Length)`，只清实际 owned、exact、已死亡的残留来源；每次写均核原 slot 仍等于预期对象并经过原生 GC barrier。这个动作处理已有的容量尾部残留。它不是任意清理活对象，也不清其它 Mod 的活工作。并发 worker/未知 manager writer 使该 sweep 无权提交，保留原责任。

### 死亡 ready 项

ready B 成功 pop 后只检查 managed 非 null，`tweenType=12` 可直接进入 `onStart`，没有统一的 TweenInfo Unity-alive 判断。两个成功 pop 后均增加 exact-class dead check，发生在内联 start-value/onStart **之前**；确认死亡时退出该 transient，然后回各自下一次 ready 取栈路径（A `398F00`、B `3996A0`）。保留实际 TryPop 输出与原生 machine state。

**Sealed 的活项不能因此丢弃。** 当前 Seal 关闭新发行，原来已接纳的活工作继续经过内联初始化、Execute 和完成；若以后要增加主动取消，它需要另一份明确的取消结果和语义。

## P3：Update 内联及派生效果归属

仅挂 `SetTweenStartValue(3986B0)` 不能补齐覆盖，Update 已内联相关逻辑。source transaction 必须给 transient activation 单独建作用域，保留该实际 TweenInfo 的参数前后值和实际 target，而不是凭最终 isOver 推断中间未执行效果。

| 实际调用 | 来源归属 | 结束依据 |
|---|---|---|
| Play instant onStart `3957E8` / onComplete `395D50` | 当前实际 Play physical generation；没有 queue admission 时另发 InstantWork | 子事件的实际 Invoke/listener generations；不能因 Play 返回就结束。 |
| Update onStart `399565` / `399D41` | 对应 ready pop 的 transient occurrence | actual event root 与精确 CALL source、真实 Invoke/dispose/physical exit；派生任务仍归自己的 owner。 |
| Execute onComplete `3984C2` | 当前执行源 slot 的 occurrence 与 Execute generation | 同上；`3984C7` 的 isOver store 排在回调之后，重入子 admission 独立存在。 |
| Execute onTween 的实际 float-event CALL | `tweenType=12` 的具体 occurrence | 必须补 closed-float event 的真实调用/参数/source 归属；普通 generic 未实现不能当空事件。E2 已见 `397F85`、`397FBA`，其余 curve 分支 CALL 由 inspector 完整枚举。 |
| Transform/UI/native setter | 实际 target root、具体 setter MethodInfo/entry、当前 occurrence | 只记直接写入与原生返回不能声称无派生效果。需要具体目标/引擎 effect owner 或真实责任移交及该 owner 的完成凭证。 |

`NativeLocalUnityEventLedger` 需要以实际 borrower root 增加动态 child-source 注册能力：保留实际 onStart/onComplete/onTween event 的独立根，绑定当前 private Tween source token；不能让 caller snapshot/任意 method name 变成 source issuer。未知 listener、子 coroutine、其它 Tween、场景任务要实际建立子 owner 或保留未完成职责。

队列 `WorkExit` 和所有派生效果 `EffectsEnded` 分开。具体任务可正常退出或按 exact-dead policy 丢弃；父 invocation 的 `AllInvokedEffectsAccounted` 必须等子责任真正完成。若把已发行子任务移交其它 owner，必须先由那个真实 owner 接受独立 roots/任务记录并返回 private transfer receipt；移交不能当整个 run 的 consumers ended。这样既能实际退休已完成的 Tween 队列记录，也不会把 UI setter 或任意异步回调藏进“返回即完成”。

实际资产有两个 Scale Tween（pathID 1116/1304，目标 RectTransform 877/895），duration=1、delay=.1、useOnStart/useOnComplete=false、三子事件持久项均为零；这些只是静态输入。运行时必须核具体 clone/root/方法和实际子事件图。不能按 pathID、方法名或 prefab 的空列表跳过 runtime 的效果责任。

## P4：具体死亡凭证、Unregister 与释放顺序

### 同根的实际死亡区间

新增 `NativeLocalTweenDeathTracker`。预激活分别保留实际 TweenInfo、实际目标 Component、其 GameObject/Transform；核它们当时的存活及 clone 关系。使用已经在生产毁灭观察中核过的 `UnityEngine.Object.op_Implicit(15409B0)`、官方 `Object.m_CachedPtr` 字段和 `Time.get_frameCount(155EFD0)`，不调用任意 OnDestroy 方法。

每个对象记录 `LiveObserved → DeathObserved → DeathConfirmed`：同一独立 root/actual class、真实单调 frame、晚于该对象活观察和相关终结/返回请求的后续 frame，实际 alive=false 且 cachedPtr=0；再次完整核对前后 source/五入口/journal 活动。dead→alive、identity/class/parent source 变化均为 Unknown。clone 的五对象死亡不能替代具体 TweenInfo/target 死亡；目标重挂其它 hierarchy 后仍活就继续持有它，而不是按旧层级推断它死了。

这证明具体 Unity 对象的 native 死亡；不宣称 managed wrapper 已被 GC 回收。永久 exact-class dead policy 保留，native Unregister 才能忘记旧裸地址；弱引用变 null 不参与此证明。

### 状态与实际正向动作

| owner 状态 | 实际动作 | 到下一阶段的私有事实 |
|---|---|---|
| Open | 真实 Play/Add 与 source journal 发行/转移标签 | classified Return 请求已到同一 coordinator。 |
| AdmissionClosing | 对每个真实 lease Seal，busy claim 同根重试 | 每个实际 lease Read 为 Sealed，issuer in-flight=0。 |
| Draining | 让活 Execute/Update 继续；死亡 ready/playing 走 P2 清理 | 每个 occurrence 已有源点退出；没有 transient/copy/实际容量 alias；所有事务已提交。 |
| AwaitingConcreteDeath | 运行同根死亡 tracker，不销毁未知活目标 | 所有要求的具体 death receipts、派生效果 receipts 已完成。 |
| WorkEnded | 原生 journal/五入口前后排空，冻结 private source-work receipt | 含 exact owner/backend/launch、lease slot/epoch、manager generations、最终图/事务截止序列和 death/effect receipts。 |
| AdmissionUnregistering | 同步消费该私有 interval 调 native Unregister | 实际 Retired 回执同 object/class/slot/epoch，cached0/issuer0/entries0；未知结果保留所有根。 |
| ObservationWithdrawing | 逻辑 detach 五入口及 source callbacks，等待已捕获上下文真实退出 | 新 callback 不再取得此 owner；已捕获 frame 完成，完整 transport/journal drained。永久 dead policy/code 不卸载。 |
| RootFreeAttempted | 独立一次释放每个 lease root，再释放本 backend/死亡/effect 观察 roots | 每一步各自成功；不再解引用已释放 slot，不重试未知 free。 |
| Retired | 发行 private `TweenOwnerRetirementReceipt`，移除该 run owner | native slot 已忘记地址、本 owner 根表为空、所有独立 transfer/effect/source 责任有对应 owner。 |

Runtime 新接口只接受已登记的同一个 production `INativeTweenAdmissionOwner` 从其私有发行器同步借出的 `CompletionInterval`。该 interval 绑定具体 lease 引用及 slot/epoch，不接受 `Snapshot + bool`。在调用实际 Unregister 前再次核 producer/metadata/strong root、sealed/零活动及 interval；成功后核 Retired 完整元组，然后才标每个 lease 的 root free-attempted。free 失败不清根记录、不发成功票、不再次调用 free。

释放 B 的 admission root 并不等于全部释放：C backend 的 `_roots`、历史 backing arrays、子事件/target/curve/death tracker、UnityEvent backend 的借出来源都各有独立根。新的退休事务必须列出每个真正的 holder，分别完成。已失去调度 alias 的历史数组可在其借用 frame 排空后释放观察根；当前队列 capacity 内的引用须先由实际操作退出。释放阶段的后置校验只读取冻结凭证与原生活动，不反过来读已经 free 的根。

native Unregister 与源点 journal 必须共享同一个真实切面：检查所有五入口以及 source callbacks 的活动和 epoch，不能仅在 CLR 分开采样两次 idle。Unregister 后出现的未来 dead-source 调用仍由永久 class policy 拦住；若已有 context 或 source-relay 未排空，保留根并等待其实际完成。source callbacks 的逻辑撤出不能漏掉已捕获上下文。

`NativeLocalObserverRetirementTransaction` 当前依赖整局 active Close 后的 context，而且根槽上限为 2048。Tween backend 有 4096 个自己的槽及额外最多 128 个 admission roots，不能直接借这个旧 context 或压低实际根数量。应新增专用 `NativeLocalTweenRetirementTransaction`，先在 host 的普通/frozen 两条 pump 上完成 Tween 自己的工作/根退休，再让 active consumers verifier 消费它的 private receipt，避免“等 Close 才能完成其 Close 前置条件”的环。

## 实施文件与接线顺序

| 顺序 | 文件/模块 | 必须实际产出的能力 |
|---|---|---|
| 1 | native `TweenSourceProfile.*`、`TweenSourceContext.*`、`TweenSourceEntry.asm`；固定源点 inspector | 完整 site/table/bytes/unwind、同步真实 parent/child frame、无中断 store→GC barrier replay、P2 两类功能修复；新 publication 唯一版本由root整合。 |
| 2 | Runtime `Hooks/NativeTweenSource*.cs`、必要 metadata/root API | 只从实际 source publication Ready 创建 bridge；精确 frame/site/layout 验证；typed identity 与同步 publication；没有 DTO 生产授权入口。 |
| 3 | Adapter 新 `NativeLocalTweenWorkOwner.Sources.cs` 或独立私有 source ledger | occurrence 标签、transient、copy/remove/transfer 重放、重入与 manager 世代；真实正常 Update After 才 commit，所有历史保留。 |
| 4 | `NativeLocalUnityEventLedger.TweenEffects.cs` / 新 child-effects partial | 实际 child roots/source-token/Invoke generations，Update 内联、instant Play、onComplete 重入及 float child 归属。 |
| 5 | 新 `NativeLocalTweenDeathTracker.cs`、backend partial | 独立具体 alive→dead 区间、真实 frame/liveness/field 双核。 |
| 6 | 新 `NativeLocalTweenWorkOwner.Completion.cs`、`NativeLocalTweenRetirementTransaction.cs`；Runtime admission checked-release partial | 真正的 WorkEnded、可消费一次的 private interval、实际 Unregister/Retired、每 holder 独立一次 free、最终 receipt。 |
| 7 | root 接现 backend/owner/coordinator | 激活前 source绑定；Update Before/After源点事务；正常与frozen host pump；以实际receipt替换 `CanRetireWork=false`，不修改成 diagnostic bool。 |

当前协作边界：本批 source ledger/effects 先落新文件和 focused tests，现有主文件仍冻结，由 root 接线；native 和 CLR 独立实现 stable header/typed bridge。最终释放接口要等这些机制都有真实实现后再开放。

### Adapter source replay 落地与接线边界（2026-10-28）

`NativeLocalTweenWorkOwner.Sources.cs` 保留每个原始 capacity slot 的独立 occurrence；相同 Tween 指针的两个既有 slot 不合并，实际 Add 正常 After 则附实际 admission ID 发行新标签。playing copy、scheduled Push/set_Item 可暂时让同一标签拥有多个位置，只有对应真实清除或按原生索引 RemoveAt 后才减少位置。ready pop 有 A/B 独立 transient 与实际 pop transaction，playing 安装必须引用该 transaction；正常 Update After 尚有 transient、未配对 Pre/Post 或未清 move 时拒绝 commit。

所有 snapshot container 源点先核完整容量、实际 backing array、logical size/version，再按真实索引应用预期变化；最后核独立采得的完整 manager After graph。playing raw array 的 wire count 是 capacity，graph 的 Count 是实际 `_workingTweenTrackSize`；只有正常 Update After 可以更新这项已实际采得的 logical field。这个局部 commit 不提供 effect/death/work-ended authority。未知 journal 指针仍由 reader 在任何解引用前拒绝，不新增晚 root。此处诊断 `QueueExited` 也不替代具体死亡、派生效果结束或最终根释放。

主 owner 接线入口依次是：完成真实 Before capture 后且 original 前 `BeginSourceOperation(context)`；真实 native After 已采得但 context 尚未 Dispose 时，Add 调 `ObserveSourceAdmission(context)`，然后各入口调 `CompleteSourceOperation(context)`；pump 只在保根/来源身份仍安全时调 `PollSourceJournal()`。只能传 `_operations` 里且当前 `_top` 的实际 context，没有可用任意 DTO 注册 Update 的入口。跳过 original 的空 source operation 记 `Skipped`，撤销未运行的 instant 诊断标签，保留旧队列工作；若已有 source 行却报告 Skipped 则为 Unknown。

`NativeLocalTweenWorkOwner.InlineEffects.cs` 与 `NativeLocalUnityEventLedger.TweenInlineSources.cs` 为实际当前 Invoke generation 发行 private association；准确匹配 source operation sequence/thread/physical parent/site 与 actual relay child caller。5B CALL 的 child return 是 **Before.RelayReturnPc + 5**，原 SourcePc 仅标记 pinned 原始源点，生产 reader 必须将两个 PC 分别与 immutable publication 对照。正常 source CALL 返回和 Invoke 的完成/Dispose 都不提供子效果结束；`EffectsEnded` 保持 false。动态 child-event adoption、float event、setter/engine effects、对象死亡、最终 native drain/unregister/root-free/receipt 仍未完成，`CanRetireWork` 必须保持拒绝。

shared manager 的已预保根 foreign Tween 在当前真实 Update 的具体 Execute source CALL 内发出 onComplete journal 时，可归档其原生 child sequence/parent/thread/depth 与具体 foreign slot 为独立标量历史；同 Update 的 foreign transient onStart 也只记 scalar source step。它们不伪造 CLR OperationContext、不发行本局 InlineEventSource，也不赋予本局 effects 归属。外来 child 必须在对应真实 parent CALL 返回前闭合，错 parent/manager/thread/slot 均为 Unknown。尚未覆盖通过未捕获外来 native operation 再重入本局 Play 的完整 physical mask 链；此类链不得按省略了外来层的 CLR parent 猜测归属。未知 foreign 指针仍不得 lateRoot。

实际 listener owner 在 source reader 绑定后用 `AttachTweenInlineWork(work)` 持有同 launch 的实际 work；主 `ShouldObserveInvoke` 扩展 `ShouldObserveTweenInlineInvoke(receiver)`，只匹配 journal 已发行且仍在当前 physical operation 内打开的 private source CALL。`CaptureNativeInvoke` 建立真实 context 后调用 `ObserveActualTweenInlineEntry(context, frame)`。未注册完整 child graph 的事件目前可用实际 mask 关联并发行独立 mask generation；这仅证明实际 Invoke/source 关系，不表示其全部 listeners 已追踪，更不提供 effects-ended。

新增 9 条 focused cases 在 `tests/RizToolbox.AudioContent.Tests/NativeTweenSourceReplayTests.cs` 的 `NativeTweenWorkObservationTests.SourceReplayCases`，主代理已在总测试表和 isolated-case 表各注册一次。它们经真实 typed reader/root-set 与 owner retained context 覆盖重复指针、transient、copy/clear、三类 indexed RemoveAt、onStart 内重入 admission、nested/伪造 context 拒绝、独立 Play、版本异常、未退出 transient、最终容量尾部差异、陌生指针不补 root、skipped 空操作与 foreign scalar 的正确/错误 parent。代理未运行 build/test；受控 transport 和图不授予生产 source publication 权限。

## 可验收的实际功能

1. 两个相同 Tween 指针从 scheduled/ready 进入不同 playing slot，连续执行、尾部压缩、scheduled 换位删除后仍有唯一 occurrence 历史；与真实最终完整向量相符，而不是把两个 admission 一起判 Ambiguous。
2. ready A/B 的不同 source slot 被实测；onStart 发生在安装前，回调重入 Add 可产生新 occurrence，旧 transient 和新 admission 不相互退休。
3. onComplete 重入、Over、Replay 后外层仍会写 isOver；记录继续按 source slot 退出。Sealed 的活任务按原路径执行，绝不因 source 已封口丢弃它。
4. exact-dead ready 不再运行内联 start-value/onStart；exact-dead playing tail 的实际 cell 被 GC-barrier 清空，workingCount 之外的旧 dead-owned尾部也被受核清除。null/unknown class 保原路径；错误分类保 Unknown。
5. manager Awake/queue resize 后保旧世代 frame/数组责任，实际失去 alias 后才退休它们；不会因旧 backing 数组残影永久卡住，也不会把实际旧 frame 隐藏掉。
6. 正常 Execute 返回但队列尚有别的重复项、派生任务未完或具体目标仍活时，只有该真实 work-step 被记录，最终 retirement 不通过。
7. 使用真实 source transport 的 native fixture 覆盖寄存器/flags/XMM、实际 original side effects、重入/异常、每个 source site 与GC-barrier顺序；CLR fixture 验 actual wire/privateparent、错PC/错thread/溢出拒绝；Adapter fixture 经同步 bridge 调真实replay，再与actualfinalgraph比对。
8. 一条完整正向路径真正得到：source封口 → 全部记录退出 → 具体死亡及派生责任完成 → native Retired 回执 → B/C/death等独立根一次释放 → private最终receipt。仅“仍返回false”的负例不算完成该验收。
9. unregister失败/未知、free失败、断裂sourcepair、nativeactivity变化时保留具体责任；不伪发Retired、不retry未知free。冻结后只使用安全的复制诊断，不再读已释放root。

Tween 的具体工作及观察根退休，不自动证明其它五个 SetActive/CanvasGroup listener、序列化/direct/listener 外部引用等整局职责闭合。它们继续由自己的具体 owner 完成，不能借本文件的 Tween receipt 放行整个 UnityEvent 或整个 run。
