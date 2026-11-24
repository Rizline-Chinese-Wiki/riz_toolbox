# 初始化批有限独立审查

本文件记录源码施工时的只读交叉审查，以及完整构建后的独立文件/日志核对。审查代理没有运行构建、安装或游戏；完整构建由主任务执行，独立native runner由另一代理执行。下述结果均读取原始证据并重新计算hash，不把它们说成独立游戏验收，也不保证不存在其它缺陷。

## 冻结后的实际核对

- 重算 [build.log](build.log) SHA与摘要相同：`f7f9d4ea7ee17e6dc02ef4d3d0073e3212607eb12dbd639c4d8f9a06d895cedf`。25个suite合计1210/1210，Runtime131、Hooks96、Audio372；13条诊断均为共享FakeNative CS0649，没有编译错误。读取完整Runtime中的生产真实Dobby初始化case为PASS。
- 实际打开 [364源ZIP](source-inputs.zip)、[18暂存ZIP](staged-package.zip)，重新校验成员集合、CRC、每entry SHA，并逐项与冻结时当前source/artifacts匹配；未重写manifest或ZIP。归档SHA分别为 `0cd7d8ac15b436ad2d7a0a1063134338ddcabf83912b6da3f8f2411668c27014` / `cbdc9040f5a94290d7a6420d6774bce54ce75b25d801f049fd341ab0dc420da4`。
- 实际读取当前游戏目录18文件，仍匹配1021；另打开其旧326项源ZIP逐项比SHA，三原游戏文件也再次匹配。不拿新source与旧安装作同批比对。
- 读取 [独立native runner](independent-native-abi/README.md) 原始输出/result/manifest，核对exit0、编译0 warning/error；重新计算全部六项实际输入SHA。其Runtime与本批暂存manifest相同，测试源与364输入manifest相同，native fixture与本批归档相同；主任务另留 [package-match.json](independent-native-abi/package-match.json)作明确连接。
- 最终读取 Hooks第四次96/96、Audio第二次372/372、Runtime定向131/131；首次Hooks的CS1501编译失败、第二次95/96旧重试断言失败原样保留，未以最终通过覆写历史。

## 实际发现与修复轨迹

| 发现及复现条件 | 已读修复与最终回归 | 保留限界 |
|---|---|---|
| Before首次native读取前未置busy，可在RetainStart同步回调中重入预留；Core又会清掉外层busy | Before/After持事务busy；Core保存恢复outerBusy；IsReadyForHost与native/activation记账重查真实drain；相应Audio初始化路径通过 | 不据此宣称全部真实游戏重入矩阵完成 |
| controller.LevelParams被替换为另一个同metadata参数时，Before可先放行original，After才拒绝 | Before新增VerifyInitializationInput，以实际bundle闭包核对精确params identity/五refs/资源，再放行original；After重复并核返回root；错input与生产受控字段/publication回归通过 | 不等同于游戏外部写入实测 |
| worker初始化Fault原先只置bridge全局位，已发布ready可能等main Tick才失效 | worker纯token通知launch原子fault；IsCommittedReady、pending验证及commit末复查都读取该位；worker/fault-race回归通过 | 它是sticky失效，不能推成全入口暂停或未来callback已终止 |
| terminal Progress内ordinary Start/getter/Play只遮蔽Mode，普通子调用Instantaneous/EndRange仍误用外层终止权限 | Runtime删除Mode限定，所有Timeline权限按较新scope遮蔽；child payload/handle回归通过 | 只对明确作用域与直接接口保证权限边界，不泛化任意间接Steam路径 |
| 独立Instantaneous/EndRange入口虽计数，但bridge使用的binding读数遗漏它们 | Progress binding叠加static真实活动，重绑检查static在途；初始化binding保留Start/getter/Play/Mode/Tooltip活动；static-drain回归通过 | bridge聚合中的Mode为保守重复计，不应表述为准确唯一调用总数 |

这些问题均按具体条件先发主任务，修复源码与本批source归档逐项一致；相应PASS读取自最终日志，不是本审查代理重跑测试。已修复的问题没有继续描述成当前未解决缺陷。

## 当前可确认的实现边界

纯身份分类只读已发布的session/run/controller/progress令牌，不读取root target或native metadata；首次主线程Before采用Start、info、progress，并与预留controller和owned bundle关联。最终ready还要求getter返回已核验、原Start退出、真实入口drain及原生loadOver读回。退休先撤纯map，再复查实际在途后独立free；unknown保留process owner，不当作根已释放。

已执行 `rg -n 'NativeLocalRunTerminationBoundaryState\.Create|new LocalRunTerminationHandler|NativeLocalRunLaunchReservation\.Reserve|LocalRunTerminationHandler\(' src`。结果只有handler构造声明，没有Loader/模块生产调用点。当前离线测试确实可驱动生产Runtime/Adapter接线，但不能写成游戏中的LOCAL初始化路径已启用；RequestPlay的OperationUnsupported边界仍存在。

Mode通过已有Progress entry共享。初始化/Play权限与terminal Update权限分开；ordinary子作用域必须遮蔽旧权限，logical detach只能关新准入，不能撤掉在途claim。跨线程Fault通过原子位阻止ready，业务main回调不在worker运行。

固定根槽与原配额已复用，但immutable映射数组及identity仍可能在native采用后分配。分配失败被捕获而转unknown是保责任策略，不能宣称已满足I04所有发布存储预先分配。历史bundle.Read等helper内部临时root责任也未因新增外层根槽自动完成重构。

## 本审查不证明的事项

- actual activation、Awake一次、真实settings/globals发布/条件恢复和RunStarted；当前RequestPlay仍明确不支持。
- 目标游戏实际metadata/prologue安装、普通流程对照、LOCAL连续游玩和原版Save/Steam零调用。
- 所有初始化/异步/间接Steam入口；仅在已固定的Start/Play/终止锚点范围内设计隔离。
- 原版native original SEH/C++异常跨CLR replacement后的unwind/finally完整性；新fixture的callback失败/null返回不覆盖此项。
- 已激活局的未来callback不存在、完整consumer-end verifier、active退休或native heap/GPU归零。
- 本批新系统重现、完整B19、全部B01–B19验收；没有新增勾选。

完整构建和归档已按上述范围复核，不倒填游戏验证，也不把测试数换算为覆盖率。I04完整预分配、实际activation协调器/生产调用点、native original异常输送与active退休等继续作为明确下一实现或验证要求。
