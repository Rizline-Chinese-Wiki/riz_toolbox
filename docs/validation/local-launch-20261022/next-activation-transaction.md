# 完整资源包之后的 LOCAL 激活事务

2026-10-22。依据999包真实资源责任证据、1021保护宿主、1038暂存交接修复及 result_gate_review 子代理的只读审核。此文件是后续接线计划，不能当作激活验收。完整目标仍是 B01–B19；仅 B03/B18a 已勾。既有[静态报告](README.md)与[资源包实机](../runtime-local-bundle-20261022/README.md)各保持自己的包和证据边界。用户要求先继续代码、实测稍后，当前四门会话留待恢复。

## 已具备的组件

10月23日续接：[1081参数/结果步骤](../foundation-local-parameter-binding-20261022/README.md)已暂存未安装。`NativeInactiveControllerParameterBinding`已实现同pending claim/已收养bundle/已绑controller的exact setter注入与读回，仍不初始化设置或激活；`NativeLocalResultCapture`已实现registry claim先消耗→真实ControllerOver一次→立即root/核metadata/复制真实值，不伪造RunStarted/RunId。这两步不是完整事务handler。下一步原生归属依据[17范围终止证据](../local-result-ownership-20261022/README.md)，退休依据[消费者映射](../local-consumer-retirement-20261022/README.md)；用户仍要求实测稍后。

| 组件 | 已实现 | 不可据此推断 |
|---|---|---|
| LocalPlayBundleInputs / NativeLocalPlayBundleFactory | 精确revision fork、独立完整native图、同一包只允许一个session导出、延迟Destroy与保责任 | 设置初始化、实际controller消费、所有游戏异常和heap归零 |
| LocalRunSessionRegistry | Prepared→PendingCommit→Committed、精确controller/result页root、一次结果claim/cache、强消费者结束token后退休分类 | Commit只是事务状态；外部token接口不是已实现native结束验证器 |
| NativeControllerPrefabDiscovery / NativeInactiveControllerFactory | 953实测精确普通来源inactive clone/Awake0/7root后帧释放；1038暂存新增输出publication与名称第8根责任 | 新8根工厂实机、激活Awake恰好一次、六globals发布或非空旧值恢复 |
| OwnedNativeControllerGlobals | 保存六delegate与flag、核Target、条件恢复/保后继、独立清理 | 真实发布/恢复与native消费者已结束 |
| LocalResultStartTarget / BindLocalResultStartPolicy | 精确3D0B20 original前接管；错误/重入/worker claim不fallback；1021已真实解析/绑定 | 普通Start正向与LOCAL claim未验；不能只按当前有LOCAL局认领未知结果页 |
| LocalRunCommandTarget / BindLocalRunCommandPolicy | 精确ControllerRetry3C1310、ResultRetry3CFF20、ResultBack3CE020；69项Hook回归，1021四门已真实解析/绑定，普通ControllerRetry原调用一次 | ResultRetry/Back正向和LOCAL事务未验；不等于已实现重试/返回 |
| LocalRunProtectionHost | 四门安装后仍须handler才准入；跨模块撤回保未退休session/handler pending work，独立process pump，未知清理保32owner配额 | 没有实际启动/结果handler；首次result receiver归属仍缺 |
| NativePublicationArgumentRecords | 1038暂存中，bundle原生调用前固定128槽保输入；配额拒绝/临时free异常保责任，known成功才清槽 | 不扩大普通Invoke/NewExact旧契约，不等于真实native异常矩阵或heap上限 |

## 激活前必须形成的链

1. 宿主建立同一registry和长期保护owner。先验证并安装Start、暂停Retry、结果Retry、结果Back全部必需入口；精确签名/RVA或任一安装失败时拒绝新局。普通实例仍原参数调用original一次，LOCAL claim后即使provider/handler失败也不可fallback。绑定生命周期必须长于所有已提交LOCAL消费者，不能随provider或WorkshopChartModule.Release直接释放。
2. 准备完整bundle与进入时返回快照（实际scene/选择的LocalId、revision、chartId及generation）。RegisterPrepared接管bundle，BeginCommit先于任何clone。保留globals baseline和精确prefab，再创建inactive clone并在任何激活前BindPendingController。仅数字metadata相同不构成adoption。
3. RetainParametersForPendingSession验证当前claim及实际bundle，随后只通过精确MethodInfo调用SetInsideLevelParams。该setter36B0E0共享地址，禁止全局Hook。核读全部五引用、独立Chart/JudgeData/资源活性；按已验证的原版初始化逻辑设置Speed/Offset和其他必要局参数，失败保inactive，不盲设Speed=1或猜单位。
4. 受控激活前记录Awake窗口；激活后验证正向恰好一次、exact clone身份、实际持久scene及六delegate Target均归本controller。任何不一致进入仍受保护的失败事务，不能提前释放图或拿第三方globals当自己的。成功证据具备后才registry.Commit并开始LOCAL run generation。
5. 建立结果转场的归属机制，保证首次结果Start进入前已保留exact result receiver并BindResultScene；不能靠尚为空的levelOverInfo、曲名或metadata.From分类。**此项当前仍缺具体native接线。** 若改用宿主自有结果UI并在精确controller的LevelOver前接管，须先证明所有自动完成/失败/跳过路径都被拦住且不会转入stock结果页；该路线也不是现成验收豁免。
6. 结果handler先TryClaimResult，仅InvokeOnce可执行一次真实ControllerOver3BEA60（有协程副作用）。结果返回用publication接口立即root，再核result.metadata与本图，复制实际结果并CompleteResult；null/异常/root/解码不符用FailResult继续保护，不能重调或执行stock Start。现有result-origin observer只观测original来源，不能替这一步执行/发布结果。
7. 自有结果/错误状态提供可返回路径；若使用stock结果页，仅受控有限UI初始化，不能调用内联ToBestScore/Update/Save/Steam的原Start。既有GameEventObserver采样levelOverInfo不自动补出新LOCAL结果；不能伪造RunStarted/RunId或把旧最佳当本次结果。
8. Retry先准备同revision的全新bundle/Chart/params/controller/run generation，失败保当前暂停或结果状态；成功后由同一启动管线交接并独立退休旧消费者。返回取进入快照，不走LevelMetadata.From固定来源。场景、BackLock/TouchBlocker和输入焦点的平衡要有直接验收。
9. BeginReturn后让实际native verifier核controller、info、result页、协程/Hook在途、scene消费者及六globals均结束；只恢复仍由本run持有的字段，保留后继修改。VerifyConsumersEnded发exact token，Close先撤旧地址分类再释放root和bundle。bundle.Dispose仅请求Destroy，宿主必须继续poll其TryCompleteDestroy到已确认；registry关闭不等于Unity释放完成。

## 不能漏掉的故障和实机矩阵

| 触发 | 应有结果和证据 |
|---|---|
| 一项保护门未就绪/安装失败 | 无clone/激活；保已转移责任；新局明确拒绝 |
| provider在Prepared/Pending/Committed撤回 | 已保图继续有主；已提交局保护不因provider消失而撤掉 |
| clone/参数/初始化失败 | globals未发布；无可运行半图；独立清理、unknown不重试 |
| Awake重入/次数或Target不符 | 无假Commit；新旧globals责任保留，受保护失败可返回 |
| 完成、失败、跳过、提前返回 | 真实来源/每局RunId；LOCAL来源stock结果/保存/Steam调用计数0，普通路径不变 |
| 结果首次Start、重复Start、处理失败 | exact归属事先建立；ControllerOver只一次；始终不fallback |
| 同谱重试、快速重复点击 | 新图/新generation；下一局准备失败不摧毁当前可返回状态 |
| handler内停用/worker/Hook在途 | 主线程操作不越界；当前claim不退回stock；门不提前detach |
| 返回/旧scene卸载、后继globals | 条件恢复且保后继；强native结束证明先于地址分类撤回/root free |
| 长期与多局 | 实际多轮压力、IL2CPP GC/内存基线、旧消息/地址复用和资源回收；本次小fixture不代替 |

下一可交付里程碑是“宿主长期保护owner + 首次结果归属 + 精确参数初始化/受控激活事务”，随后才开放RequestPlay实验入口。不能只装空classifier的门就宣称保存隔离或可玩曲包完成。
