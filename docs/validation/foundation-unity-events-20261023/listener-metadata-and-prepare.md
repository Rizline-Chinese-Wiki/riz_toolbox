# Listener metadata 与 Prepare 路线边界

2026-10-23编写，2026-10-24统一构建完成。本批新增两个 internal Runtime API、14 组独立回归，已由主代理注册并在Runtime153/153及完整25套中通过，见[最终批次索引](README.md)。没有UI、安装或游戏调用；这里不改变历史批证据。

`InspectRootedDelegateMethod(RootHandle actualDelegate, IntPtr exactDelegateClass, IntPtr module)` 返回 `NativeRootedDelegateMethod(Target, ActualClass, Method, Multicast, Virtual, MethodPointer, InvokeImplementation, MethodCode)`。它只读取调用者已持有的实际 delegate root；先核 root/header/class/Delegate assignability 和官方具名字段类型/offset/无 shadow，再核实际 `method` 字段的固定 MethodInfo2022 可读布局、有限名称、class/return/parameter 存储。只有 official declaring-class 方法枚举中的 exact token 才进入完整导出 metadata 对照，枚举上限 4096。末尾重读 root identity 和 delegate 字段，拒绝中途变化。

字段读取使用 `ReadField<T>` 的同步本地 value buffer；不新建/释放 IL2CPP GC handle，不调用 listener、不反射制造 delegate。调用方始终持有原 root；DTO 的 Target 是观察到的指针，长期采用时 backend 先同步建立自己的独立 Target 根，不能在放开 delegate/root 后把裸 DTO 当生命周期 owner。多播父 `delegates != null` 被拒为 opaque，由 backend 有界展开后逐叶投影。static/open、virtual、generic/inflated 和不能建立 executable Entry 的方法只保留只读诊断；此 DTO 从不授予调用或 consumers-ended 权限，也不猜 `RuntimeMethodInfo.mhandle`。

`ResolveObservedReferenceReturn0(IntPtr module, IntPtr declaringClass, string name, IntPtr returnClass, int rva)` 仅接受 `UnityEventBase.PrepareInvoke@1563A60` 和 `InvokableCallList.PrepareInvoke@154E1D0`。returnClass 必须与官方 `m_ExecutingCalls : List<BaseInvokableCall> +20` 的闭 class 完全相同；方法必须 exact declared/nonstatic/nonabstract/nongeneric/noargs/reference8，并核固定 executable RVA。返回既有 `CoroutineFactoryReference0` 的 typed native ref0 observer target；ordinary resolver 仍拒绝该 ABI。这个函数只解析目标，不调用 Prepare、不表示当前 Invoke 会经过该入口。

## 三函数静态结果

新目录中的 `check-listener-prepare.py`、`listener-prepare-disassembly.txt`、`listener-prepare-evidence.json` 是旧批同名文件的逐字复制；`listener-prepare-copy.json` 保存复制 SHA。没有修改历史内容。原检查 Python JSON 解析 script.json 的 ScriptMethod，PE 映射/反汇编仅下表三段，合计 264 指令；固定 GameAssembly SHA `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。

| 函数 | 范围 | 指令数 | 相关实际操作 |
|---|---|---:|---|
| UnityEvent.Invoke | `[15642E0,1564550)` | 150 | `156435A call PersistentCallGroup.Initialize@1550EE0`，返回 `156435F`；之后内联 executing-list clear/AddRange/update，`156440A` 读取执行列表并遍历 |
| UnityEventBase.PrepareInvoke | `[1563A60,1563B40)` | 62 | 本身也内联 list Prepare；不调用 `154E1D0` |
| InvokableCallList.PrepareInvoke | `[154E1D0,154E280)` | 52 | 独立方法体存在，但上面两段没有对它的直接 call |

实际 `UnityEvent.Invoke` 没有对这两个 PrepareInvoke 的直接 call；准备逻辑已内联。不能提供不存在的“Invoke→Prepare 返回 caller”常量，不能用两个 ref0 hook 代替实际执行集合捕获。listener 中或其它入口可以自行调用独立 Prepare，但那不是本次 Invoke 的内部准备回边。`156435F` 只属于条件 persistent Initialize 返回，既不覆盖 dirty=false 分支，也不代表随后 executing list 已准备完毕。

后续仍需按真实已 root event、物理 ALL-receiver invocation 栈和当前明确支持的 listener 类型观察；没有真实 Prepare 命中或 dirty/update 变化无法解释时，保留 pending/unknown，不能主动补调用或签 whole-run completion。

## 14 组回归范围

真实 unmanaged buffers + synthetic exported metadata 支持成功投影、caller root 保持、multicast 拒绝、字段 type/layout/static/shadow、坏 metadata 指针早拒、official membership/4096 上限、layout/export flags/signature 对照、opaque static/open/virtual/generic、非 executable Entry、读中变化、两个受限 ref0 目标、错误目标/return/ABI、worker 提前拒绝。所有组断言没有 native runtime Invoke；projection 不增加或释放 caller GC roots。

测试 fixture 的 SEC_IMAGE 仅映射人工 PE 供 VirtualQuery executable-range 校验，不执行代码或 DllMain。对相同 header 做了 Python/Win32 image-mapping 检查，确认两个 pinned RVA 均属于该映射的 executable MEM_IMAGE 区间；14组C#检查另在统一构建中运行通过。最初人工 PE 第一节 RVA 有大空洞被 image mapper 拒绝，修为 SectionAlignment 处开始的零填充虚拟尾段。该人工映射不是游戏metadata或listener执行验收。
