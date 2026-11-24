# 下一步：从预初始化通道到引擎完成与整局退休

2026-10-24。本批生产源码已把新增 18 个操作入口移到首次 `il2cpp_init` 前发布，CLR 改为核对 V3 后只附加观察者。[完整构建与分层证据](README.md)已通过 25 套、1579 项检查，546 项源码及 22 项暂存包冻结未安装。以下是下一阶段工作，不是已验收清单；整体 B01–B19 仍 active，仅 B03 整项和 B18a 子项保持勾选。

## 1. 验收实际启动与对象接线

按用户安排恢复游戏实测后，核对同一次真实冷启动中的 Doorstop 首次初始化顺序、27 写实际凭据、18 个真实 metadata 声明和 CLR 一次 Attach。隔离映像已验证生产只读采用全部 18 个通道、无新增指令写入和 engine install；仍必须在实际启动中确认，不能用自有 Dobby 目标或隔离映像的通过代替这次启动。

映像发布、自有原生回调、CLR 共用 broker 三层已有独立证据；后续需把实际 receiver、Enumerator/Coroutine、owner/代次、tracker provider 和 IL2CPP 根逐项接入同一运行链。受控 metadata、graph/root/provider 不能继续作为生产对象的替身。部分解析或 Attach 失败、post-VerifyReady 失败，以及任意第 N 写失败应分别注入：保留已发布通道、实际写计数和在途责任，禁止重装、假回滚或继续受支持的原初始化。本批尚无任意部分写入失败动态证明。

## 2. 优先实现实际引擎节点登记、执行和最后清理

[静态研究与完整机器码证据](cancellation-research.md)已给出可直接实施的固定入口；不再只增加 Stop 返回观察。第一步是建立带本地代次的 native node registry，将实际 tracker 的 owner/receiver/Enumerator/Coroutine 与 native MonoBehaviour、node 关联。节点地址会复用，不能单独作为永久身份。

| 下一入口（固定 UnityPlayer RVA） | 要保存的事实 | 必须守住的边界 |
| --- | --- | --- |
| IEnumerator Start `792240` 与执行窗口 `78F840` | 实际输入/输出 node、owner、Enumerator，以及每次真实执行的开始和结束 | Start 返回前已有同步首次执行；不能等 managed Coroutine 返回才开始建账。需相应 typed native ABI，不能套 void operation 或追加不存在的 MethodInfo |
| 节点撤销 `793840`，配合同一节点的调度登记/删除路径 | Stop 原因、确切节点代次、owner 摘链和 scheduler 责任 | 摘链、`node+58` 清零或 scheduler 项删除都不等于正在执行的调用已退出；`node+60` 包含多种引用，不是执行次数 |
| 最后引用清理 `78F680`，并覆盖 Start 内联清理 | 原始返回字节、最后清理分支和已保存身份 | 原函数可能释放 node；After 只用此前复制的标量和实际结果，不能再次读取可能已 free 的 node。单挂此函数还漏 Start 的内联最终清理 |

先在自有 shape fixture 验证这些真实签名的参数、返回、输出槽和异常保持，再制定固定引擎入口的安全首发合同；当前没有发布 UnityPlayer hook。运行时还需核实际 icall 解析地址、模块及固定代码身份，不能仅凭 dump 名称或静态注册表授予运行权。

逐记录的取消完成候选必须同时具有：已关联的实际 Stop 原因与节点代次、调度撤销、全部实际执行排空、最后清理正常完成，且无 Unknown、漏配对、外来责任或新登记/代次替换。`StopAll` 保留 entry 集合及之后的变化核对；请求期间的新 Start 不继承旧快照。string 形式没有可信实例关联前继续 MatchUnproven。只有取得上述动态证据后，才设计 `EngineCancelled` 和 Pending 的转换；当前没有解除它们的授权。

## 3. 联合 listener 与资源退休，完成 Retry/Return/Loader

继续补齐每个 delegate leaf 的真实执行、全部 mutation 写者、序列化与 generic/shared/直接字段写、provider/Loader 转交及未来 Start 的封口。观察了 pre/post invokable 或最终列表相等，不表示所有 leaf 已执行，也不表示将来不会再次生产。

最后在同一 run 中联合引擎取消完成、listener/provider 责任、native 在途和消费者借用，验证 Retry、Return、再次进入、菜单窗口重建与 Loader 交接。顺序是停止新生产、排空实际在途和借用、确认旧对象不再可达，再提交退休和逐根释放；覆盖正常、异常、取消失败和部分释放时新请求到达。单个协程结束、本地 Outstanding=0 或若干帧静默不能代替整局资源结束凭据。完成这条组合链之前保持 Pending 和 `RequestPlay` 关闭，不据回归数量扩大验收勾选。
