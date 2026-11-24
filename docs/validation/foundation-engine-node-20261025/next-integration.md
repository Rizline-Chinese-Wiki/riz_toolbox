# 从节点观察走到完整生命周期

2026-10-25。当前四类 typed 通道、只读 CLR bridge、dispatcher 和节点标量账本已通过完整 25 套、1597/1597 离线回归；实际自有 Dobby 四入口原生验证为 3191 检查，真实 Dobby→CLR→实际账本联合验证为五进程 855 检查、44 次原调用和 28 对捕获/完成/清理责任。[本批说明](README.md)记录正式原件及 561 源/22 包冻结结果，新包未安装、未游戏实测。下一阶段需要把这些真实观察接入游戏的安全发布、现有 run 身份和调度资源责任；不能通过放宽 Pending 或将 Release 返回值直接改成取消完成来缩短范围。

## 先建立四个引擎入口的生产发布合同

现有 V3 只覆盖固定 GameAssembly 的 18 个监听变更/Stop 操作，不含 UnityPlayer 的节点入口。新增生产发布层需要同时核验 UnityPlayer、GameAssembly 和固定 Dobby 的身份，核验四个入口的完整函数边界、原字节、返回 ABI 和关键节点字段布局，再生成独立的 V4 或等价强度凭据。每行必须保留模块身份、RVA、kind、slot、target、prepared original、entry、epoch、发布前后字节和最终状态，不能由 CLR 自报 target 和 epoch 代替实际 producer 记录。

静态候选为 UnityPlayer 的 Start `+792240`、Execute `+78F840`、Release `+78F680`、Detach `+793840`，依据见[实际机器码研究](../foundation-operation-preinit-20261024/cancellation-research.md)。这些地址与字段仅适用于已核对的固定映像。生产 `RequireReady` 必须核验新凭据全部行并只执行 Attach；缺行、旧 V3、重复采用或身份改变时拒绝，不退回游戏运行中的普通首次安装。

安装时机必须单独证明。UnityPlayer 早于 GameAssembly 初始化存在，不能仅因旧入口在首次 `il2cpp_init` 前发布，就推断引擎节点函数此前也无人执行。先确定可在首个相关调用前完成统一准备和发布的实际启动点；若该点已经可能并发执行，需提供相应原生调用准入或等效可证明方案。全部 trampoline 必须先准备，再发布写入；任意写后失败保留已产生的原生责任和 Unknown。不能把假回滚、重新 Configure、重装或稍后 Attach 当作撤销已执行的机器码写入。

验收需要覆盖固定双映像的只读身份/字节检查、真实发布凭据、旧 V1–V3 的拒绝、重复进入、逐次写入失败、未知状态竞争及真实冷启动。自有 Dobby fixture 仍用于验证原函数和异常传输，不能替代游戏入口的第一次写入时序证据。

## 把节点代次连接到现有 run 和协程身份

节点账本目前只使用 native 已复制的标量，能在 Start 尚未返回时按 owner 和实际 IEnumerator cache 匹配同步 Execute；这一关联已经在本批真实 native→CLR→实际账本链中执行。但生产 coordinator 尚未创建和绑定这四类通道。下一步要在强发布凭据核验后建立该绑定，并把节点代次与真实 tracker 的 receiver、Enumerator、Coroutine、owner、slot、generation 关联，覆盖初始化、终止和 cutout 三类协程。必须先保留 Start 进入时的关联责任，再接受同步首执行；不能等 Coroutine wrapper 返回后才开始记录此前已经执行的节点。

跨层身份不能只比较节点地址、脚本 handle 或 managed wrapper。核对 receiver 对应的 native owner、真实 enum 引用和 tracker 代次，分别记录 wrapper 的节点引用及其建立/释放；保留节点地址被再次分配时的旧代次证据。native 已释放的 node 和其指针值不得由 CLR 再次读取。worker 路径继续只记录必要标量或发布 Unknown，不读取 provider、metadata、根或引擎对象。

验收要把真实 native 回调、实际 bridge/dispatcher、实际节点账本和三类 tracker 连成同一条运行证据，覆盖 Start 内同步 Execute/Release、节点复用、外来 owner、旧代次完成、异常和观察者错误。受控根/图测试可以验证协议，但必须与真实 IL2CPP 对象/GC 证明分开标注。

## 收齐 scheduler、内联清理和消费者责任

四个外层入口仍不足以证明全部未来执行来源已经关闭。沿固定研究中的 scheduler 登记、删除、cleanup callback、native nested coroutine 和 continuation/wait 记录建立独立责任。登记时记录实际 payload 与 node 代次，删除和 cleanup 分别记录；拒绝把“Stop 返回”“节点 owner 已清零”或“没有当前执行”视为不再产生新执行的证明。

Start 的同步首执行可能在普通 Release 观察范围之外走内联清理；账本的 `InlineCleanupUnproven` 必须由实际入口和标量事实闭合。不得因为 Start 输出 null、AL=0、引用计数为零，或 node 已释放就清除此不确定性。Release 的最后分支、摘链完成、真实 Execute 窗口退出、scheduler 未来来源关闭和所有消费者归还都需要自己的证据和顺序。

达到这些条件后，再设计有明确 owner/run/slot/generation 的取消完成凭据，连接现有取消请求账本和三类退休 gate。未知状态、在途调用、未配对 completion、旧代次或任一未来来源未封口时保留 Pending。消费者根、provider、future、listener 和媒体资源应由各自责任完成后退出；不能共用一个“看似 idle”的布尔值统一释放。

## 用完整 Retry、Return 和 Loader 收尾

最后的验收应以同一真实运行链覆盖冷启动、进入菜单、加载一局、暂停、重试、通关或中断、返回菜单及再次开局，核对每个 run 的来源关闭、取消请求、节点代次、执行、调度和消费者回收。结果页、场景/窗口重建、偏好恢复、旧回调晚到和第二局复用都属于完整范围。

在用户恢复实测前，可继续实现生产发布、故障注入、生产 coordinator/tracker 联合身份、scheduler 与内联清理闭合，并扩展已经通过的自有 native→CLR→实际账本测试和离线包核对。新包只有在所需证据通过并被明确记录后才进入游戏验证；当前 `EngineCancelled=false`、Pending 保留、`RequestPlay` 关闭，B01–B19 不新增验收勾选。
