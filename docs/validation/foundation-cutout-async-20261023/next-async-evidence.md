# 下一轮 EventTrigger 异步观察的四函数证据

2026-10-23。只读磁盘 PE、已有反汇编、dump.cs，以及由 Python JSON 解析的 script.json；未构建、加载游戏、操作 UI 或修改 src/tests/native。`gameRuntimeVerified=false`。此文不改变已冻结的 1393 项构建证据，也不签发消费者全部结束证明。既有导入/导出分诊沿用 `../local-launch-20261022/triage.json`，没有重做全库扫描。

结论：可以直接复用现有 typed `Start` / `MoveNext` transport。两类工作都进入公开 `StartCoroutine(IEnumerator)`；在其 before 回调中已有真实枚举器，可以先建立根、身份、初始字段记录，再允许原函数及其同步首个 MoveNext 执行。下一轮不需要为这条路径新增 ref1 Factory transport。现有 Factory transport 仅支持无参数实例工厂，不能冒充这里的 ABI。

## 输入与有限范围

| 输入 | SHA-256 |
|---|---|
| `D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll` | `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` |
| `C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/script.json` | `a51cc6ee5881110fbeb884679b13d20bce4ac4df9055bf535f3cdc476bf35d10` |
| 同目录 `dump.cs` | `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb` |

以 pefile 映射 RVA、capstone x64 解码，边界取 Python 解析的下一 ScriptMethod 地址。只新增解码以下四个范围，合计 220 条指令（含范围末尾 padding）。StartEvents 和 SimpleDelay 的调用点复用本目录 `terminal-disassembly.txt`，没有重新扩展为第五个函数。

| 函数 / ScriptMethod | 范围 | 指令数 | 范围 SHA-256 |
|---|---|---:|---|
| `EventTrigger$$StartEventsEnumerator` | `[38A3F0,38A460)` | 34 | `af33258f84785d74c1674620bb5b2ff5205d7930af514bbb8ed5a0155302da00` |
| `<StartEventsEnumerator>d__16$$MoveNext` | `[39B4D0,39B5A0)` | 61 | `9912c95fe348279dc7e73f3a4ecb081c902fb452704bf43bbc6a33ba04a4a006` |
| `EventTrigger$$<StartEvents>b__14_1` | `[38A700,38A710)` | 13 | `b904053c62926849e6cffc4eb7246680105082ac087f21564c410c77257f2ca0` |
| `<Delay>d__4$$MoveNext` | `[3AF0A0,3AF250)` | 112 | `98bc45ff61a7cb1d228fc10e59d02ac78bf39f288f940b9ea79c3660511a2d0d` |

## 枚举器与事件身份

dump.cs 93262–93272 给出 `EventTrigger.<StartEventsEnumerator>d__16`：`<>1__state: System.Int32 +10`、`<>2__current: System.Object +18`、`trigger: EventTrigger.Trigger +20`；MoveNext 为 `39B4D0`。同文件 93244–93248 的 Trigger 为 `events: UnityEngine.Events.UnityEvent +10`、`offset: System.Single +18`。枚举器没有 EventTrigger host 字段，host 必须来自实际 Start receiver，不能从枚举器臆造。

| RVA | 实际字节 | 含义 |
|---|---|---|
| `38A401` | `488bf9` | 工厂保存 RCX，即传入 Trigger |
| `38A436` | `c7431000000000` | 新枚举器 state=0 |
| `38A440` | `48897b20` | 新枚举器 trigger=该 Trigger |
| `38A449` | `488bc3` | 返回新枚举器 |
| `39B4FC` | `488b4720` | state=0 路径读取该 Trigger |
| `39B500` | `c74710ffffffff` | 先令 state=-1 |
| `39B521` | `f30f107018` | 读取 Trigger.offset |
| `39B537` | `e8d485ffff` | 创建 `WaitForSeconds`，构造入口 `393B10` |
| `39B540` | `48895f18` | current=实际 WaitForSeconds |
| `39B558` | `c7471001000000` | state=1，本次返回 true |
| `39B56A` | `488b4f20` | 恢复时重新读取同 Trigger |
| `39B56E` | `c74710ffffffff` | 恢复时 state=-1 |
| `39B57A` | `488b4910` | 读取该 Trigger.events |
| `39B585` | `e8568d1c01` | 同步调用 `UnityEvent.Invoke@15642E0` |
| `39B58A` | `32c0` | Invoke 正常返回后返回 false |

正常轨迹为 Start 前 state=0，首 Move 后 state=1/true，下一 Move 先 state=-1，Invoke 正常返回后 false。其它初始 state、外部重写字段、未配对 Move、Invoke 抛出或原生返回未知，均不能补造自然完成。current 在 terminal 路径没有清零，不能要求终态 current=null。

已有 StartEvents 调用点 `38A69A mov rcx,rax; 38A69D xor edx,edx; 38A69F call 38A3F0` 只传 Trigger 和空 MethodInfo；工厂也仅将 RCX 写入 trigger。物理形态是静态单引用入参工厂，无 EventTrigger receiver。dump.cs 93293 的文本不保留 static 修饰，后续实际接线仍需 NativeRuntime 检查声明类、static flag、返回与参数 metadata。不要使用现有 `CoroutineFactoryReference0`，也不要把 Trigger 错当 EventTrigger this。

## Reset Action 与共用 Delay

dump.cs 93295 将 reset 回调定位为 EventTrigger 的实例方法 `<StartEvents>b__14_1@38A700`。它完整的有效函数体为 `c6413000 c3`：将 receiver 的 `_refuseInvoke +30` 写 false 后返回。该 RVA 同时有 `System.Text.EncoderNLS.ClearMustFlush` 的共享代码别名；地址相同不能替代精确声明 metadata、Action Target 和 MethodInfo 验证。

已有 StartEvents 证据：`38A5E3` 从元数据槽加载 R8，`38A5ED mov rdx,rdi` 将实际 EventTrigger 作为 Action Target，`38A5F6 call Action..ctor@377890`，`38A5FE mov r8,rbx` 将 Action 交给 SimpleDelay，`38A601 movaps xmm1,xmm6` 为实际 Max offset，`38A604 mov rcx,rdi` 为同一个 EventTrigger host。`38A5E3` 的 RIP-relative 槽 RVA 为 `1E55790`，磁盘字节 `735c016000000000` 是编码元数据 usage，尚非运行时 MethodInfo 指针；此磁盘 slot 本身不授权裸指针调用。script.json 仅含 ScriptMethod 项，不能从中补造 runtime metadata slot 解析结果。

dump.cs 93742–93754 的 `SimpleCoroutine.<Delay>d__4` 字段：state `+10`、current `+18`、seconds `System.Single +20`、todo `System.Action +28`、deltaTimeProvider `System.Func<System.Single> +30`。既有 SimpleDelay 工厂 `3AE39E` 写 state=0、`3AE396` 写实际 seconds、`3AE3A5` 写 todo。既有初始化 tracker 已使用相同 Delay.MoveNext transport，下一轮应共享它的物理 patch、另按已发布枚举器身份分类，不能再装相互覆盖的 detour。

Delay.Move 的静态状态行为：

- state=0 时先写 -1。seconds<=0 可立即在同一次首 Move 中执行 todo 并返回 false；因此不能强制期待 Coroutine 非空或至少 yield 一帧。
- 正秒数且未设置 deltaTimeProvider 时创建 WaitForSeconds，写 state=2/true；恢复时 state=-1，清 provider，再执行 todo/false。
- 若实际全局 provider 存在，会走 state=1 的逐帧路径，seconds 会被递减；不能错误要求其整个生命周期保持初始 bit pattern。state=1 与 state=2 都是实际分支，不能把只允许 state=2 写为协议。
- `3AF101 488b4728` 取 todo；`3AF10E 488b5028`、`3AF112 488b4840`、`3AF116 ff5018` 从实际委托读取 method/method_code 并调用 invoke_impl。仅看到 Delay 返回 false不能替代确切 reset Action 的身份核验。

## 真正的 typed Start caller

| 操作 | 已有真实路径 | typed Start before 所见 caller RVA |
|---|---|---|
| 单个 Trigger 延时事件 | `38A6AD call StartCoroutine@153D380` | `38A6B2` |
| 拒绝重入标记的 reset Delay | `38A607 call SimpleDelay@3AE340`；后者还原栈后 `3AE3D0 jmp StartCoroutine@153D380` | **`38A60C`** |

SimpleDelay 的最后一步是 jmp 而不是 call，Start caller 不是 `3AE3D5`；原始 `38A60C` 返回地址保留。实际绝对地址都应使用已验证模块基址加 RVA。

下一轮的最小接线可以复用 `ResolveCoroutineStartHook(module)` 及其 `StartCoroutine_Auto` 验证别名、`BindNativeCoroutineObservation(Start)`，并为 `39B4D0` 的确切实例 bool-return-0 Move 增加逻辑 observer。Start before 先核 caller+actual host+已捕获 source 列表/版本，立即保留真实 enum；Trigger 支路核 exact Trigger/events/offset，Delay 支路核 exact单播Action class/Target/EventTrigger method/非virtual/no delegate array。启动返回后只采用真实 Coroutine 输出，允许同步自然结束时输出为空。

必须保留 Start 内同步 Move 的配对窗口、worker/reentry sticky fault、实际 native activity drain、固定容量和逐代身份；同 host 发生重复 StartEvents 不能自动归并旧工作。同一个 Trigger 也可能在多次分派中生成不同 enum，需要本次分派序列而非仅 Trigger 地址。

本次没有读取实际 Action、实际 Trigger/UnityEvent listener 列表或运行时 MethodInfo。四函数证据可确定直接异步来源与观察入口，**不能证明任意 UnityEvent listener 没有外置异步消费者**；也不能把 reset bool 已清零或两个枚举器返回 false当作 whole-run 消费者结束。
