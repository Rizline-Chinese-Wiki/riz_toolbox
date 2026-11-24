# 首次安装：选择 native pre-init，post-init 只接回调

2026-10-24。1507 批源码冻结期间的只读审查；本文未构建、安装或启动游戏。

**第一版选择固定 Doorstop 的 native `il2cpp_init` wrapper：在调用原 init 之前，安装固定 UnityEvent 的父入口和 mid；初始化期间全部原生直通；原 init 返回以后，CLR 只发布回调表，不再修改游戏指令。** 这是当前最少组件、能够推进 B07/B15 的路线。下一批应直接实现 native bootstrap、回调快照 shim 与托管 adopt-existing 接线，随后做独立真实 fixture；无需先建立通用调试器、通用远程注入器或公开写地址 API。

## 支持合同与拒绝范围

[B07/B15](../../../.docs/foundation-expansion/SPEC.md) 要求的是正常兼容框架：精确 ABI、一次物理挂接、多订阅、原调用一次，以及内容/声音能力。首版声明支持固定游戏映像和自有启动链，游戏经首次 IL2CPP 初始化取得合法对象并执行 UnityEvent；原 init 内部即使启动 worker 或执行目标，也发生在安装完成之后。

这里采用的**产品前提**是：受支持启动中，首次原 `il2cpp_init` 之前不存在已经运行、能够合法取得该 runtime 的 UnityEvent 实例并调用它的游戏路径。Unity 的 [UnityEvent 参考源码](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Runtime/Export/UnityEvent/UnityEvent.cs)将它实现为托管对象、调用列表和委托；这支持上述运行期依赖的解释，但不是 Windows 全线程停止或所有 Unity 版本的启动顺序保证。应对固定构建验证这条启动合同；不能把它写成已获官方通用保证。

不需要证明恶意 native 插件不能把任意整数当指针、伪造对象或直接跳进 RVA。拥有同进程任意本机执行权的插件可以破坏任何框架合同，本项目不提供这种隔离。正常高级插件仍必须遵守初始化阶段、对象有效性和 broker 入口规则。已知其他 loader 先初始化 IL2CPP、已经启动的进程热接入、预先取得并绕开 broker 的 trampoline、合法的 pre-init 游戏调用等，属于本版**不支持的启动方式**，应明确拒绝首次安装；不能悄悄把它们称为恶意行为而继续写入。

这收窄了 [first-install-options](../foundation-void-admission-20261024/first-install-options.md) 中“证明所有直接函数指针路径”的要求：应验证受支持游戏和兼容 loader 的合法生命周期，不把任意恶意代码的可达性作为无限验收目标。当前尚未运行该生产启动合同；本文件也不把 1507 项回归升级为它的证明。

## init ABI：官方声明与当前映像相符

固定 Doorstop commit `33dab9a6733862eb81869ff08431d9478b28784b` 的 [runtimes/il2cpp.h](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/runtimes/il2cpp.h) 声明 `int init(const char* domain_name)`，Windows 定义 `__cdecl`；[func_import.h](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/runtimes/func_import.h) 在 ENV64 使用平台默认调用约定。[bootstrap.c](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/bootstrap.c) 的实际 wrapper 先保存原 init 的 `int` 结果，再启动 CoreCLR，最后返回该结果。[Unity 官方仓库所带 API 头](https://github.com/Unity-Technologies/SimpleUIDemo/blob/master/Tiny3D/Library/DotsRuntimeBuild/artifacts/Stevedore/il2cpp/libil2cpp/il2cpp-api-functions.h)也声明 `DO_API(int, il2cpp_init, (const char* domain_name))`；该头不是本机 2022.3 的版本证明。

本次另外只读解析实际 GameAssembly 的 export directory，并反汇编 `il2cpp_init` 导出的固定首段；没有扫描 script.json 或跟踪其他函数。文件 SHA-256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，导出 RVA 为 `0x2841C0`，到第一条 RET 的 40 字节为：

```text
40534883EC20488BD9488D152FC3540133C9E8CD5F0C00488BCBE8519D02000FB6C04883C4205BC3
```

实际代码保存 RCX 参数，在 `0x2841DA` 调用后于 `0x2841DF` 执行 `movzx eax,al`，再恢复栈并返回。它与上述 32 位返回 ABI 一致；不能改成 void、IntPtr 或猜测 domain 指针返回。建议直接复用固定源码类型：`using Il2CppInitV1 = int (__cdecl*)(const char*);`。原 domainName 不复制后替换，原函数调用一次，int 结果原样返回；原 SEH/C++ 异常继续原生展开，失败或异常时不启动 CLR。不要根据本段反汇编新增未经核实的“内部 init”地址。

## 这一轮能直接实现的最小组件

| 组件 | 最小责任及接口方向 |
|---|---|
| 固定 native wrapper | 在现有 `init_il2cpp(const char*)` 中依次执行 `BeforeInit`、真实 original init、成功后的既有 CoreCLR startup。仅给固定源增加小接缝，不先维护大规模 Doorstop 分叉。 |
| `BootstrapBeforeInit` | 私有输入是已解析的原 init、其真实 GameAssembly 模块实例及固定 profile。核身份/代码/ABI，准备 Dobby trampoline、native slot、mid relay，配置一次并实际发布、恢复保护、flush、读回。返回 `UnmodifiedUnsupported / NativeReady / WriteUnknown`；不接受普通 SDK 提供任意目标地址。 |
| `BootstrapShim` | 作为两个不可变 config 的永久 native callback；维护每次父调用的 TLS 快照、mid 归属与故障统计。未 attach 时无 CLR 调用、对象读取或托管分配。 |
| `BootstrapAttachCallbacksV1` | post-init 私有接入：先核实际 MethodInfo/目标语义与 bootstrap 身份，再原子发布一份完整、不可变的 Enter/Exit/Mid/User 回调描述。只允许一次 attach；整份表发布，不能分别写三个函数指针。 |
| Runtime adopt-existing | 为 broker 增加受限的“采用已安装父入口”路径，绑定 bootstrap 实际 slot/entry/original/mid/映像。它不重新 claim slot、不重新 Configure、不再次 Dobby patch。已有 late-install 构造路径不能直接代用。 |

native 状态快照至少包含 version/size、固定 profile ID、模块 base/size/身份、init OS thread/epoch、父 slot/entry/trampoline、mid returnPC/relay、准备/写尝试/读回/flush/结果及 sticky unknown。它是内部现场记录，不能让调用者写一个 success 布尔值就制造生产权限。native bootstrap、helper、trampoline、relay、状态和已发布的 callback descriptor 全部保留到进程结束；逻辑 detach 不卸载它们。

应把父入口和 mid 都在 `BeforeInit` 内一次完成，避免第一版又需要 post-init 指令写事务。现有 [固定 mid 证据](../foundation-event-mid-20261024/mid-site-evidence.json)为 Invoke `0x15642E0`，NOP site `0x156441B`，硬件 returnPC `0x1564420`。仍须核实际 Dobby 迁移范围不触及 mid。先准备全部资源、配置 helper、建立并核 relay，再进行已授权的固定 site/parent 发布；原 init 只能在现场完整且已知时进入。这条路线复用 pinned Dobby 的 Prepare→Configure→Commit 次序，不把它自身当成并发指令发布屏障。

`BeforeInit` 不枚举 IL2CPP 类型、不分配 Unity 对象、不启动 CoreCLR；它依据固定映像身份和机器码工作。post-init 语义解析必须仍与同一实际 entry/ABI 相符，失败则保持 native 直通并报告 capability unavailable，不二次改码。当前 Loader 后台等模块/首次 tick 后才安装的路径不能创建此启动凭据，应绕开该 fixed target 的旧安装分支。

## 不可变 VoidConfig 可以复用，关键是每次调用快照

[VoidContext.h](../../../native/RizToolbox.JudgeContext/VoidContext.h) 已支持 `original + enter + exit + user` 一次配置。使用真实 Dobby trampoline 作为 original，Enter/Exit 永远指向 BootstrapShim，即可保持既有 V1 ABI 和 original-once/SEH/C++ finally；mid config 同样永久指向 native MidShim。CLR attach 改的是独立 descriptor 的可见性，不能改上述 config。

每次物理父调用执行以下协议：

1. ShimEnter **先压 native TLS record，即使当前 descriptor 为 null**；record 包含本次不可复用 identity、父 slot/frame/OS thread、acquire 取得的一次 descriptor 快照、managed token 与状态。native transport 返回给 ShimExit 的 uintptr token 指向/标识该 record，不能把 managed token 本身当 native record。
2. 若快照为 null，全程直通；若非 null，只调用该份 Enter 并保存它返回的 managed token。全体新回调仅对 acquire 读到新 descriptor 的新 invocation 生效；发布前已经 Enter 的旧调用即使随后走到 mid，也仍使用 null 快照。
3. MidShim 只读取当前线程**最内层匹配父 record**，并核固定 returnPC、slot/physical identity、self 及已验证的方法策略。只用该 record 的 descriptor；不得重新读 global、搜索更老的父上下文或仅凭 self 相同继承 token。被抑制/Enter fault/foreign/reentry 不匹配时跳过观察并记录原因，原游戏继续。
4. ShimExit 只调用 record 中同一份 Exit，传同一 managed token 和 Returned/Abnormal/EnterFaulted，再在 native finally 中精确弹出。Enter、Mid 或 Exit 的 observer fault 各自 contain/置 sticky unknown，不吞 original 异常。Shim 内部应保住 record，不能等外层 VoidContext 吞了 Enter fault 后才发现 TLS 无法配对。

TLS 深度有明确上限；容量不足时必须有顶层 suppression/overflow 配对，禁止退回下层 record 造成跨调用归属。正常调用、同步递归、foreign worker、attach 发生在 original 中途、异常退出都应独立验收。descriptor/callback/user 的根必须在 release-store 发布前建立，发布后永久保留；逻辑停用只关闭订阅，不释放在途快照。Shim 的 fault/overflow 必须合并到 Runtime activity Known，不能因为旧 helper enterFaults 仍为零而报告安全。

旧 InFlight 覆盖 parked 和完整 native parent；Active 覆盖已准入 Enter→original→Exit。mid stats 只覆盖观察 helper，不覆盖最后 ASM restore/RET，仍不能单独作为退休证明。本方案只完成首次安装和 postattach，现有 admission gate 保留给以后确有需要的局部发布。

## 为什么不先做另两条路线

| 路线 | 可依赖的事实 | 本版取舍 |
|---|---|---|
| `DEBUG_ONLY_THIS_PROCESS` | Windows 官方保证 pending debug event 时所有目标线程停止，直到 ContinueDebugEvent。 | 真正可用的严格备选。但不能暂停后再让同进程 DobbyPrepare/LoadLibrary 自行运行；需外部可回放发布计划或受控 native 预准备握手，还要管理异常转发/调试器退出/Steam 启动。代价大于固定 pre-init，不作为首版前置。 |
| `CREATE_SUSPENDED` | 官方只保证新进程的 primary thread 尚未运行。 | 它不能单独证明 GameAssembly 已映射、bootstrap 可安全运行或 Resume 后所有线程受控；可作将来 launcher 的部件，不能直接发首次写许可。 |
| 当前 post-init managed 同步安装 | 原 init 已执行。 | 减少延迟不改变阶段，不选。 |

上述 Windows 事实分别见 [debugger 主循环](https://learn.microsoft.com/en-us/windows/win32/debug/writing-the-debugger-s-main-loop)、[debugging events](https://learn.microsoft.com/en-us/windows/win32/debug/debugging-events)及 [process creation flags](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags)。LOAD_DLL 通知不能单独当作可随意执行 LoadLibrary/Dobby 的许可。native wrapper 的实际安装应位于正常 init 调用处，不能搬到 DllMain；微软 [DLL 最佳实践](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-best-practices)要求 loader lock 下避免复杂初始化、跨线程同步及托管代码。

## 最小验收与明确拒绝

下一批交付按四个闭合工作项推进：固定官方 wrapper 接缝及 ABI；真正 native parent+mid 安装/直通；per-call snapshot shim+一次 attach；Runtime adopt-existing 与联合 fixture。fixture 使用真实 native 原 init（int、参数、SEH/C++ payload）、真实 Dobby 与真实 mid CALL，不只返回证明对象。验证 init 内部的合法调用、init worker、attach 中途旧调用仍不接新 mid/Exit、attach 后新调用三回调配对、递归隔离、observer fault、GC 后回调根、所有 original 次数、错误映像/既有 patch/错误阶段零写。最后再做单次固定构建真实冷启动，验 native 安装完成→original init→CLR attach→首个正常 UnityEvent 的实际次序和透明游戏行为；这一步尚未执行。

写入前必须拒绝：指纹/实际模块/原字节/unwind/固定 ABI 不符；bootstrap 不是本次首次 init wrapper；已知先行初始化或别的 loader 已拥有目标；目标/relay 超界或不可验证；同代状态已 claimed/unknown；无法核 post-init adopted target。未写入拒绝允许原游戏无该能力继续。写尝试后保护、flush、读回或 owner 不明则为 `WriteUnknown`：不能进入 original init 或宣称“关闭 mod 后继续安全”，不能盲回滚、重试或释放根；应中止受支持启动并留下诊断。纯 CLR attach 失败而已完成的 native 现场仍完整已知时，可以保持原生直通。

固定 profile 若发现**合法** pre-init UnityEvent 路径，则该 profile 的 pre-init 产品前提失败，应转用 OS debug-event 发布路线或新增真实 native 启动握手，不能用日志暂时没调用、线程枚举、全局 Suspend 或 5/8 字节数据原子写代替。不存在这类已知路径时，应按上述有界合同推进实现和冷启动验收，不继续为任意恶意同进程代码扩大证明范围。
