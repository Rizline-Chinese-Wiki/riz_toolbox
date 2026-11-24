# 下一批：观察 stock UnityEvent 的内联准备与实际执行

本计划只推进当前固定游戏版本的执行证据，不把 listener 图快照或独立 `PrepareInvoke` 当成完整消费者协议。依据是本目录的 [准备路径证据](listener-prepare-evidence.json)、[有界指令证据](terminal-evidence.json) 与 [后续协议](next-integration.md)；三者均没有游戏运行验证。

## 1. 在真实内联准备之后取得执行集合

`UnityEvent.Invoke@0x15642E0` 没有调用两个 `PrepareInvoke`。dirty 分支在 `0x156435A` 调用 `PersistentCallGroup.Initialize@0x1550EE0`，返回 `0x156435F`；后续直接 Clear / AddRange / 清除 needsUpdate。`0x156440A` 从实际 calls 对象的 `+0x20` 加载 executing 到 RBP，空引用检查后从 `0x1564417` 进入遍历。因此推荐在 **`0x1564417` 安装固定版本的 native 中段观察点**，记录 RSI（本次 UnityEvent）、RBP（本次实际 executing），再同步在主线程独立持有该列表。空集合也会经过这里。不能改为主动调用 Prepare，不能从入口旧缓存或出口新缓存推断这次遍历了哪个列表。

先增加独立的 native `void(self, MethodInfo*)` 父调用 transport，以实际 native 入口 / 正常返回 / 异常展开确定 Invoke 代次及 in-flight；让当前 ledger 复用这一个物理所有者。当前 `ExactHookBroker` 的 `InstanceVoid` 保留观察与标量 probe 没有中段寄存器帧；`NativeCoroutineKind.Factory` 是引用返回，不能借用它表示 void。新增中段 transport 必须有 Prepare / Configure / Commit、固定字节校验、寄存器和标志恢复、Windows x64 栈对齐及可展开的 unwind 信息。原位重放被覆盖指令，原游戏路径只继续一次；不能把现有函数入口 detour 直接套在中段。

若中段观察点的原指令、`.pdata` / unwind 或发布状态不能确认，就保持 `InlineExecutionUnobserved=true`，先完成 transport；不能放宽校验来宣称已观察准备过程。

## 2. 在三条真实执行调用处观察原调用

现有指令证据给出以下范围；所有地址是该固定 GameAssembly 的 RVA。

| 路径 | 真实调用点 → 返回点 | 调用当时的值 |
| --- | --- | --- |
| 第一个直接分支 | `0x1564470 → 0x1564475`，目标 `0x154E5B0` | RCX 是实际 invokable；此前 `xor edx,edx` |
| 第二个直接分支 | `0x15644C2 → 0x15644C7`，目标同为 `0x154E5B0` | RCX 是实际 invokable；此前同样清零 EDX |
| 虚分支 | `0x156451F → 0x1564525`，`call [rax+0x178]` | RCX=RDI 是实际 invokable，RDX=`event+0x28` 的参数数组，R8=`klass+0x180` 的 MethodInfo；目标来自该实际 klass 的 `+0x178` |

三个分支读取列表项的 getter 候选均是 `0x9A40B0`，对应返回点为 `0x156443A / 0x156448C / 0x15644E1`。现有证据没有给它可信的官方元数据签名，故此计划不需要给它增加猜测 ABI 的函数 hook。

推荐为上述 **三个调用点增加 native call-site relay**，而不在共享目标 `0x154E5B0` 上安装普通 `InstanceVoid`。每条 relay 在调用前捕获实际父代次、RBP 列表、EBX 索引、RCX receiver、原始参数寄存器和真实调用目标；在原调用正常返回或展开后成对记录退出。调用点的原返回地址作为来源证据，由实际 relay 入口取得，不能由 TLS 推测或补写。保留原调用指令的长度；六字节虚调用可使用范围内的六字节间接 call relay，不能替换为五字节 E8 加 NOP 后仍把入口取得的返回地址宣称为 `0x1564525`。虚分支的目标 / MethodInfo 必须在进入托管观察前保存，不能在回调结束后重新查槽而调用另一个目标。

`Invoke()` 与 `Invoke(Object[])` 共用 `0x154E5B0`，其参数和隐藏 MethodInfo 所在位置不同。两个直接调用点将 RDX 清零，并不足以独立判定全部共享别名的签名。relay 应原样转发该调用点的原始寄存器 / 栈，而不是把目标 cast 为一个统一 C# delegate；也不能把 R8 中残留的类型信息解释成零参方法的 MethodInfo。虚分支另按实际官方元数据核实 `void(Object[])` 槽签名。无法识别的 invokable 继续运行原调用并记录 opaque，保留责任。

该路径只证明“本次 Invoke 实际调用了这个 invokable”。具体 delegate leaf 的目标、虚分派、泛型及其向协程、Tween、Task、manager 队列的转交，仍由后续实际 owner 协议处理。

## 3. 先验收保真与覆盖，再接消费者结束协议

先用 native fixture 覆盖空集合、dirty 内联重建、三条执行分支、同列表多项及重复 receiver、监听器内增删 / 替换 executing、父 Invoke 重入和 foreign 子调用、工作线程、原函数抛出、观察回调失败、容量耗尽与发布中断。检查每个原调用恰好一次、参数 / 返回行为保真、异常继续原样展开、物理 in-flight 成对、来源不是继承父 TLS、未知不释放 root。发布后的 patch / relay / callback 为进程所有，未知安装结果不能重复安装。

只有准备观察点、三个实际执行调用点、父 native 退出及对应来源全部通过，才允许将**本次**内联执行观察标记为已覆盖。列表对象 / 版本前后相等不能排除中途增删后恢复，故 `MutationClosed` 与完整 `DispatchPathVerified` 不能仅靠此标记变为 true。后续仍要记录实际 mutation、封口 future sources、取得每个 listener 转交工作的结束或取消证据，再组合 `VerifyConsumersEnded → Close → TryRetire`。当前源码回归通过、静态地址匹配或孤立 Prepare 返回都不能代替这项验收。

本批完成前保持现有 Pending 与资源责任；不清空游戏回调，不按时间、GO dead 或某个协程自然 false 宣称整局结束。游戏实测继续按用户“实测稍后”的安排单独进行。
