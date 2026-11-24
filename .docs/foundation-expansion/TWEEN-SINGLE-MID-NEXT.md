# onTween<float> 的实际列表与 listener producer 下一批

日期：2026-10-29。本文记录有界静态定位、实施合同、第 7 节 P1/P2 owned machine 和第 9 节 P3 native V7 publication 冻结证据，`flavor = null`。独立 native producer 与正式 native publication 已实现并通过各自有界 fixture；正式 Single typed invocation 的 CLR/真实游戏验收与 P4 effects 仍为 Pending。正常 Return、Dispose 或入口/出口空快照仍不构成 effects 结束。

授权与范围来自本轮用户任务及 [SPEC.md](SPEC.md)：只读固定 GameAssembly 中 `UnityEvent<System.Single>.Invoke` 的四段 RF，共 647 字节，核对现有 Runtime/Adapter/native 接线，随后先做独立原生 producer 与 owned machine fixture。没有独立 security case，scope.md、IOC、ATT&CK 不适用。没有启动、加载或修改游戏，没有分析 callee 正文，没有修改已冻结退休 helper。跨 C#/FirstInstall/包合同由 root 统一，不能局部修改 26/57。

## 1. 精确输入与可重现证据

固定输入 `D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll`：35,146,752 bytes，SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，AMD64 PE，ImageBase `0x180000000`。下文全部为 RVA。

| RF 范围，右开 | 正文长度 / SHA256 | unwind RVA / 长度 / SHA256 |
|---|---|---|
| `0xbc0ff0..0xbc106a` | 122 / `2d0805d04ea926cb60735cc26043803bed7fbff14255e11a0006990e9db9464e` | `0x1d4e3a0` / 20 / `b2759315dd38c62bd092f7337759011560660899bd5217e7d8f464f0851cbca4` |
| `0xbc106a..0xbc1255` | 491 / `364b4d1276a86363d3a14d0bc4fe790b7a35902d16c9959ff8f8fbd47b2c01be` | `0x1d4e3b4` / 36 / `1cdcf906e179e1e484c4399dc8f5fa07337f1e77d694c6620b13a7f38d358a4e` |
| `0xbc1255..0xbc1271` | 28 / `779ff7ee53221b940938ace6e40c8b9889e3f7eda20ad79c7c8cb4e5be2a0e5b` | `0x1d4e3d8` / 36 / `2609c7e8ccbc78fc3067c83c04e2e22c0e48b17a8e03cd12d0416a1cc7ae915a` |
| `0xbc1271..0xbc1277` | 6 / `2668f032fcc3d7f2026f511f2a78a64e07cc928fcd0b4e220f843abdfc9cb207` | `0x1d4e3fc` / 16 / `c359bbd40956a17c8211d3d79be019a266491a192b46da7ffda68ba3846001d2` |

四段正文及完整 unwind 与 `FirstInstallProfile.h` 的 `Operation25*` 数组逐字节匹配；静态 `.pdata` 的四个 `(Begin,End,UnwindData)` 也匹配。三个 CHAININFO 的 parent tuple 均是 `(0xbc0ff0,0xbc106a,0x1d4e3a0)`。共反汇编 158 条指令，覆盖 647B，没有向外展开任何 callee。

| Evidence | source_ref / content_hash | repro_command 与观察 |
|---|---|---|
| E1 静态 PE 与 profile | `D:/riz-build-cache-next/tween-single-mid-plan-a-r1-20261029/single-static-evidence.json` / `c996776e022199cfc2a07d7bba40986052944afc33cac5711290946c963e9e7a` | `& C:/Python314/python.exe -B D:/riz-build-cache-next/tween-single-mid-plan-a-r1-20261029/inspect-single.py`；先断言整 PE SHA，再匹配四 RF、全部 code/unwind/pdata，输出调用点与 imports。 |
| E2 原始反汇编 | 同目录 `single-647-disassembly.txt` / `27065e8e754c6212d22993d8ba4ab27757ad83f73e22e0066d01f862d2eb52f7` | 同 E1 命令；每条包含实际 RVA、原字节、mnemonic、操作数。 |
| E3 源码接线 | 同目录 `source-evidence.json` / `e0a516caca4566bea62ec4b4d6a81534271497eb88a3fc986484386d084016a1` | `Get-Content -Raw <source-evidence.json>` 后对每个 `path` 跑 `Get-FileHash -Algorithm SHA256`；包含本次读过的 14 份 Runtime/Adapter/native 文件及长度/hash。 |
| E4 检查器 | 同目录 `inspect-single.py` / `46c58050242764ec39e11334fac0eec4eb92bd3b52ee600943d633ebf44cb93c` | 只用 `Path.read_bytes`、pefile、Capstone；不 Map/LoadLibrary，不执行目标。 |
| E-imports/exports | E1 的 imports 和 exportCount | 10 个 import DLL，共 228 符号；386 exports。记录文件/API、线程/进程、网络和 crypto 类符号，未跟踪行为：`CreateFileW/ReadFile/WriteFile`，`GetCurrentProcess/VirtualAlloc`，`WSASend/socket/connect`，`CryptAcquireContextW/CryptGenRandom`；本次未发现直接 `Reg*` 导入。 |

工具实际为 Capstone 5.0.6、pefile 2024.8.26、`C:/Python314/python.exe`。`ida-reverse/scripts/start.ps1` 实际返回 `ERR:Missing idalib-mcp`；没有安装工具或用未完成的 IDA 会话声称已反编译。这里是受限的 PE/指令级静态观察，不是游戏运行证据。

## 2. 一次性 mid：列表为空也真实到达

实际列表生产 CALL 在 `0xbc1059`，字节 `e8022a9a00`，目标 `0x1563a60`。现有 `NativeRuntime.ListenerMetadata.cs::ResolveObservedReferenceReturn0` 将该目标固定为 `UnityEventBase.PrepareInvoke`，返回类型是实际 `List<BaseInvokableCall>`。它不是 `InvokableCallList.PrepareInvoke` 的 `0x154e1d0`。

返回后是 `0xbc105e: mov r15,rax`、`test rax,rax` 与失败分支。不要覆盖这组含测试/分支的指令作为首选方案。更直接的 producer 点为：

```text
0xbc1082  4c89742438          mov [rsp+38h],r14
0xbc1087  660f1f840000000000  nop word ptr [rax+rax]
0xbc1090  413b4f18            cmp ecx,[r15+18h]
0xbc1094  0f8d90010000        jge 0xbc122a
```

`0xbc1087` 是完整 9B NOP，位于 CHAIN0 的附加 prolog 完成之后、第一次列表 count 比较之前。非空、空列表均走到一次；后续 listener 循环回跳到 `0xbc1090`，不会再次触发 mid。候选发布为 `E8 rel32 + 4 NOP`，span 9B，真实硬件 CALL return PC 是 `0xbc108c`。它不需要逻辑 replay，也不重放 PrepareInvoke。

此点的实际角色：`RBP=event`、`R15=returned executing list`、`R13=原 Single MethodInfo`、`ESI=0`、`ECX=0`、`XMM6.low32=原始 float bits`。`XMM1` 在 PrepareInvoke 后不是可依赖的 payload 保存处。先同步 root R15（及需要独立保留的 event），再读类型、count、version、数组和元素；不能先检查对象再补根。null event/Prepare 异常/返回 null 到不了该点，绝不是 empty-list receipt。

## 3. 三条实际 dispatch CALL 与不同 ABI

| site / CALL / 原字节 | 硬件 return / post 9B / 回跳目标 | 实际 target、参数与角色 |
|---|---|---|
| 0 / `0xbc1107` / `ff9098010000`，6B | `0xbc110d` / `ffc68bcee97affffff` / `0xbc1090` | target=`[saved RAX+0x198]`；`RCX=RBX=invokable`，`XMM1=XMM6`，`R8=[class+0x1a0]` MethodInfo。这是 typed virtual 分支；`InvokableCall<Single>`/cached 子类身份仍须官方 metadata 与实际 vtable target 验证。 |
| 1 / `0xbc115d` / `e84ed49800`，5B | `0xbc1162` / `ffc68bcee925ffffff` / `0xbc1090` | direct target=`0x154e5b0`；`RCX=当前 invokable`，`RDX=0` MethodInfo。它走零参数 invokable 路径，不得把 XMM1 当作传入 float。 |
| 2 / `0xbc121b` / `ff9078010000`，6B | `0xbc1221` / `ffc68bcee966feffff` / `0xbc1090` | target=`[saved RAX+0x178]`；`RCX=R14=invokable`，`RDX=[RBP+0x28]` 实际 object[]，`R8=[class+0x180]` MethodInfo。该数组的第 0 项此前由 XMM6 保存值装箱、写入并走 GC barrier。 |

三 site 的公共角色都是 `RBP=event`、`R15=list`、`ESI=index`、`R13=Single MethodInfo`。每个 site 前分别有真实 `get_Item`：`0xbc10a6/0xbc1122/0xbc1177 -> 0x9a40b0`；它们是三个分支探测步骤，不是三个 listener occurrence。只在实际 listener CALL 发行 dispatch generation；同一个 invokable 指针出现在两个 index 时是两个 occurrence。

6B 虚 CALL 可以沿用“首字节 NOP + 5B CALL pre relay”的宽度策略，保持原 return PC 为 source+6；5B direct CALL 保持 source+5。pre helper 读取实际虚目标一次，恢复全部机器状态后 tail-jump 原目标，不能吞掉或重试原目标读取的 SEH。post 在原 return PC 跳到 stub，observer CALL 后 replay `inc esi; mov ecx,esi; jmp 0xbc1090`。三处 post 均为完整 9B 指令窗。

site 2 必须先保留实际 RDX 数组及其实际 boxed element，不能换成 event 最新字段值；检查 boxed Single 时比较 raw32，保留 NaN payload、signed zero、Inf、subnormal。site 0 的 cached Single 子类可能以 `m_Arg1` 代替动态 payload；已有 `InspectRootedCachedSingleInvokable`/publication helper 可提供官方字段与固定 bits 证据，不能将原始 Invoke bits 直接当作所有叶子的有效实参。

## 4. 复用边界、parent 和 unwind

可复用 `RizEventMidFrameV1` 的完整 GPR/XMM/RFLAGS/MXCSR/FPCSR 保存语义、普通 CALL 和保持 source return slot 的 pre-tail 技术；不可直接复用旧 listener 的 receiver decode。旧代码固定 `RSI=event/RBP=list/RBX=index`，native parent 来自 `RizVoidBootstrap_CurrentListenerParent`，broker 也只接受 `NativeVoidPhysicalIdentity`。Single 的寄存器、typed ABI、operation slot `(kind=3,slot=31)` 和 parent 原调用窗口都不同。

下一 native producer 应在实际 `OperationContext` Single entry 内、observer snapshot 所属 generation 已确定时创建私有 TLS parent；保存 native epoch/entry sequence/event/method/value bits，以及本次 entry 冻结的 producer observer。管理回调 Enter 返回后才进入 OriginalWindow。mid/pre/post 必须匹配该栈顶 parent、OS thread、source RSP、真实 source return PC 和 site；foreign/nested/unattached 调用掩蔽祖先，不搜索旧 owned parent。晚 Attach 不能借已进入原函数的旧 generation。未观察到 mid、重复 mid、缺 post、token/目标/序号不符、溢出或 observer 故障都保留 Unknown 责任；原函数和原异常继续执行。

old CHAIN0 在 `0xbc106a..0xbc1087` 又保存 RSI/R12/RBX/RDI/R14。动态 post stub 只链 primary RF 会漏掉这五个保存点。可验证的候选模板是当前 `Operation25Continuation1Unwind` 的 36B：附加 SAVE_NONVOL 全部 CodeOffset=0，CHAININFO 尾部指向 primary tuple；但必须用真实 `RtlLookupFunctionEntry/RtlVirtualUnwind` 对 stub 的所有 CALL/replay/跳转边界逐点验证，不能凭形状直接接纳。mid 自身的 full-save helper 有自己的合法 unwind，返回 `0xbc108c` 后落在实际 CHAIN0 prolog 完成区。

## 5. 下一包的接口和发布计数方案

先做独立 `TweenSingleEventContext.h/.cpp`、全保存 ASM、owned caller ASM、machine fixture 和专用严格构建脚本。Configure 只供同 helper 的私有 FirstInstall 路径；没有传入 Ready/bool 的公共替代证明。拟公开原生入口为 GetMidEntry/GetPreEntry/GetPostEntry/Attach/GetInfo/ReadCurrent 共 6 个；ReadCurrent 仅在 native TLS 当前同步 delivery 中接受那个实际 frame 地址并复制快照，拒绝 caller DTO/过期副本/worker。parent Begin/OriginalWindow/Close 只由实际 OperationContext 调用，不导出。V1 observer frame 同时包含 parent native sequence/epoch、child sequence、stage/site、event/list/invokable/index、实际 target、实际参数寄存器、raw float bits、source/post transport PC 和 RSP；machine 指针仅在同步回调中有效。

本阶段 owned fixture 的配置与 metadata 是受控前提，不是实际游戏 FirstInstall Ready。当前冻结 V6 仍为 26 operation / 57 writes / 22 Tween source / 77 exports。

正式接入时建议整体升一个新的 FirstInstall receipt：新增 mid 一次写及三组 pre/post 六次写，**57 → 64 attempted game writes**；operation 仍 26、Tween source 仍 22。若以上 6 个入口全部发布，helper EAT **77 → 83**。新 receipt 增补独立 Single publication 身份、7 source span、3 post stub/RF/unwind、source protection/readback 和新 lane epoch；确切 layout/大小由 root 同步 C#/native/pack/verifier/install 后冻结。不得现在局部改变 V6 的常量或将新 span 藏进原 57 次写。

发布前核完整 647B 原文、四份 unwind、静态 pdata 与 OS RF；发布后只认可七个精确 span 的 sealed expected patch，其他字节继续全量校验，不能忽略整个 continuation。动态 block 所有字节、动态 RF、完整展开链、相对可达性、保护恢复、cache flush 和失败后的资源责任都需要独立证明。

## 6. 可执行任务与验收顺序

| Path / 工作项 | 实施输出 | 必须拿到的验证 |
|---|---|---|
| P1 / WI-S1 原生 producer | 私有 Single parent、真实循环前 mid、三 pre/post dispatch、新 native-only wire | owned machine fixture 通过实际 Single1 Operation entry；空 list mid 恰一次、非空三路径/重复元素/多 index；target/MethodInfo/float rawbits 全相符。 |
| P2 / WI-S2 原生异常与保全 | 原 CALL/尾跳/post replay、TLS mask 与缺 post 关闭 | observer C++/SEH/非零失败不抑制 original；original C++/SEH 不被 observer catch 吃掉；mid 后第一 CALL 前异常、各 listener throw、target-load fault、nested foreign/worker/late Attach/容量与序号拒绝。缺 post 发实际 abnormal，保留根/记录。 |
| P3 / WI-S3 首次安装与 CLR | root 统一新 receipt、64 writes、83 EAT、官方 closed metadata、root-first list/dispatch capture | 精确七 span 与每一字节篡改、四 RF/pdata/unwind、真实 OS unwind、真实 CLR ABI；禁止 caller DTO/字段 bool 签发 producer/parent。actual list 和每次 dispatch 与同一 Single native sequence 私有绑定。 |
| P4 / WI-S4 effects | typed 原生 occurrence 接实际 SourceOccurrence/InstantWork，empty receipt 与非空派生责任分开实现 | empty 必须有本次真实 mid 的 rooted count=0、writer generation 稳定、真实 After/Dispose 与最终原生 drain；非空/opaque/multicast/cached/setter/新 Tween/协程效果要具体 owner transfer/end，均不能按 Return 或 listener removal 结束。 |

现有 mutation profile 明说只涵盖非泛型方法，`InvokableCall<Single>.add/remove_Delegate`、generic upper writers 与 serialized/direct field replacement 不能被当作已闭合。`OperationRootReceipt` 也仅在 Before 的 `CapturingObserver` 中有效，不是 mid OriginalWindow 能力。新增 typed physical identity 应按真实 parent 窗口发行，不能重用旧 Before root receipt。

### Findings 和调用路径

| Finding | status / confidence / evidence_ids | location / impact |
|---|---|---|
| F1 | validated-static / high / E1,E2,E3 | `0xbc1087` 是空/非空必经的一次性列表 mid；Runtime 现有 Single entry/exit 不提供它。 |
| F2 | validated-static / high / E1,E2,E3 | 三 listener 分支 ABI 不同；旧 Void listener decode/physical parent 不能直接套用。 |
| F3 | candidate / high / E1,E2 | 新 post stub 必须恢复 CHAIN0 额外保存；完整模板及 OS boundary fixture 是接纳条件。 |
| F4 | candidate / high / E3 | Single list/dispatch producer 只补可观察输入；writer/effects 关闭仍需 P4。 |

P-callflow（path_type=callflow）：实际 Play/Execute 的五个 pinned CALL → 当前 Single1 entry sequence（E3/F2）→ `0xbc1059` 真实 PrepareInvoke → `0xbc1087` list mid（E1,E2/F1）→ 三条实际 listener CALL 的单独 child generation（E1,E2/F2）→ native abnormal/returned close（P1/P2 owned 验证见第 7 节）→ root 验证的 source occurrence/empty 或具体 effect owner（P4 待实现）。本段 E1–E4 是初次静态定位证据，没有游戏动态运行、实际 metadata、CLR 或正式新 publication 验收；第 7 节独立补充 owned producer 验证，不能将其提升成游戏运行能力。

检查记录：已固定 PE/range/hash；已记录 imports/exports 与工具不可用限制；已完成 Evidence→Finding→Path；范围限定 647B；没有修改任何冻结编译源；没有把计划、静态定位或受控 fixture 前提写成生产能力。

## 7. P1/P2 owned producer 已冻结的实际证据

本次接续只修改 Single 的六份独立文件；沿用 `OperationContext.cpp` 中已有 `RIZ_TWEEN_SINGLE_EVENT_FIXTURE` 最小接点，没有修改 production 分支、FirstInstall、Profile、C# SDK、Adapter、pack 或 verifier。授权与范围仍是本文开头的任务和 [SPEC.md](SPEC.md)；这是本地 native 实现与受控机器 fixture，不是安全攻击 case，不建立无关 scope/IOC/ATT&CK。只读游戏静态观察仍严格限定 E1–E4 的 647B；本次没有再次展开 callee，也没有启动、加载、安装或修改游戏。

冻结目录为 `D:/riz-build-cache-next/tween-single-event-a-r10-20261029`。实际 `/W4 /WX /O2 /EHa /MD /guard:cf` 构建退出 0，42 个独立进程 mode 全部退出 0，合计 **33,792 checks / 0 failures**。冻结后独立复核 14 份当前源码 hash、ZIP 内 14 个 entry 的长度/hash、144 个产物 hash，全部一致。归档包含实际编译过的 OperationContext 与依赖；归档不依赖将来工作区源码的状态。

| Evidence / 工作项 | source_ref / SHA256 | 观察与复现 |
|---|---|---|
| E5 / WI-S1,WI-S2 构建与输入 | `fixture-manifest.json` / `0744a283c054c28d15c7e21fafc2f8e0d6d4daa9a7812906011bbb3faefe18c4` | 目录均为上述冻结目录；`passed=true`、`compileExitCode=0`、`sourceHashesUnchanged=true`、planned/actual modes 均 42。 |
| E6 / WI-S1,WI-S2 每进程实际执行 | `artifact-hashes.json` / `93dbc92c41e490c5f16bc954d4e49df1eeed9d20835cfd0451bee2416fccf2a6` | 固定 144 个文件 SHA256，包括 build.cmd/log、EXE/OBJ/PDB、全部 stdout/stderr/result 与 manifest；每 mode 的 exitCode、日志 hash 和 JSON 同时在 E5 中记录。 |
| E7 / WI-S1,WI-S2 受控 EXE | `TweenSingleEventFixture.exe` / `2bf7b09ac2d645a9980b75b90386a0cf5935c3e200b3bdea15745c41695b8633` | 216,576B；只执行 owned EXE、OS unwind 与本地 helper 源，没有 Dobby/游戏装载。 |
| E8 / WI-S1,WI-S2 不可变源码 | `single-machine-sources.zip` / `2c219dd893c7f67f5827bdd026910268d0bee9917daeed4599d6dd63eae63859` | 43,888B，14 entries；每个 entry 与 E5 source row、当前源码一致。 |

可直接复跑已有 EXE 的单项，例如 `& 'D:/riz-build-cache-next/tween-single-event-a-r10-20261029/TweenSingleEventFixture.exe' original-cpp-2`。完整重建必须选择**不存在的**输出目录，脚本拒绝覆盖失败或成功原件：

```powershell
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoLogo -NoProfile -File 'C:/Users/71957/Desktop/riz_toolbox/scripts/Test-TweenSingleEventNative.ps1' -OutputDirectory 'D:/riz-build-cache-next/tween-single-event-a-r11-20261029'
```

此命令记录的是 r10 冻结源码的复现方法；r11 当时未运行，不能按此命令的存在声称已通过。默认实际 vcvars 为 `C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/VC/Auxiliary/Build/vcvars64.bat`。r10 `build-single-machine.cmd` 保留完整实际命令；C++ 使用 MSVC 14.29，ASM 使用同一 BuildTools `ml64`。

| 冻结编译源码 | bytes / SHA256 |
|---|---|
| `TweenSingleEventContext.h` | 4323 / `596f4e7b2c54163f7dd13453fd94fd7c15d679ae0a74b8564d5961d96443b52f` |
| `TweenSingleEventContext.cpp` | 17963 / `6df523988f74ccf78c41be60f958ae77b7f68fabea418aafaf4f65142d434ec8` |
| `TweenSingleEventEntry.asm` | 4794 / `2dc8e43940af78e978fc2d7d743ecc28187fb70197b815a6ebdb56d6d928ca0b` |
| `fixture/TweenSingleEventFixture.cpp` | 35877 / `53bc1896bf8457e953fa3d4c20968ab62be2df853326b6c212089fa30b44772f` |
| `fixture/TweenSingleEventFixtureCaller.asm` | 6973 / `de28a8c5e2fa89d0a8052c13df36f7320086d3981d3110d9bf595970d2bb0eda` |
| `scripts/Test-TweenSingleEventNative.ps1` | 9712 / `505874abb63d93979b7bc5f0f7b2571786176e7e6047f9084d35f660dfa63360` |
| 已有 conditional 接点 `OperationContext.cpp` | 17623 / `83101343fd144fb193493351763356c0972f007a9a2ce7bd675683a55f2a7e56` |

除脚本外表内 native 相对路径均从 `native/RizToolbox.JudgeContext/` 起算。脚本是 repo 相对路径。

### 7.1 正向 producer、ABI 和责任保留

每次 owned invocation 通过真正 `RizOperationContext_GetEntry(RizOperationSingle1Void,31)` 调用实际 `SingleEntry`/`Dispatch`。私有 CaptureEntry 在 Operation observer snapshot 之前发生；Begin 使用 native entry sequence/parent epoch，Original 在 Before 返回后开启，Close 在真实 `__finally` 中执行。fixture 观察者从 `ReadCurrent` 读取真实 TLS delivery 地址，核对 native parent 的 event/method/caller/rawbits/token 与当前 Operation Before 帧；没有 caller DTO 发行 parent。

`happy` 实际调用 16 次 original：8 个 raw32 各跑一次 empty 和 mixed list；empty 与 nonempty 均恰一次 mid。mixed 为 typed、zero-argument、object[]、同一 typed 指针再次出现四个 index，共 32 个 listener CALL，各有真实 Before/Returned 和独立 child sequence。raw32 包含 `+0`、`-0`、正负 Inf、正负 NaN payload、最小 subnormal 和普通值；site 0 检查 XMM1 与 R8 MethodInfo，site 1 检查 RDX=0，site 2 检查真实 RDX 数组、box rawbits、R8 MethodInfo 与 R14 invokable。三个实际目标内部 `_ReturnAddress()` 均精确等于各自 source return PC。

`machine-state` 共 9 次**实际执行前后**比较：mid 前/后 1 次、pre 到原目标入口 4 次、post 观察点经真实 replay 回 loop 4 次。每次比较全部 16 GPR、16 XMM、RFLAGS、MXCSR、FPCSR；pre 按 CALL 的 8B return slot 修正 RSP，post 仅允许原始 `inc esi; mov ecx,esi` 的规定状态变化。此项不是 synthetic CONTEXT 展开检查。

| mode 组 | 实际执行与断言范围 |
|---|---|
| `late-attach` | 第一 entry Before 内 Attach；该已进入 invocation 没有 producer 回调，第二 invocation 才收到本代 observer，两个 original/listener 均执行。 |
| `nested`, `nested-same` | 不同 event / 同 event 实际 Single entry 重入，native TLS 取栈顶 parent；嵌套返回后原 delivery 恢复。 |
| `foreign`, `worker-foreign`, `worker-owned` | raw original 不经 Operation entry，同线程或 worker 均不能借 ancestor 生成回调；worker-owned 通过自己的真实 entry 取得独立 parent。两 original 和全部 8 listener 继续执行；foreign 导致 Unknown 并保留有责任的 parent。worker 使用 join 同步，本项未宣称多线程并发压力测试。 |
| `observer-*`, `mid-*`, `returned-*` | Mid、Before、Returned 各实际抛 C++、SEH 或返回非零，共 9 mode；边界隔离 observer 故障，原函数继续。下一次调用 original/listener 继续执行而 producer 不重发授权，Unknown/retained 仍在。 |
| `original-cpp[-1/-2]`, `original-seh[-1/-2]` | 三 dispatch site 分别由原目标真实抛出 C++ payload 或 SEH payload；外层捕获原 payload，原 C++ 析构实际运行；未返回 child 在真实 Operation finally 中变成 Abnormal/MissingPost 并保留。 |
| `before-call-cpp`, `before-call-seh` | 已实际 mid、尚未第一次 listener CALL 时原 body 真实抛异常；不伪造 child 或 empty 完成，保留 parent。 |
| `target-seh`, `target-seh-2` | site 0/2 的 vtable 目标地址位于实际 PAGE_NOACCESS；MethodInfo 读取仍可达。原目标读取 AV 从 observer 边界外继续传播，target-read-fault 标志与 retained child 保留；没有吞掉并重试目标读取。 |
| `abnormal-cpp`, `abnormal-seh`, `abnormal-nonzero` | 原 listener 已有异常时，Abnormal observer 再抛 C++/SEH 或返回非零；外层仍接到最初原异常。 |
| `missing-post-0/-1/-2` | owned source 真实 CALL 原目标后跳过对应 post helper；所有 4 listener 正常执行。真实 parent close 发现未匹配 child，发 Abnormal/MissingPost；正常 Return 不伪加 ParentAbnormal。site 0 重复指针的两个 index 分别保留。 |
| `parent-overflow`, `child-overflow` | 真实进入第 33 层 parent / 连续 257 次 listener，分别达到 32 栈深和 256 child 上限；overflow=1，原函数继续，32 parents / 256 children 的责任记录保留，inFlight 最终归零。 |
| `no-mid`, `duplicate-mid`, `null-list` | 真实跳过 mid、两次走同 mid CALL、null R15；只接受一次合法 mid，无 mid 的 invocation 不能发行 child。null-list 的 AV 来自 owned body，不是实际游戏 PrepareInvoke 的异常路径证据。 |
| `worker-read`, `stale-read`, `alias-read`, `copied-read` | 当前地址跨线程、回调结束地址、输出覆盖当前 frame、复制帧地址均拒绝；Unknown 保留。异步或复制帧没有 native physical authority。 |

42 mode 共捕获 **14 次真实原异常**，包括原 C++/SEH、mid 后异常、实际目标地址 AV、null-list AV、原异常与 observer 二次故障。该统计不包含 synthetic CONTEXT，也不包含已隔离的 observer 故障。所有 mode 均检查 Operation Before/After 数目配对、native parent/child inFlight=0、retirement reservation=0 与 entered/exited 相等；Unknown/retained 没有被这些零计数清除。

### 7.2 OS 展开边界与已废弃形状

owned caller 分成**两个真实静态 RF**：primary 保存 RBP/R13、XMM6/R15 和 0x48 栈；continuation 在相同真实 Single 栈槽保存 RSI/R12/RBX/RDI/R14，再以 CHAININFO 链到 primary。三个动态 post stub 用 36B xdata：五个 SAVE_NONVOL 的 CodeOffset 全 0，链尾是实际 owned primary tuple。`RtlLookupFunctionEntry` 查的是真正注册的静态/动态 RF。

已接纳的 owned post stub **21B** 为 `E8 rel32(post)` 5B、`FF C6` 2B、`8B CE` 2B、`0F 84 rel32(loop)` 6B、`0F 85 rel32(loop)` 6B。两个条件分支目标相同且覆盖 ZF 两值，不额外改变寄存器或 flags。三个 stub 的真实指令边界 **+0/+5/+7/+9/+15** 各用 OS `RtlVirtualUnwind` 检查，共每进程 15、全 run 630 次；synthetic CONTEXT 使用 owned 控制栈，断言原 caller RIP/RSP、RBP/R13/R15、五个 continuation 寄存器和 XMM6 全部正确恢复。这些是实际 OS API 检查，**不是在每一个边界真实抛异常**。

三个 full-save helper 每次同步回调另作 10 个 synthetic CONTEXT 检查：入口、push R13 后、PUSHFQ 后、sub rsp 后、建立 R13 frame 后、dispatch CALL 前/后、恢复 epilog LEA 前、LEA 后 POP 前、POP 后最终 RET/FF25 前。检查真实 `RtlLookupFunctionEntry` 返回的 helper RF、固定 prolog/epilog 字节、frame register=R13、非易失 GPR 与 XMM6–15；全 run **6,380 次**。这些模拟部分 prolog/epilog 的 CONTEXT，不能宣称有 6,380 次真实异常。

实际异常的范围是 7.1 所列 original C++/SEH、target-load AV 和 observer 故障，通过实际 helper/source/原目标/Operation finally 展开；没有在所有 stub 或 helper 指令边界逐条引发异常。机器状态 9 次比较、OS synthetic 边界检查、真实异常三类证据分别记录。

失败原件保留：旧 `tween-single-machine-a-r1/r2/r3-20261029` 真实包含 MASM/严格警告失败和 r3 的三个 OS 展开失败，并非仅 plan；新 `tween-single-event-a-r1-20261029` 实际 original-cpp 未能穿透，owned source 的 FF25 post JMP 被 OS 作为已完成 epilog；随后改成真实 E9+4NOP source patch。分离 primary/continuation 后，新 r3 在 stub **+9 的 E9 尾跳**仍被 OS 作为已完成 epilog、跳过全链；r4/r8 含各自严格 MASM 失败。r5 起接纳上述 21B 双条件分支，r10 才是本节最终 42 mode 与 hash 冻结。旧失败目录均未覆盖或删除；不能将旧 14B/E9 stub 直接套用到 Single。

### 7.3 Findings、调用路径与未覆盖项

| Finding | status / confidence / evidence_ids | location / impact |
|---|---|---|
| F5 | validated-owned / high / E5–E8 | Single native TLS producer 从实际 Operation entry 关联一次 list mid 和三个 ABI 的逐 occurrence CALL；支持同指针不同 index，晚 Attach 不追溯。 |
| F6 | validated-owned / high / E5–E8 | 原 listener/target-load 异常与 observer 故障边界分离；真实 finally 发现缺 post 时保留记录与 Unknown，原异常继续。 |
| F7 | validated-owned / high / E5–E8 | 有 continuation 额外保存时，14B/E9 stub 的 +9 展开不可接纳；21B 双条件跳转加完整 36B chain 在 owned OS 边界和实际异常范围内通过。F3 静态候选据此被修正，仍待固定游戏 FirstInstall 的独立 P3 复验。 |

P-owned-single（path_type=callflow）：真实 Operation Single1/31 entry（E5,E8/F5）→ 私有 parent snapshot/OriginalWindow → owned continuation 一次 mid → 三条 patched source CALL 的 pre-tail 与原目标 → 真实 source return、21B post observer/replay，或真实 Operation finally 的缺 post Abnormal（E6,E7/F6,F7）→ inFlight 归零但失败责任记录不清除。timeline 为：沿用 E1–E4 静态定位；旧 machine r3 actual failure；new event r1 original exception failure；new event r3 split-RF E9 boundary failure；r5 21B shape 接纳；r10 42 mode 全过及独立 hash/ZIP 复核，均在 2026-10-29，旧失败仍在。

**P1/P2 仅冻结本节所列 owned 范围。** 尚未覆盖：真实游戏 PrepareInvoke/类型与 closed metadata/vtable 身份；真实 CLR GCHandle 的 root-first list/array/box 捕获及 cached Single 生效实参；真实 FirstInstall 七 span、动态 block seal、失败安装资源责任、四 RF/pdata 的发布前后核验；生产 DLL 的 CLR 回调与 V7 SDK 布局；序列耗尽/保留池耗尽与任意 hostile ABI 输入的穷举；同时并发压力；通用 typed writer 闭合与 effects owner/end。retained parent/child 是 native 责任**记录**，不是本 fixture 已创建的 CLR GC root。Unknown 不等于 effects 已结束。当前正式 helper 仍是冻结 V6 的 26 operation / 57 writes / 22 source / 77 EAT，本批不能将 42 mode 提升为生产 Ready。

## 8. 提交 root 统一的 private publication 建议

以下是供 V7 讨论的**建议**，没有编译、导出、安装或修改 production。建议 Single publication 是当前 receipt 的一个独立 typed section，由 FirstInstall 私有配置和 sealed state 发行，不能由 managed caller 传入地址、Ready/bool 或已复制 frame 构造。它必须与 operation row 25 的 immutable `(kind=3,slot=31,target,original,entry,epoch)` 匹配，Single epoch 单独记录。公开只增加六个已经列明的 producer 入口；新的 current FirstInstall export 替换 V6 名称，EAT 总数仍是 **77+6=83**。

建议 Windows x64 8B packing 的 section 共 **416B**；每项只表达 immutable 发布身份或实际计数，`phase/unknown` 来源于私有 seal，绝不是 caller 授权位：

| offset | 字段与宽度 | 合同 |
|---:|---|---|
| 0 | `u32 capabilityVersion,byteSize,phase,unknown` | 新 section 版本与精确 416B，读失败保留 Unknown。 |
| 16 | `u32 operationId,kind,slot,expectedWrites` | 固定 `25,3,31,7`，外层总 writes=64。 |
| 32 | `u64 epoch,parentEpoch` | producer native claim / 实际 Single operation claim。 |
| 48 | `ptr midEntry,preEntry,postEntry` | 必须等于 helper 本身三个固定 entry。 |
| 72 | `ptr allocation; u64 allocationSize` | 实际近端 block，整块 bytes、保护、所有权与 failure retention 私有封存。 |
| 88 | `ptr midRelay,preRelay,postRelay,directTarget` | 四项共 32B；directTarget 仅 site 1；三 relay 若为 leaf，无额外假 RF。 |
| 120 | `ptr spanPc[7]` | 顺序固定为 `mid,pre0,post0,pre1,post1,pre2,post2`。 |
| 176 | `ptr sourceReturnPc[3],midReturnPc` | 真正硬件 CALL return PC，site 0/2 仍为 source+6，site 1 source+5。 |
| 208 | `ptr postStub[3],postTransportReturnPc[3]` | stub+5 才是 post CALL return，不替代 source return 身份。 |
| 256 | `ptr functionTable; u64 functionTableBase` | 3 个真实动态 RF，base 与模块相同且所有 RVA 可表达。 |
| 272 | `ptr unwindInfo[3]` | 每个完整 36B，五个 zero-offset SAVE_NONVOL + 实际 primary tuple。 |
| 296 | `u32 spanWidth[7],protection[7]` | 宽度固定 `9,6,9,5,9,6,9`；保护记录实际读回值。 |
| 352 | `u32 stubSize[3],unwindSize[3]` | 固定各 `21` / `36`；seal 每一字节，不能只读 header。 |
| 376 | `u32 registeredFunctionCount,lookupChecks,boundaryChecks,reserved` | 真实 RF=3、每 stub 5 个边界即至少 15 lookup 与 15 boundary checks，reserved=0。 |
| 392 | `u32 configuredMask,publishedMask; u64 attemptedWrites,completedWrites` | mask 仅低 7 位；部分安装保留真实 attempt/complete，不能用 expected count 冒充完成。 |

若 root 保留现有 operation matrix 的内部 2112B 存储形式并追加该 section，新 current packet 的**候选**大小是 2528B，顶层 abiVersion 改为 7；这不是已经冻结的 C#/native layout。无需发布旧 V6 getter，也无需保留旧 API。root 可调整最终大小，但 SDK、native、FirstInstall、pack、verifier 必须一起冻结，不能先局部升常量。

P3 接纳门：七 span 发布前完整 647B/四 unwind/pdata/OS RF 匹配；先构造并 seal 动态 block/全部 RF/xdata/相对可达性；对 21B shape 的 +0/+5/+7/+9/+15 逐点做 OS 展开并核五个额外保存，再写七个精确 source span；每次写之前计 attempt，完成保护恢复/readback/cache flush 后计 complete；source 只接受精确 expected patch，其他正文每字节仍校验。`ReadCurrent` 仍只能接受当前同步 frame 地址，不增加公共 Configure/Begin/Close。失败后的资源不能卸载或重用来清除原责任。本建议只交付 root 统一 V7 事务，不改变本批冻结 V6。

## 9. P3 native V7 publication 后续实施与冻结

第 7 节是此前 P1/P2 批次的历史证据。随后 root 冻结 [SINGLE-V7-CONTRACT.md](SINGLE-V7-CONTRACT.md) 并授权正式 native 集成；本节记录这次**后续**结果。当前 native sole reader 已改为 `RizFirstInstall_GetInfoV7`，2528B packet、416B `singleEvent@2112`、26 operation、22 source、64 attempted game writes、83 EAT。第 8 节的建议由合同接纳并落实，正式当前版本不再是 V6。C#/包合同与实际采用由 root 统一；本子批次没有修改 C#、pack verifier 或候选安装内容。

新增 `TweenSingleEventPublication.inl` 是 `FirstInstall.cpp` 匿名 namespace 内的私有实现，复用真实 WriteSource、事务状态和 snapshot 锁。production Single entry 现在实际编译/链接 `TweenSingleEventContext.cpp` 与 `TweenSingleEventEntry.asm`；保留 fixture 条件只供 Admission/Single 自有 EXE，production 不再依赖 `RIZ_TWEEN_SINGLE_EVENT_FIXTURE` 才能接线。私有配置读实际 Operation row 25 epoch，再读 Single GetInfo 的实际配置 epoch，没有用常量填写生产 receipt。

动态 block 为 RX `MEM_PRIVATE` 4096B：三个 21B post stub 位于 `+0/+0x100/+0x200`，完整 36B xdata 在各 `+0x40`，三行 RF 在 `+0x380`；mid/pre/post 的 FF25 leaf relay 各在 `+0x500/+0x520/+0x540`。其他字节全部固定 CC。native 全 4096B 封印；独立 mapped fixture 从语义和精确固定 bytes 重建全部 4096B 再比较。原函数 647B 的四个 RF、所有 xdata、pdata 保持完整身份检查；仅七个精确窗可被各自 expected patch 替换，continuation 其他每个字节继续比较，未忽略整个 continuation。

七段 `configuredMask` 在生成 block、RX/flush、RF 注册及实际 context 配置完成后记录；每个实际 WriteSource 调用之前增加 Single attempt，成功保护恢复/flush/readback 后增加 complete 和 published bit。外层 `writesStarted` 继续由实际 BeginGameWrite 累加，最终读取实际值，未从 expected64 强行赋值。失败对象、RF、prepared allocation 和已写 bytes 均留在原 owner，无回滚、释放、卸载或重试消除责任。

跨退休接线保留原 Operation Single1 的入口 reservation，并在 mid/pre/post 使用 Single reservation；observer fault、缺 post、读拒绝、overflow、retained 与 FirstInstall Unknown 都将共同 retirement owner 置为 sticky Unknown。完整 retirement acquire 还实际读取 Single 配置、Attach、fault/retained 和 inFlight 状态，拒绝尚有责任的截点。聚合 retirement fixture 的新增 SingleInfo inFlight 门是**controlled counter 输入**，其声明 `singleCountersControlled=true`；42 mode 中通过真实 Single entry/helper 的 reservation 是另一类真实机器证据，二者没有混称。

| Evidence / 工作项 | 固定产物（均位于 `D:/riz-build-cache-next/`） / SHA256 | 实际结果 |
|---|---|---|
| E9 / WI-S3 正式 native build | `single-v7-native-a-r3-20261029/native-manifest.json` / `7fed28cd34b1a29280da80a620c763d9074f444d5af4f02f83b01d22ebda134b` | `/W4 /WX` compile 0；实际 EAT=83，sole GetInfoV7；94 份输入、26 个 DLL/EXE/OBJ 产物，冻结后当前 hash 零差异。ABI 31,261 checks，真实 Dobby 对 owned 函数 prepare/commit 61,358 checks 均通过。 |
| E10 / WI-S3 正式 helper | `single-v7-native-a-r3-20261029/RizToolbox.JudgeContext.dll` / `30ec8ed7245432ff81490cc346fb13d03dca060bb0ef9575f90e2a3078779bde` | 与 E9 实际 build 绑定；源码 ZIP SHA `2b49b58f567a7cc625b416648fbadd6af19781ffe0acbee8e650b3b19f9b6ad2`，包含实际 `.inl`，不漏 include 实现。 |
| E11 / WI-S3 固定 PE 与七 span | `single-v7-profile-a-r2-20261029/profile-result.json` / `4277ad1e1f7114d1d24a699c55c4fb63d4c61ccc06ec3e24f2113cf11ea54f02` | 仅读文件，核完整 647B/四 RF/xdata/pdata/七原始 span SHA。实际编译 `.inl` 和 profile 中 publisher SHA 均为 `391d8fc538f8d26e34ec2c2c145499f38a65cecc37ee68d8fb9304d925962d0d`。 |
| E12 / WI-S3 正式 mapped publication | `single-v7-mapped-a-r4-20261029/fixture-result.json` / `45266e740d7606334bc358cba143c128bfc1e92fc494628976249b0b10791995` | 182 个独立进程，256,673 checks，0 failures；实际 V7/2528/64、17 invalid-header cases、section parentEpoch=实际 row25 epoch、native producer counters 零、三 RF/15 OS synthetic boundaries 均通过。 |
| E13 / WI-S3 原子 Unknown 与部分写入 | `single-v7-unknown-a-r4-20261029/fixture-result.json` / `fddde0a1e62c1ce79f84d82bf383465734fd1b94ebcc73c622c4b88d72cda673` | Unknown atomic 先于每次读取，详细 writer 暂停期间连续 100 个 V7 快照均 Unknown；997 checks。七个独立 partial mode 各 20 checks，见下文。完整 result/manifest 原始 bytes base64 与 SHA、92 份 native source 来源和实际链接 21 OBJ 身份已嵌入 E12。 |

E12 的 182 modes 包含原有 41、七个 source span 共 53 个字节的逐字节篡改、80 个动态 block 边界抽样、7 个 source protection 和 1 个 block protection 变更。80 个 block offset 精确清单保存在 `singleBlockTamperOffsets`；覆盖每 stub 的 CALL/replay/两 Jcc、每完整 xdata 的 header/save/chain、每 RF 首尾、每 relay 的指令/指针、CC padding 与 allocation 最末字节。**不是 4096 个逐字节篡改进程。** 全 4096B byte-for-byte 比较与 80 个有意义篡改样本分开记载。53+80 个字节场景记 fixtureTamperWrites=1、fixtureProtectionChanges=0；保护场景反之；全部还原原输入后仍为永久 Unknown，`singlePermanentUnknownAfterRestore=true`，生产 writesStarted 仍 64。

E13 编译的是真实 `FirstInstall.cpp` 加 `RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE` 的唯一有界 seam，并链接 E9 同一 helper build 的 21 个实际 OBJ。**没有在生产 DLL 注入 fault seam。** 原旧 Unknown 脚本只链接 import library，不能解析私有 publication，实际 r1 的九个 unresolved private symbols 保留；r2 实际 EXE 已过但旧 v5 wrapper 字段拒绝也保留；r4 才是修复同源 OBJ/proof 校验后的最终通过目录。最终原件明确 `actualHelperObjectsLinked=true`、`productionDLLInvoked=false`。不增加任何 fixture export；生产 EAT 仍恰 83。

七个 partial mode 在每段**真正完成写入**并记 complete 后触发一次实际重复 Before，使事务转为 Unknown；它不是模拟计数，也不是未成功 VirtualProtect 的故障注入。每进程完整读取 RX block、三 registered RF、七段实际原文或 patch、native 配置 epoch 和重复 reentry 后的全 packet 不变：

| mode | outer 实际 writesStarted | Single attempt / complete | publishedMask |
|---|---:|---:|---:|
| `partial-single-0` | 36 | 1 / 1 | 1 |
| `partial-single-1` | 37 | 2 / 2 | 3 |
| `partial-single-2` | 38 | 3 / 3 | 7 |
| `partial-single-3` | 39 | 4 / 4 | 15 |
| `partial-single-4` | 40 | 5 / 5 | 31 |
| `partial-single-5` | 41 | 6 / 6 | 63 |
| `partial-single-6` | 42 | 7 / 7 | 127 |

所有 partial 的 configuredMask=127，phase=Unknown，resourcesRetained=true，reentryDidNotWrite=true；最后一个即使 Single 七段都写完，后续 22 source 尚未发布，仍绝不能 Ready。attempt/complete 在这些中断点恰好相等；此项没有宣称覆盖失败的 VirtualProtect/flush/readback 导致 attempt 大于 complete 的每一种 OS 失败。

同冻结源码的必要回归也已真实完成：`single-v7-admission-a-r3-20261029` 4 modes/359 checks，`single-v7-retirement-a-r1-20261029` 18/669，`single-v7-sources-a-r1-20261029` 13 modes，`single-v7-references-a-r1-20261029` 9 modes，`single-v7-rejected-a-r2-20261029` 5/200，`single-v7-single-event-a-r1-20261029` 42/33,792。Source/References 因退休 cpp 的完整 hash 变化而重新编译，未将旧 hash 包装成新通过。每个目录的 manifest/result/source archive 保留原件。

复现 E11 用 `scripts/verify-first-install-operation-profile.py --game-assembly D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll --profile native/RizToolbox.JudgeContext/FirstInstallProfile.h --output-json <fresh-json>`；复现 E12 用 `scripts/Test-FirstInstallMappedNative.ps1` 的显式 `-HelperDirectory` 指向 E9，`-GameAssemblyPath`/`-DobbyPath` 指向固定只读输入，`-SinglePartialProof` 指向 E13，`-OutputDirectory` 必须全新。E13 对应 `Test-FirstInstallUnknownNative.ps1`，同样使用 E9 输出的实际 OBJ。各目录已存实际 build cmd、stdout/stderr；这些命令只供按当前冻结源码重现，不能用命令文本代替运行结果。

F8（validated-native-mapped / high / E9–E13）：正式 V7 已真实准备并发布七个 Single span，完整原文/动态 block/RF/xdata/protection 身份参与 receipt，实际部分进度和 Unknown 责任保留。P-v7-single（path_type=callflow）：固定输入完整验证（E11）→ 同 helper 私有 Prepare/实际 epoch（E9,E10）→ native 全块/RF/15 synthetic boundary 前置验证 → 七段逐次 WriteSource 与外层真实 64 次 attempt（E12）→ 完整 seal readback 或中断后的 retained Unknown（E13/F8）。

本节不声称执行任何游戏地址：mapping 仅为 `DONT_RESOLVE_DLL_REFERENCES`，Dobby 只对隔离映射内地址准备与写入，fixture 不调用这些目标/入口/relay、原始 il2cpp_init 或游戏 DllMain。独立 Dobby ABI fixture 执行的是自有函数。真实 CLR root-first、官方 closed metadata/vtable、typed occurrence/effect owner/end、游戏首次执行排除与端到端游戏行为仍由后续真实验收处理；生产发布通过不能补齐这些未知。
