# 控制器在激活前登记的候选路径

2026-10-22。设计记录，尚未实现/实机验收；不修改原43锚点报告或其历史broker源码快照。

此前单参Instantiate在返回前可能执行Awake，无法先取得新controller身份。候选改成三参非泛型 `UnityEngine.Object.Instantiate(Object,Transform,bool)`：先创建宿主空GameObject并SetActive(false)，以它的Transform作parent实例化组件prefab，取得exact controller及其GameObject roots后登记PendingCommit，再激活宿主parent。整个过程不修改原prefab和原版场景对象。

[Unity 2022.3 官方Instantiate说明](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Object.Instantiate.html)说明组件克隆会连同其GameObject/子树克隆，Awake/OnEnable仅对当时activeInHierarchy的对象执行。官方行为给出本方案依据；本游戏实际回调顺序仍需验证，不能把文档当作已测证据。

本地dump.cs第112915行声明三参非泛型入口，RVA `0x153FAA0`。运行时必须核实精确static方法、三个参数类/一字节Boolean、返回Object、MethodInfo及固定样本entry，不对共享泛型thunk挂钩。使用前还要验证parent确实inactive、clone的exact类/parent及activeInHierarchy，并通过已验证Awake观察点计数，证明克隆/参数注入阶段没有提前回调或六全局委托改动。

顺序约束：完整本地资源包与所有结果/重试保护门就绪 → baseline全局委托root快照 → 注册PendingCommit → 创建inactive parent → 三参clone并核身份/未激活状态 → 绑定controller身份和root → 精确SetInsideLevelParams → 激活parent → 验Awake只发生一次、六委托分别指向本controller → 提交会话。Awake可能移动controller到持久场景，不能用parent的存活替代controller/回调责任；记录移动后真实挂载点与独立root。

失败时保留PendingCommit身份和隔离责任，停止后续场景/播放动作；只销毁自建parent/clone并等待实际Unity失活，逐字段条件恢复本会话仍拥有的全局值。未验证的旧delegate活性、未知Release/Destroy/free都保留责任，不能无条件还原旧快照或清空后继controller。消费者结束验证器须证明没有旧调用/全局引用后，才撤旧身份分类并释放根。

需要的实机证据：inactive parent/clone状态、clone前后Awake和globals零变化、激活时Awake一次与六委托归属、准备失败不影响原controller、激活后失败回滚、provider撤回和普通→LOCAL→普通交错。前置门的Cdecl测试、参数工厂探针或单独globals单测不能代替这些记录。
