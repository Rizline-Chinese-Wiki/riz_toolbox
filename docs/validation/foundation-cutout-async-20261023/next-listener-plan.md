# 下一轮 UnityEvent listener 接线计划

2026-10-23。535/535 定向检查后的准备性只读审查；完整构建由主代理负责。本次没有修改 src/tests/native、运行构建、安装、加载游戏或操作 UI。仅阅读下列 5 个文件，其中 dump 只取下述有限类段。没有新增反汇编或实机证据；`gameRuntimeVerified=false`。

## 已核输入与现有能力

1. `src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutEventSourceBackend.cs`：69 个固定根槽；每条 Trigger 的实际 UnityEvent 已在 `6 + 2 * index` 槽独立 rooted，最多 32 条。`Root` 在 ObjectClass 检查前记录根；`ReadItem` 在 publication 前记录 owned Int32 参数。
2. `src/RizToolbox.Runtime/NativeRuntime.cs`：`Class`、`ObjectClass`、具名 `Field`/`Method`、`Root`、`InvokeReferenceWithPublication`。`Method` 可按完整参数类型查找，并拒绝重载歧义；普通 `Read`/`Invoke` 不是有界临时责任的替代品。
3. `src/RizToolbox.Runtime/NativeRuntime.MetadataDiagnostics.cs`：`InspectMetadataClass`；`InspectMetadataFieldClass` 从真实字段取得已存在的闭引用类型；`InspectDeclaredMethod`/`InspectDeclaredStaticMethod` 核声明类、签名和 executable entry。完整 `InspectMetadataMethod` 目前 private；`InspectExistingReference` 专用于既有 `Release` 合约，不能泛化冒充 listener 方法解析器。
4. `src/RizToolbox.Runtime/NativeRuntime.GenericMetadata.cs`：`ReflectSingleReferenceGenericClass` 仅支持单个引用类型参数，且会分配反射对象、数组并使用普通临时根。此路既不支持 `CachedInvokableCall<int/float/bool>`，也不应作为新有限责任 owner 的默认初始化方式。
5. `C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs`：7680–7725、7933–7955、20823–20850、114015–114227。以下 offsets/RVA 是待 runtime metadata 与固定 image 校验的候选值；本次没有验证这些 listener 函数体。

## activation 前采用的真实对象图

所有类位于 `UnityEngine.CoreModule.dll / UnityEngine.Events`，除表中另注者。用具名 metadata 核字段类型/offset，不扫原始内存，不用 open generic dump 中的 `0x0` 当闭类型 offset。

| 对象 | 必须记录的具名字段 / 候选 offset | 用途 |
|---|---|---|
| UnityEventBase | `m_Calls +10`、`m_PersistentCalls +18`、`m_CallsDirty +20` | 实际 InvokableCallList、序列化 PersistentCallGroup、缓存脏状态 |
| PersistentCallGroup | `m_Calls +10 : List<PersistentCall>` | 序列化候选集合，尚不是本次执行集合 |
| PersistentCall | `m_Target +10`、`m_TargetAssemblyTypeName +18`、`m_MethodName +20`、`m_Mode +28`、`m_Arguments +30`、`m_CallState +38` | 实际 Target/class、方法名、模式与调用状态的原始值 |
| ArgumentCache | `m_ObjectArgument +10`、`m_ObjectArgumentAssemblyTypeName +18`、`m_IntArgument +20`、`m_FloatArgument +24`、`m_StringArgument +28`、`m_BoolArgument +30` | 对象与字符串独立根；int/bool/raw float bits 拷贝；禁止统一转字符串丢掉参数身份 |
| InvokableCallList | `m_PersistentCalls +10`、`m_RuntimeCalls +18`、`m_ExecutingCalls +20 : List<BaseInvokableCall>`；`m_NeedsUpdate +28` | 三个列表分别保存身份、顺序、size/version 与条目，不能互相替代 |
| InvokableCall | `Delegate +10 : UnityAction` | 无参实际 delegate；记录叶子 Target/class/MethodInfo，而非只记 InvokableCall 类名 |
| 已存在的闭 InvokableCall<T> / CachedInvokableCall<T> | 具名 `Delegate`；Cached 的 `m_Arg1` | 从实际 ObjectClass/字段 metadata 取闭类型；仅支持已明确核验的参数类型，未知闭类型保 opaque/pending |
| UnityEvent | `m_InvokeArray +28 : Object[]` | 记录实际存在的 fallback 参数数组；null 不自动分配，也不证明后续不会创建 |

`m_Mode` 与 `m_CallState` 在本 dump 中所有枚举常量均显示 `0x0`，不能据此解释枚举数值。先保存原始值；执行模式必须由后续固定 metadata/有限静态证据核定，再与实际编译出的 delegate/argument 对照。

最小接线是让 Source 提供一次性的同步具名根借用：校验同 launch/session、未 activation、完整 capture、!busy/unknown 后，逐个发布其已持有的 UnityEvent RootHandle。新 listener owner 先占 process 强 owner 和固定槽，再同步建立自己的独立根并立即存槽；借用的根不转移、不 Dispose，也不在 Invoke/Cutout 后重新从 controller 查事件字段。同一 UnityEvent 被多个 Trigger 引用时只建一个 event owner，另存各 Trigger 关联及调用顺序；同地址本身不能区分来源。

List 闭 class 直接来自上述真实字段的 `InspectMetadataFieldClass`。复用 Source 的 `_size/_version`、精确 `get_Item(Int32)` 与 publication 参数记录方式：读取 count/version，逐项 publication/root，末尾复查同列表身份、count/version 和顺序。版本相同不足以代替逐项比较；列表为 null、超界或闭类型不匹配时保留已获得责任，不截断为“空列表”。

建议首轮固定上限：32 个 distinct events、所有列表合计 128 个条目记录、合计 128 个 delegate 叶子、2048 个根槽，process owner 上限 32。每次 native/root 尝试前先占相应槽并记 attempted；超界只停止采用并保留责任。所有上限都表示这次支持范围，不表示实际图已完整。根槽覆盖 event/group/list/call/invokable/delegate/target/cache/对象参数/字符串/实际数组；原生 MethodInfo/Class 指针是 metadata 标识，不当作 IL2CPP 对象 Root。

## delegate 与 Method 的可做边界

`mscorlib.dll/System.Delegate` 的具名字段为 `method_ptr +10`、`invoke_impl +18`、`m_target +20`、`method +28`、`method_code +40`、`method_info +58`、`original_method_info +60`、`method_is_virtual +70`；`MulticastDelegate.delegates +78`。先核真实 delegate class，再读取并 root Target、实际 multicast 数组及每个叶子。数组非空必须完整有界展开，不能只采用顶层 Target/Method，也不能把空 Target 当成已结束。

任意 listener 的 `invoke_impl` 不保证等于 `method_ptr`；现有 reset Action 的 direct singlecast 比较只能用于它已核定的构造路径。虚方法、共享泛型代码、静态/open delegate、闭包或不同 ABI 都不能仅凭函数地址认领。`RuntimeMethodInfo.mhandle +10`、`Delegate.get_Method@AD3BE0` 是候选相关证据，当前没有从这些任意输入到已验证 native MethodInfo 的通用公开接口；不要凭字段名称把 mhandle 强转可调用指针。

第一版可以完整保存未知叶子的实际 class/Target/原始 method 标识并报告 pending。若要解释并观察其真实执行，需要一个最小 metadata-only 接口：从已 root 的真实 delegate、经可读性/具名字段校验的 `method`，复用现有 private `InspectMetadataMethod` 返回完整签名/flags/declaring class/entry；不调用 listener，不制造 delegate。已知方法则可直接用现有 `InspectDeclaredMethod` 按完整签名对照。虚分派、泛型和无法确定的实际 receiver 仍需独立解析证据。

## 实际 Invoke 前后怎样连续观察

1. activation 前完成上述对象图和 hook 安装。`UnityEvent.Invoke()@15642E0` 是本次无参事件入口候选；按实际 rooted UnityEvent receiver 分类，保存每次物理 invocation 代次。worker、MethodInfo 不符、同 event 重入、未知 cleanup 只做标量 fault；主线程读取前后都核 fault/真实 native 在途。外来 event 的物理 child 必须遮蔽父上下文，不能继承父 event 身份。
2. 在 Invoke before 保存三个列表及 dirty/update 状态，但不要主动调用 `PrepareInvoke`、`RebuildPersistentCallsIfNeeded`、`PersistentCall.GetRuntimeCall` 来“读出”执行列表：这些是可能分配或更新缓存的游戏操作。候选实际路径为 `UnityEventBase.PrepareInvoke@1563A60`、`InvokableCallList.PrepareInvoke@154E1D0`、`RebuildPersistentCallsIfNeeded@1563B40`、`PersistentCallGroup.Initialize@1550EE0`。先有限验证实际 Invoke→Prepare 的物理调用关系，再以真正返回的 List 建根/快照；仅靠 Invoke 入口与出口快照不能证明中途执行了哪个列表。
3. persistent 候选的 target+方法名不是 resolved listener。缓存脏时必须等真实重建，采用实际 InvokableCall/delegate，并核其与相应 PersistentCall/ArgumentCache 的关系；Off、失效 Target 或未生成 call 只记录“不执行/尚未解析”，不记消费者自然结束。typed/enum 参数与泛型签名未核定时保 pending。
4. Invoke after 重新核列表身份、各 version/count/元素顺序、delegate 叶子及参数，读后再核 fault/drain。预期 lazy cache 重建也必须由本次真实 Prepare/rebuild 代次解释；不能静默重置 baseline。记录 AddListener/RemoveListener、AddCall/DirtyPersistentCalls、InvokableCallList Add/Remove/Clear/Prepare 的实际 mutation 序列，才能识别 Invoke 中增删后恢复或 executing 列表替换；只看前后相等做不到。此表未授权调用这些 mutation，更不清空共享 event。
5. 真正的 listener 执行需要针对已核 ABI 的具体 delegate/method 观察及配对退出。未知自定义 BaseInvokableCall 子类或 `Invoke(Object[])` fallback、虚方法/泛型/多参数 ABI 没有被现有无参 void hook 自动覆盖。若所需 transport 不能让原始 native 异常保持原语义，先保留未知，不把 managed callback 正常返回当作 native unwind 证明。

现有 native Factory reference0 transport 的物理签名可作为两个 PrepareInvoke 返回观察的候选：以实际 receiver、固定返回 caller、精确 MethodInfo 和返回 List class 验证，不能套用“返回 IEnumerator”的专用 resolver。AddListener/AddCall 等带 reference 参数的 void、RemoveListener 的双 reference void 则需要先核对相应 hook ABI；本次 5 个文件没有展示这些观察入口。`InvokeVoidWithPublication` 可以安全组织一次已授权调用，不等于可以无损观察任意相同参数方法。缺对应 transport 时，第一版只交付真实 Invoke/Prepare 快照与 mutation 未闭合状态，不能宣称连续 mutation ledger 已完成。

## 能给出的证据与仍不能关闭的责任

该 owner 可报告完整有限对象图、实际执行列表代次、已观察 listener 的配对调用及 mutation/fault。它不能因 Invoke 返回、Target Unity-dead、delegate 从列表移除、reset Action 清 bool 或两个 Cutout enum false 就签发整局 consumers-ended。

listener 可将 Target/参数交给其它 coroutine、SimpleDelay、Tween、Task、UnityEvent、静态集合或外部容器；实际闭包与 multicast 叶子也可再转交。未有对应真实操作 owner、取消/自然结束与未来 producer 关闭证据时一律 unsealed/pending。共用 UnityEvent 从其它实际 caller 调用也要观察，不能只凭当前 Cutout TLS 归属。观察根仅在真实采用关系撤回、完整 native redrain、逐根一次释放全部满足后释放；未知 free 不重试、不退 owner 配额。保持现有 Source 的 Pending 与保护，直到独立的全消费者协议真正闭合。

下一轮最小交付：同步借用 event 根；有限 listener 图 snapshot + 真实 Invoke/Prepare/mutation ledger；未知类型保持 pending。随后根据实际捕获到的少量 listener class/method 决定具体 operation owners，先不扩展到全游戏调用图。
