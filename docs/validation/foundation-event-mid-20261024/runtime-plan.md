# UnityEvent 内联列表观察的 Runtime 接线方案

2026-10-24。此文是待 ABI 确认的实施设计；没有修改源码、测试或 native，也没有构建或游戏验收。基线是上一批 1475 项冻结结果。

## 要取得的事实

固定构建的 `UnityEvent.Invoke@0x15642E0` 内联准备列表。在 `0x156440A` 将本次实际 executing 读到 RBP，空引用检查后经过 `0x1564417` 的 9 字节指令段；空列表也经过该点。该段包括 `xor ebx,ebx`、`xor eax,eax` 与 NOP，随后 `0x1564420` 才读列表大小并遍历。

证据来源是上一批 [原始指令](../foundation-unity-events-20261023/listener-prepare-disassembly.txt) 与 [后续集成说明](../foundation-native-void-20261024/next-integration.md)。`0x1564417` 是中段位置，不具有普通函数入口 ABI，也不能作为 Dobby 普通 entry 处理。

本批仅把“真正进入了某次 Invoke 的内联准备后位置，观察到该次实际 executing 列表”升级为事实。三个 listener 调用分支、动态 mutation、委托派生的异步任务及其取消和结束仍未关闭；`DispatchPathVerified` 必须保持 false。

## 最小接线面

| 层 | 改动 | 权限边界 |
|---|---|---|
| Native mid helper | 保存全部整数寄存器、标志、约定要求的 XMM 和原栈位置；捕获真实 patch return PC、RSI、RBP，观察一次，重放 9 字节并恢复后继续一次 | 回调没有写寄存器、跳转目的地或替换原路径的权限 |
| Runtime mid resolver/installer | 验固定 module、profile、映像哈希、官方 UnityEvent.Invoke 元数据、mid RVA/字节及 continuation；独占安装责任和原生活动计数 | 只支持这一个固定中段，不暴露任意地址 SDK 接口 |
| NativeVoid broker | 增加线程物理 top 与受限当前上下文关联 | 不从 receiver 搜索父调用，不用时间或仅调用深度代替 token |
| Adapter backend | 接受真实 mid 回调，先独立 root RBP 的实际 executing 对象，再进行列表/元数据检查 | 原始根和新增独立根不转移；故障后保留责任 |
| Listener ledger | 保存该 Invoke generation 的独立 inline snapshot 与 token/调用帧证据 | Pre、inline、Post、独立 Prepare 四种记录分离 |

## 当前物理调用的关联

现有 broker 的 `NativeVoidInvocations` 包含唯一 token、真实入口 `NativeVoidFrame`、调用线程和每个 sink 的 context，但字典本身不能回答“当前正在执行哪个物理调用”。按 Self 找一条记录会把 foreign child 或同对象重入借给父 context。

建议在现有记录上增加 `ParentPhysical`、`Entry` 与 `Stage`，并用 `[ThreadStatic]` 保存当前物理 top。记录必须在 `EnterProbes` 和 capture 之前压栈。每次 physical Enter 都占自己的位置，包括 worker、foreign Self、foreign MethodInfo、无逻辑订阅、无有效 context 和 malformed frame。原生 Exit 在 after、Dispose 和配对 probe Exit 后才弹栈。退出乱序、错线程、错 token 保持 unknown，不能通过寻找旧父记录自愈。

Stage 至少区分 `Capturing`、`OriginalWindow`、`Exiting`。仅 Enter 即将返回 native original 前设 `OriginalWindow`；Exit 回调的第一步设 `Exiting`。这样 capture、after、Dispose 或 probe 自身触发的伪中段不能获取原调用执行权限。中段回调再入也要有单独的观察 busy 标志，不能重用一次正在接收 inline 列表的 context。

4096 记录容量保持有界。若 Enter 在记录分配前 overflow，token 为 0，线程 `overflowDepth` 加一并遮住所有父 top，直到对应 Exit(0) 减一；此期间拒绝 mid 关联。必须处理嵌套 overflow 和记录后进入的 child，不能在任何父 top 仍可见时仅返回 token 0。observer fault 继续由受限标量回调通知，original 仍执行一次。损坏或溢出引发 sticky unknown，不能默认为完整覆盖。

最小内部访问推荐挂在既有 `Binding`，只允许绑定自己的 sink 查询其当前 context。概念签名为 `TryGetCurrentNativeVoidContext<T>(validatedMidFrame, out T context, out NativeVoidPhysicalIdentity identity)`；最终名称与参数待 ABI 确认。`NativeVoidPhysicalIdentity` 由 broker 私有构造，携带固定 entry、真实 token、线程、完整入口 frame 与 generation，调用方不能用任意整数制造许可。不新增 SDK 服务或公开 raw pointer getter。

查询必须同时满足：

1. 这个 Binding 的 sealed `NativeVoid0` entry、MethodInfo、declaring class 与 mid 所属入口完全相同，安装/原生活动/探针状态均 Known。
2. 当前线程 top 恰是该 entry 的此 token，字典仍持有同一个记录，调用线程匹配，`overflowDepth == 0`，Stage 是 `OriginalWindow`。
3. mid frame 来自已安装的固定 mid transport，frame 版本、大小、保留位、实际 return PC、continuation 与约定一致；不能只比对一个猜测常量。若最终 patch 是 5 字节 call 加 4 字节填充，真实 call return PC 是 mid+5，而 continuation 是 mid+9，两者必须分别保存和校验。
4. 保存的 RSI 等于该物理入口的 Self，入口 MethodInfo 为 null 或固定真实 token；mid 的 RBP 非零。worker 只记录 scalar fault，不能读取 Unity 元数据或 root 列表。
5. Binding 仍 active，并且该物理记录确实持有这个 sink 返回的同一 context，context 的 generation/receiver 与 ledger 的当前对象一致。foreign child 即使 Self 与父相同，也只能命中自己的 top/context，绝不能向下查找可用父 context。

在 mid callback 内持有这张内部许可，再立即核真实 native activity；检查失败前不读取对象头、字段或声明类型。中段 native inFlight 与 Invoke native inFlight 是不同计数；直到各自原生退出，任意一方都不能以 managed token 出栈或上下文已 Dispose 推定 native 已空闲。

## Backend 和 ledger 的接收顺序

backend 完成纯 managed transport、thread、frame、token、context、scalar 身份检查后，第一项对象操作是为 RBP 的实际对象创建独立 GC root，并立即存入有界 root 责任表。不得先 `ObjectClass`、解引用列表、比较字段或调用 `PrepareInvoke`。该 raw publication 的 root 接收使用现有 `published:true` 风格；若 root 创建或后续确认异常，责任保留为 unknown。

随后依次检查 authority 与两个 transport Known、实际 root Target 仍等于 RBP、真实 closed `List<BaseInvokableCall>` class，进行有界 size/version/完整 item 次序读取和前后复核。empty list 同样保存实际列表身份和版本。记录必须包含 event、Invoke generation、唯一 native token、入口 caller/MethodInfo、mid actual return PC、executing identity、inline snapshot。若列表与入口 Pre 或出口 Post 不同，只保存差异；不能拿 Pre/Post 补成 inline 事实。

同一 generation 预期只经过该准备后中段一次；重复 mid、外来 receiver、context 已结束、列表 snapshot 期间重入或来源失效均保留未知责任。inline snapshot 保存后再检查通道和 authority，确认后才设置该记录的 `InlinePreparedObserved`。snapshot 的保存不等于监听调用已全部执行或所有相关资源可以退休。

owner snapshot 建议增加 `InlinePreparedInvocations` 与 per-event `InlinePreparedObserved`/`InlinePrepared`。全局 `InlineExecutionUnobserved` 若继续保留，应说明它表示完整执行链仍未观察；即便本批取得 inline list，也不能把“inline 准备事实”伪装成三个调用分支与未来任务完成。`Pending=true`、`MutationClosed=false`、`DispatchPathVerified=false` 均保留。

## 安装与故障保留

先验证固定 PE/profile/字节，再准备 near relay 与只读观察配置，最后发布一个固定 mid patch；slot、callback、relay、原字节、安装状态均由进程责任表持有。发布前后验证目标保护、实际模块、RVA、精确字节与回调配置。第一次不确定写入或保护恢复失败后，地址永久保留且 unknown，后续请求拒绝重新 patch 或释放 relay。

NativeVoid entry 与 mid patch 分别持有安装状态，任何一个失败都不能通过另一方成功冒充完整安装。新 mid transport 只能查询既有 sealed entry，不能制造另一个普通 InstanceVoid alias；游戏没有被安装或运行不等于新 backend 已验收。

## ABI 确认后的验证范围

受控 broker 测试覆盖 top/token 的 push/pop 时机、foreign child 的自身 mask、同对象重入、worker、entry/exit callback 重入、错 caller/RSI/token、detach、overflow 遮父和错序 Exit。backend 测试覆盖 root 先于检查、empty list、inline 与 Pre/Post replacement 区别、重复 mid、独立 Prepare 不充当 inline、共享通道中途 unknown 后不继续 native read。

真实 native fixture 必须验证全部约定寄存器/flags/栈恢复、9 字节重放、call return PC 与 continuation 各自准确、正常/SEH/C++路径继续一次、worker/reentry、观察 callback fault、near relay 距离、安装前后原字节、未知发布不可重试及原生 inFlight 覆盖完整回调窗口。Root 唯一构建；本设计阶段不启动游戏或写入游戏目录。
