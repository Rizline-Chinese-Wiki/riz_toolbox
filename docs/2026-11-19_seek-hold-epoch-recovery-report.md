# Seek 长条部分启动恢复

原 Hold epoch 在两条目标长条中完成第一条的实际 Factory／Start 后，如果第二条在元数据检查时失败，会把此前已经正常返回、完整读回且回调排空的停止和启动全部归为 Unknown。第一条目标仍在生命周期表中，原长条不能恢复。本批保留这些已确认阶段的恢复责任，同时继续拒绝未确认的原生写入、异常 Start、输出发布或临时清理结果。

`SeekHoldEpoch` 现在在真正原生 dispatch 前登记 effect，字段或 Factory／Start 的独立读回完成后才确认。只有 `StartTargetHolds` 的已知检查／dispatch 前失败允许进入恢复；有 pending effect、实际 observer Unknown、完整 target 尚未确认或 `NativePublicationCleanupOwner` 时仍 Unknown。目标构造只尝试一次，重复请求不会重新 Factory／Start，也不会夺走已有恢复责任。

部分目标恢复先核原 source、profile、模式、输入、实际计数及全部生命周期记录和 iterator，再经原 Stop 停止已启动目标，恢复原 Judge 时间、计数和原 iterator。已生成但尚未调度的 Factory 输出由原根责任管理，恢复不额外发 Stop。Runtime 新的 reference admission 在 metadata、临时根与参数准备完成后才记 dispatch；正常返回 token 在 post-call 撤权检查前进入 publication 责任，避免输出在撤权异常中遗失。

## 当前验证

最终运行 [current-r2](../work/n2-seek-hold-epoch-recovery-20261119/current-r2/results.json) 的 outer session **51745** 自然 exit 0（chunk **4dd7eb**），15 个 child 全部自然 exit 0：

| 范围 | 通过案例 |
| --- | ---: |
| 实际 Hold epoch Prepare／Stop／target Start／恢复 | 10/10 |
| 相邻 TargetState、初始化与时间目标 | 28/28 |
| 模式字段 | 3/3 |
| Hold 生命周期、Stop、native owner／source／alive／discovery | 62/62 |
| Reference publication／void publication／exact 与 guarded field writes | 54/54 |
| 合计 | **157/157** |

本次 Release solution **增量构建 0 errors／0 emitted warnings**；本轮首次完整编译 `current-r1` 发出 62 条既有测试警告（55 PlaybackStartInitialization＋7 Judgements），未宣称已消除。1800 当前输入、6 个受测 DLL 及用户 README 前后身份保持。源码为 **7 修改／4 新增**，7 份真实改前与 11 份改后文件已保存并核字节。[封存](../work/n2-seek-hold-epoch-recovery-20261119/validation-r1.json)由 seal session **55046** 自然 exit 0（chunk **96da07**）核验。

新场景包含正常 AutoPlay 与 manual FreeHold、Stop 后无目标 dispatch 的失败、第二 Factory 前失败、同步 Move 后异常 Start、Factory 后 Start 元数据拒绝、字段实际 store 后读回失败、部分目标 count 被外部改变、Factory 返回时 run 撤权、原 Judge queues 同 epoch 组合，以及完整 target 最后读回失败。正常恢复清理零根，Unknown 路径保留原 owner，重复 Dispose 不重试未知 free。

队列组合实际调用既有 `BindHoldEpoch`／`ReadOwnedJudgeCount`，计数 **1→0→1→0→1**，18 次实际 Clear／Add／inverse 写入，完整 backing、capacity tail、size 与 signed version 恢复。三个原 Start／三个原 Move、两个 Stop 均排空。该计数接线原已存在，本批只执行验证，没有重写或用当前计数重置列表 baseline。

## 证据范围与失败保留

夹具复用原 FakeNative target／Hold owner／数组、真实 NativeRuntime、backend RunValue 与 Hold lifecycle。真实 ExactHookBroker 安装受控 Start／Move／六 Stop，实际 `JudgementModule.CaptureSeekBackend` 私有 issuer 在填入原模块登记字段后发行 access；本批实际执行 epoch `Prepare → StopPredecessors → StartTargetHolds → StopTargetHolds → RestorePredecessors` 和真实 backend observer callbacks。

模块安装字段是受控装配，profile／native original 是 shim。没有执行固定游戏 original body、真实 Dobby 或 engine Detach，没有正式 Fresh 票，也没有完整 gameState／rendering／五参与者事务。因此 **不签完整 Seek、正式 provider、固定 consumer、真实 Speed／Seek 或 production**。

`reproduce-r1` 中 case 3 的 actual child exit 1：Factory 1、Start 1、Stop 1、count 1，Hold／Start／Move in-flight 均 0，却被原 Operation 归为 Unknown。原 Execution 与 Runtime 字节仍匹配改前备份；profile 注入 seam 只用于进入原执行路径。expected-failure runner exit 0 只表示成功捕获，不能当验收通过。原 case 2 把错误注入 TargetProfile，会撤销 finger reader 来源，不能用它证明可恢复；最终改为仅受控 epoch profile 检查失败并保留原 reader 身份。

`focused-r1/r2` 是上述错误注入的阶段失败；`focused-r3/r4` 和 `queue-focused-r1` 只属阶段证据。`current-r1` 的 epoch 与相邻 Hold 案例均通过，但默认 Runtime 为 380/389，9 项缺既有原生 fixture 环境（`RIZ_INITIALIZATION_ABI_FIXTURE`／`RIZ_INITIALIZATION_CONTEXT_HELPER` 等），outer 实际 exit 1，整批没有验收。没有删除失败测试或借旧原生结果签当前源码。

`runtime-focused-r1` 为新测试局部变量重名编译失败；r2 是新断言误认为模拟 unknown free 后物理根仍存在。FakeNative 实际先移除 native handle 再抛错，修正断言并补原异常＋cleanup 聚合、保留记录且拒重试检查后，r3 54/54。所有历史日志、退出和输入保留，最终仅用 current-r2 的新构建与新执行验收本批。

## 下一功能

继续沿既有 `NativePlaybackSeekParticipants` 与 `NativeSeekGameStatePreparation` 组合 Hold epoch、audio、真实四 progress cache、实际 gameState／rendering 和 Clock；正向仍 Hold→audio→progress→gameState→Clock，反向先停止目标长条，恢复 Clock／gameState／progress／audio，最后独立读回后重启原长条。正式固定 profile／Fresh／consumer 的执行证据单独补，不把受控 shim 提升为正式准入。

相机、N1、退休批和前批 source／pitch／progress 切片不重做。B01–B19、R06／R07／R08 实机、完整 SDK／production 仍未完成。没有 commit／push、发行包修改、真实游戏安装／启动、UI 或账号操作。
