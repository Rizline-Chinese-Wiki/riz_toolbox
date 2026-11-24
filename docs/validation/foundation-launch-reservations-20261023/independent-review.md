# 1187 冻结批交付审查

本文件由独立文档/静态证据子代理在源码冻结、完整构建之后完成。审查范围是结果一致性、归档逐项完整性、当前安装边界，以及对关键生产接口/对应回归的有限只读抽查；**不是第二次完整源码审计或游戏运行验收**。没有重跑构建、安装或修改既有证据 JSON/ZIP。

## 实际核对

1. 读取 build-summary，重新计算25个 suite 总数为1187/1187；Runtime130、Hooks82、Audio364；完整build.log的SHA与摘要一致。编译诊断列表为空，两个native fixture各自保留 `gameRuntimeVerified=false`，未混称为游戏验收。
2. 实际打开352项 source ZIP、18项 staged ZIP；核对成员集合、CRC及每项SHA，并分别与当前声明输入、artifacts/package逐项比较。ZIP SHA与已有archive-check一致，未重新生成归档。
3. 再次读取游戏目录18项实际安装文件，与1021 manifest/previous-installation-check逐项比SHA；另打开1021 source-inputs.zip核对326项，不将当前工作树误当旧源码。三份原游戏文件同时重新比SHA。
4. 抽查 `LocalRunLaunchCommitState`、`NativeLocalRunLaunchReservation`、pending result、capture factory，以及 bridge 的 Reserve/Attach/VerifyInitialized/IsDrained；抽查 pending capture、factory和bridge回归注册及最终日志中的相应PASS。源码声明输入与已冻结ZIP相同。
5. 读取前两次整合失败原文与第三次通过结果。六个handler安装拒绝和其后容量断言失败均保留，未宣称所有阶段初次通过。

## 确认的边界

| 核对点 | 审查观察 | 不能推出的结论 |
|---|---|---|
| 同一四类配额在native前预留 | coordinator检查同一个handler/bridge，capture/factory/queue/graph先占既有槽；错误bridge先拒绝，空回滚与native尝试后的保留分开 | 已实施实际激活、Awake或完整原生初始化 |
| 同一 shared ready | 真实graph采用和核验后才允许prepare；registry commit与共享token发布之间不调用新的native/participant回调；early terminal阻止可用ready | 真实RunStarted、getter已被原游戏调用、主线程调度全矩阵已验 |
| pending capture 槽复用 | ReservePending不触碰backend/source/root；AttachPending只接实际backend；提交后在同一owner初始化；现有32配额与legacy共用 | 游戏结果已采到、初始化Hook/active释放已连通 |
| 线程与失效 | root-free worker claim先记录pending early terminal；graph worker查纯token；失配/unknown/native尝试后不能空取消 | 全部异步消费者已终止，或未知根已物理释放 |
| 归档/暂存/安装分离 | 1187的352源/18staged ZIP、1021的18安装/326源ZIP各自匹配 | 新包已安装、旧包实机证据自动适用于新包、干净新系统可复现 |

## 仍需实施与验证

当前没有初始化原生hook实际调用graph接口，也没有生产激活/完整sink与Retry/Back/active退休事务。`VerifyInitializedGraph`的现有drain覆盖范围不能自动包括尚未接入的Start/getter/Play；下一批必须把这些真实入口及共享static输送层纳入。独立静态证据已确认Start的两项Timeline调用位于Progress around外，Play还有独立Mode尾调；不能用本批管理状态宣称Save/Steam全覆盖。

具体步骤见 [下一批初始化隔离](../local-initialization-order-20261023/next-initialization-isolation.md)。保持用户“实测稍后”的现场安排；此审查不勾选B任务、不更新goal完成状态。
