# Seek Hold 模式字段修复

实际长条重建准备读取了不存在的 `AutoPlay`／`FreeHold` 字段。固定游戏元数据将两项定义为 C# 自动属性，真正的存储名为 `<AutoPlay>k__BackingField`（Boolean，0x79）和 `<FreeHold>k__BackingField`（Boolean，0x7a）。错误字段名会让 Hold epoch 在捕获模式时直接拒绝，阻塞后续 Seek。

`NativeJudgementBackend.SeekHoldEpoch` 现在通过同一个私有 `ReadModes` 读取这两个 exact declared field；初始化捕获和每次后续 `VerifyModes` 使用相同字段、类型与偏移。实际模式变化继续拒绝，未增加别名查找或写入权限。字段依据与当前正式 `NativeSeekTargetNativeProfile`、TargetState reader 及已有 [游戏元数据证据](validation/gameplay-semantics-20261020/note-identity-proof.json) 一致。

## 当前验证

`work/n2-seek-hold-mode-fields-20261119/current-r1/` 原 outer session **21308** 自然 exit 0（chunk **75baf9**），5 个 child 均自然 exit 0：

- Release solution **0 errors／62 条既有测试警告**：PlaybackStartInitialization 55、Judgements 7，未宣称已消除。
- 三个新模式检查分别执行全部四种模式组合、两种实际模式变化、错误 Boolean 类型／偏移；共 **3/3** 测试案例。
- 相邻实际 TargetState、初始化目标和 note-time 目标投影 **28/28** 案例。

共 **31/31** 案例；1796 当前输入和 4 个受测 DLL 前后身份保持。三份真实改前文件和四份改后文件已保存；源码差异为 3 修改／1 新增。用户 README SHA256 保持 `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`。[封存汇总](../work/n2-seek-hold-mode-fields-20261119/validation-r1.json)已由 seal 自然 exit 0（chunk **9445af**）核验。

测试通过受控构造调用真实 private `VerifyModes`，以及真实 NativeRuntime／backend RunValue／target reader／independent roots。metadata 是合成 IL2CPP export fixture，未发行正式 observer access，未执行完整 epoch `Prepare`、`Start`／`Stop` 或实际游戏。**本批只验模式读取和相邻投影，不能签完整 Hold epoch／Seek。**

## 失败保留

`reproduce-r4` 用原产品执行真实 private mode check，actual child exit 1，失败明确位于 `SeekHoldEpoch.VerifyModes → InspectExactDeclaredReadField`，错误为字段缺失。expected-failure runner exit 0 只表示成功捕获，productAcceptance 为 false。

`reproduce-r1/r2` 是新增测试缺 dispatcher namespace 的编译失败；`reproduce-r3` 是受控 fixture 缺 `UnityEngine.Coroutine` metadata，虽 child exit 1，**不属于产品复现**。修测试后 r4 才确认原产品错误，全部原日志保留。

## 下一个功能切片

代码复核发现，Judge queues 已经通过 `BindHoldEpoch`／`ReadOwnedJudgeCount` 接收同一真实 epoch 的停止、目标和恢复计数，并独立核第二份完整样本；只调整该计数，不重置队列及 backing/version。上一批“Stop 清零计数”是待核线索，现在不能据此重写或放宽 baseline。

下一执行原注册 observer／profile 下的 Hold epoch `Prepare → StopPredecessors → StartTargetHolds → StopTargetHolds → RestorePredecessors`。核查目标 Start 部分完成时的恢复责任：当前 `_targetStarted` 在全部目标结束后才设定，异常进入 `_unknown`，因此不能把失败请求宣称可恢复；先取得实际正常退出／callback drain 材料，再决定是否需要细分已知和未知失败。

随后接实际 gameState／判定／rendering 的完整五参与者。上一批 956 项只属于其封存源码；本批源码变化后不借旧 956 或发行 ZIP 验收。正式 Fresh／固定 consumer、完整 Seek、B01–B19、真实 Speed／Seek 和 production 未完成。没有 commit／push、新包、安装／启动游戏、UI 或账号操作。
