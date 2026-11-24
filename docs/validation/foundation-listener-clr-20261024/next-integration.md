# 下一步补齐真实游戏链路

2026-10-24。本批已通过自有 native → CLR bridge/broker → 真实 ledger 的有限联合验证，见 [README.md](README.md)；游戏实测仍按用户安排稍后进行。接下来按“实际 receipt 和回调 → 全部生产者/消费者归属 → 可证明退休 → Retry/Return/Loader 端到端”推进，不把离线通过换算为全部 19 项完成。

## 1. 让生产采用失败也可核对

先为生产对象 root 的两阶段采用补隔离故障注入。实际 `CaptureDispatch` 第一阶段通过 `AdoptDispatchReference` 将 source 的 Invokable、List 和 site 2 的非空 RDX 引用逐一 Root 并存入 owner 槽；这时不读取 `root.Target`、Class 或列表。保存完实际引用后，第二阶段才执行 `RequireObservationKnown`，读取 `root.Target`、Class 和 List 并核对 row/index。需要验证首个 Target 读取或后续第二个 Class 检查故障时，三个引用的根已保存、故障后的 native 读取不再发生；Root 申请中途失败时，保留已取得的根与槽位，状态变 Unknown，不重试、不释放或假称采用完成。当前这一生产对象采用顺序经过静态审查，尚未完成上述故障注入。

V2 receipt 复核、桥准备、Attach 及其失败后的 delegate/native 通道责任，是另一条可补的独立动态验证边界；它不等同于这里的“先 Root、再读取对象”两阶段。

同时补原始异常的被动 first-chance 记录，与 native 外层 catch 比较实际 code、flags、参数及载荷。保留真实故障地址；虚表读取 AV 的地址属于 helper 实际读取位置。不要把上一批 OS flags 的观察套到本批，也不要在没有证据时把全部 flags/异常展开写成透明。

## 2. 同一次真实冷启动连接 receipt 与回调

游戏实测恢复后，先在支持的 Doorstop pre-init 合同中验收：固定模块身份、九次首写责任、三处 source 的 `+5/+5/+6` 续接、三个 `6/9/9` continuation、永久 RX 块和动态 RF 链均满足 V2；真实 original init 恰好一次；仅 Ready 且 init 非零返回才启 CLR。零返回、Disabled、Unknown 和重复 init 的行为需有原始日志，不能仅看游戏能打开。

随后在同一进程中采用真实 V2，不重新 Dobby 安装，验证实际 UnityEvent 父/Mid/listener 的 module、epoch、site、source/transport return、event/list/index/invokable 和 native 父/子序列。把实际回调与 IL2CPP 根及 metadata 捕获连到同一 owner generation，核 main/worker、递归、正常/异常结束。自有 fixture 的地址偏移只能用于自有测试，不能代入游戏 PC。

通过条件是同一次真实冷启动的 producer receipt、托管采用和实际 game callback 三者可相互核对，并记录失败后的责任留存。当前 [mapped-publication-evidence.json](mapped-publication-evidence.json)、[managed-adoption-evidence.json](managed-adoption-evidence.json) 与 [listener-managed-evidence.json](listener-managed-evidence.json) 分别验证了不同范围，尚未形成这条游戏端到端证据。

## 3. 区分 dispatch 与 leaf，补 mutation 和取消

当前源码可见的两个主要缺口是操作观察和生产者结束：对 `src/RizToolbox.Adapter/AudioContent` 与 `src/RizToolbox.Runtime/Hooks` 检索 `AddListener`、`RemoveListener`、`RemoveAllListeners`、`StopCoroutine`、`StopAllCoroutines`，没有发现这些操作的生产观察接线。现有 listener 通道记录 invokable 调用前后；因此即使 `ListEqual` 一直相同，也不能签发 `MutationClosed` 或“全部未来调用已结束”。

先保持现有 invokable dispatch 记录；再按真实 native ABI 覆盖 delegate 内实际 invoke loop、multicast 展开、Unity target 检查与跳过路径。virtual、generic/inflated、static/open、嵌套 multicast 等不能靠 metadata 名称猜执行签名，不能从一次 invokable 调用推出所有 leaves 都执行。未知对象继续 opaque 并保留根和 Pending。

给 listener 增删、persistent dirty/rebuild、executing-list 变更及可能直接写字段/列表的路径建立连续 mutation owner。仅比较入口/inline/退出快照，无法排除中途增删又恢复。mutation 必须关联同一实际 event/list/delegate generation，并明确不支持的写者如何使状态 Unknown。

随后给 delay、cutout、EventTrigger、音频/资源 provider 和其他能继续投递旧 run 工作的入口建立生产者归属与撤销。观察 StopCoroutine、StopAllCoroutines 或 provider Cancel 返回后，还需证明引擎不再 MoveNext/回调/排队交付；调用返回本身不是取消完成凭据。自然完成、取消、失败和重入应分别验证，旧 generation 的迟到 callback 不得借用新 generation。

这些操作必须分别解析实际 metadata、核对签名与 native ABI，再注册对应的操作观察，并建立谁持有调用权、谁撤销未来进入、谁确认取消完成的生产者责任模型。增加检查次数或反复抓取 leaf 快照无法替代这些生产接线。

## 4. 退休资源后再验收完整流程

只有 controller/result、scene/resource/audio/global 消费者、排队和在途回调、全部未来生产者都对同一 run generation 封口，才允许签发完整消费者结束与资源释放凭据。验证根保留到最后使用之后；身份不明、取消未确认或写后 Unknown 时继续保责任。listener 计数归零、原函数返回或账本相同，都不能单独授权释放。

最后以真实游戏验证完整 Retry、Return 与 Loader 生命周期：进行中撤销、结果页、返回菜单、重试、新 run 激活、旧 callback 迟到、场景/资源/音频 provider 清退，以及模块逻辑停用/重启和加载失败。每轮都核旧消费者不再访问已退休资源、新 generation 不接收旧事件、永久 native 通道与逻辑 sink 的生命周期一致，并单独记录不能物理卸载的模块/root 责任。

最终发布应另有清楚的完成矩阵，逐项挂实际证据和未覆盖路径；新包的安装与冷启动结果应记录为新批次，不覆盖本批 `installed=false` 或冻结归档。这里列的是下一阶段实施和验收顺序，尚未完成的步骤没有通过标记。

模块底层 scope19 的完整目标仍处于推进中；本批完成的是监听器 CLR 联合验证这一阶段。
