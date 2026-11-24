# Tween 实际源点、队列转移与内联事件

2026-10-28。本批将 22 个实际指令源点接入同一次预初始化发布，记录重复 Tween 的源槽位转移，并将五处零参数子事件 CALL 与真实 operation／Invoke 关联。实际构建和下列验证已通过。完整 effects、对象死亡与最终根释放仍未完成；没有安装或操作真实游戏。

## 实际调用链

operation 当前统一为唯一 `RizFirstInstall_GetInfoV6`，capability 4、25 个操作、56 次写入、2048 字节。56 次是原来 34 次加 22 处源点写入。独立 EngineTransport 仍使用其 V5／91280 字节协议，二者不是同一个版本号。旧框架协议兼容不作为要求。

新增源点覆盖两个 ready pop、playing 插入／搬移／清除、scheduled push／set／三个 RemoveAt、尾项死亡分支、三个 Execute CALL，以及 Update 两个 onStart、Play 的 onStart/onComplete 和 Execute 的 onComplete。具体固定 RVA 见 [profile](../../../native/RizToolbox.JudgeContext/TweenSourceProfile.h)。每段 raw store 与原 GC write barrier 连续重放，中间不进入 CLR；真实 GC 行为仍需要游戏实测。

[原生发布器](../../../native/RizToolbox.JudgeContext/TweenSourcePublication.cpp)在写入前分配永久 8 KiB RX relay、注册 56 个动态 RF，并核对 172 个真实指令边界的查找和异常恢复。配置包含 43 个实际 relay 返回点。CALL/RET 保持匹配，不改真实返回地址；死对象选择结果写入 outgoing home cell，relay 保存并恢复 flags 后走固定续行。普通源点仍执行原始指令；ready 确切死亡项回原取栈循环，dead-tail 走原清槽／barrier 路径。

新增 source publication 是 2032 字节私有回执，绑定全部源地址、relay、continuation、块与函数表。主 V6 Ready 同时核源点、完整方法体与该回执；预初始化只发布代码，IL2CPP 官方类型在初始化后另行核验。Runtime 的生产 reader 由真实已加载 producer 发行，不能从复制快照构造。

[原生 journal](../../../native/RizToolbox.JudgeContext/TweenSourceContext.cpp)保留实际线程、嵌套 operation sequence、source／relay PC、真实槽位／版本／容量、事务和独立 occurrence。192 字节记录、96 字节 Info；8192 条环形窗口允许正常长期读取，过期读取明确拒绝，尚未配对的 Before 不得被覆盖。独立 Play 不伪装成 Update；Execute 同时核 manager self 与 Tween 参数。

[Adapter 重放](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.Sources.cs)给同指针的两个排队位置不同标签；pop 先进入 transient，再按实际 playing 安装或明确丢弃退出。copy 与 clear、Push 与 RemoveAt 分别记录实际位置，完整最终容量向量核验后才提交。跳过原函数的空 source operation 不制造 instant effects。已有活任务继续运行，Seal 只关新 Play/Add。

[内联事件关联](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.InlineEffects.cs)核当前私有 source pair、真实 Invoke context／generation、线程和 relay 中的实际原 CALL 返回点。当前只建立真实调用来源，`EffectsEnded=false`；完整 child event 内容正在下一批补齐。

## 实际验证

原件均在 `C:/Users/71957/riz-current-checks/`，以下全部是已观察到终态 exit 0 的运行。

| 验证 | 结果 | 目录 |
|---|---|---|
| 完整 helper 严格构建及实际 PE EAT | `/W4 /WX`、唯一 V6、全部新增导出通过 | `tween-source-native-r3-20261028` |
| 实际 source publisher Prepare 诊断 | 262 检查，172 边界零失败；无 source 写入 | `tween-source-publication-diag-r2-20261028` |
| 当前 helper 映射发布与实际内存篡改 | 37 进程、47468 检查 | `tween-source-mapped-r3-20261028` |
| 自有 source 行为夹具 | 8 进程；1573298 条循环断言 | `tween-source-owned-r2-20261028` |
| 同源码托管核心 | Audio/content 894/894、协调器 28/28、对象身份 10/10 | `tween-source-core-r1-20261028` |
| 三套专项 | Runtime 249、Module 26、Hooks 199，共 474 | `tween-source-managed-r1-20261028` |
| 同源码实际 CLR 采用 | 两进程、508 检查 | `tween-source-adoption-r3-20261028` |
| 六组件 Release | 成功；一个既有生成源码 nullable 警告 | `tween-source-release-r1-20261028` |
| 当前 helper 对应的 admission 行为 | 四进程、359 检查 | `tween-source-admission-r1-20261028` |

37 个映射进程包括原来 12 种、22 个源点分别篡改，以及新 relay、unwind、函数表篡改。每次先验证 22 行、43 bindings、56 RF 和 metadata 尚未绑定／零 callback，再修改实际字节并检查 Ready 转 Unknown。这只证明受控映像发布和拒绝，没有执行游戏函数。

8 个 source 行为模式为 happy、capacity、broken-pair、wrong-parent、foreign-tail、abnormal-exit、unbound、unfinished-window。真实执行 preserving ASM 与 43 个 CALL 返回身份，检查 GPR/XMM/flags/DF/MXCSR/FPCSR、选择分支、重复槽位、容器变化、嵌套和环形窗口。大量断言来自相同机制的循环，不能换算成完整游戏覆盖率。所有对象／容器／barrier 模型自有；metadata/admission 前提受控，未证明游戏 GC 或完整栈别名。

9 条 Adapter focused cases经实际 typed reader 和 retained context覆盖重复 occurrence、transient、copy/remove、回调重入、完整容量不符、旧 context／陌生指针拒绝、Skipped 和已预保根的 foreign scalar。Runtime 另增加 10 条 source reader／metadata 用例及两条环形窗口用例。

## 已定位并修复的失败

保留所有失败目录，未覆盖原件。

- `tween-source-owned-r1`：MASM 将四个 `LABEL BYTE` 分支目标解释为 byte operand；改为 `LABEL NEAR` 后重跑 r2。首轮没有行为用例执行。
- `tween-source-mapped-r1`／`publication-diag-r1`：Flags 区域使用额外 ALLOC8＋CHAIN，parent 的固定 frame base 导致保存寄存器和 XMM 读址差 8 字节。12 个边界失败；改成 48 字节独立 fused unwind，将 SAVE 偏移加 8 并使用 FAR 编码，保留 ALLOC／PUSH 顺序。r2 诊断全部 172 边界通过，没有放松比较。
- `tween-source-mapped-r2`：原写入函数只更新 UnityEvent 方法体 seal，第一个 Tween 源点写入后拒绝，累计 35 次。现改为写前精确分类 Unity／operation 已存在 seal，验证原字节并在成功写入后更新正确完整方法体。新 r3 全部通过。
- `tween-source-adoption-r1` 因主发布未成功而失败；r2 的实际 happy child 已通过，但 runner 仍要求旧 listener version 5 而拒绝输出。修正当前合同为 6 后，新 r3 两进程通过。

## 当前边界和后续

生产 reader 只解析事先独立强根保留的对象。新容器扩容、Awake 重建、新 child event 或陌生 Tween 不能从事后 journal 指针补根后冒充 source capture；这些正常动态路径仍需同步 borrowed-reference publication。shared manager 已有 foreign Execute/onStart 的受核标量历史，但 foreign native 层再重入 owned Play 的完整 physical mask 链尚未实现。

零参数子事件完整预激活登记、`onTween<float>`、任意 listener／setter／engine 派生效果、实际死亡区间、物理排空和一次性 Unregister／root-free 仍需继续。`CanRetireWork` 保持 false，源点正常返回不等于 effects 结束。完整 B01–B19、生产首次执行准入与真实游戏整局验证仍未验收；性能优化后置。

## 冻结哈希

候选已实际打包并通过独立迁移核验：`artifacts/foundation-tween-sources-candidate-20261028/`，24 载荷／26 证据。manifest SHA256 `51bc4e2c0ce079220098c44aaa4a6932093cf075512af4a8e6911491b7fc0fbe`。新增 19 项合成安装矩阵已全部通过，原件在 `D:/riz-build-cache-next/tween-source-install-r2-20261028/`。先前受限环境 r1 为 17/19，两项目录链接在测试准备时被沙箱拒绝，未计为安装器验证；获准环境 r2 真正建立目录链接后验证拒绝并全部通过。没有操作真实游戏。

- helper：`d08976a0e2e45b9e0783a697f64329426352a281e2353ad71b52d9e63ad47e9e`。
- native manifest：`8dab60360e42aecbcd39def2ba6c77e34b9d4d0f83b70a20d6d39d5953b7206a`。
- 映射结果：`ee17ee0f92ff712783f14af1444f57cfbe2771755d6b19412f1d0370470ffa40`。
- owned 行为结果：`831e0451cae7b9b3cafdf660fd6da6af20cea46fe829611b8132c27ad79f0284`。
- core 快照：`4979df5cd387486e24f821506f23d3cfad6cfc86764f982a48153a361283cfa1`。
- 三套专项：`3e417d992d4ad70a9d1874584f15cf9990030932a83dc4d62d503021f826e6aa`。
- CLR 采用：`ecb4fde7e52ff327c07b129c27ab3d0453eb462d19c06d82a5fb2f78c825f212`。
