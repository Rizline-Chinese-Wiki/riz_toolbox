# 有限交叉审查与冻结复核

本批由父代理负责生产协调器、Runtime 发布接口、整合与完整构建；两个子代理分别实现/核对激活及转场、设置初始化，另一个子代理完成结果 sink 审查与实现。作者与审查者有重合，本文不能称为完全独立的安全审计。没有安装或操作游戏，也没有为新 1266 包另跑一个独立 native ABI runner。

## 交叉审查实际处理的问题

- 单独激活 controller 不会创建 Level 场景。审查补入真实 scene-manager 调用、新 AsyncOperation 与完成凭据；帧泵先等新 loaded Level，再核验实际初始化图和 commit。
- 原场景卸载后暂存 parent/transform 会失效。初次激活仍要求五对象完整；新增的转场后读取只接受同局同 controller 的已完成转场，允许两个暂存对象成对死亡，持续核对三个持久 clone 对象、DDOL scene、一次 Awake 及六 globals。
- 设置核验不能在 getter 首次进入时随意创建一个新基线。现在核对实际 setter 的五引用与原始数值位；生产安装前预解析方法/字段/哈希，getter 前后使用同一回执。
- 结果窗口逻辑关闭不能证明正在执行的按钮回调已经结束。sink 追踪回调计数，控件/窗口释放失败进入 unknown；同步 Close 在回调 finally 之前不签发 ConsumersEnded。
- 新初始化故障用例刻意保留未知责任，会占用原有固定 32 配额。四个新用例隔离到专用子测试进程，没有扩大或重置生产配额；unknown 的 throw 行为保持，修正了误期望 false 的断言。

## 核对过的交付边界

[build-summary.json](build-summary.json)与[完整日志](build.log)一致为 25 suites、1266/1266、Runtime136/Hooks99/Audio420，日志 SHA `ab2b729e680912a0ccdadddc97578d5ed254f59d1ee93d9a92102a507c60db54`。完整增量日志没有编译诊断；前置 Audio 的 14 条 FakeNative CS0649 和 1 条测试 CS8602 原样保留，不宣称干净构建零 warning。

父代理打开 383 项源码 ZIP 与 18 项暂存 ZIP，逐项核对成员集合、CRC、SHA 与当前声明输入/暂存文件。旧 1021 的实际 18 安装文件、其旧 326 源归档和原游戏三文件各自重新匹配。检查脚本及机器记录位于本目录，source/package ZIP 不含本批说明文档，也未封装全部工具链或依赖。

父代理再次从实际 PE 读取 scene 静态证据的 7 范围、1895 指令，匹配字节及范围 SHA，并冻结 7 份静态附件。该脚本只复核相应 PE 内容，不冒称复核证据文件列出的全部外部输入。完整 static ZIP 的 SHA 为 `7739c428b5e062beb0342656c8394bd406a61e0e61848365c7dfbc84d03db18f`；literal 恢复限制依旧保留。

源码搜索及读取表明：Loader/模块还没有构造 coordinator/result sink/完整 handler 链，`RequestPlay` 仍返回 OperationUnsupported。coordinator 保留成功或失败后的 process owner，没有可调用的完整活动局退休路径；Retry/Return 回调没有完整命令实现。它们是本批未开放用户入口的直接依据，不是新包已经可玩的证明。

设置子代理随后只读交叉核对本目录四份说明与当前状态，确认上述使用边界；发现正式报告 callflow 将 origin 捕获误排到 activation 后。已按实际 coordinator 修正为 setter→捕获 origin→activation→StartNextScene，未修改冻结源码或包。父代理另核对八份说明的 218 个本地链接均存在，检查表仍仅 B03/B18a 勾选，见 [documentation-check.json](documentation-check.json)。

## 未由本次审查证明

实际游戏 activation/Awake/全 globals、实际 scene/初始化、间接或异步 SaveSteam 全覆盖、菜单输入/相机恢复、完整返回/重试及 active 消费者退休都仍需后续实现或验证。既有 Start/getter native original 异常 fixture 的范围没有扩大到 Play/Progress/Timeline。未发现以上已审查路径的其它确定接线错误，不等于证明整个底层没有缺陷；B01–B19 完整目标仍未完成。
