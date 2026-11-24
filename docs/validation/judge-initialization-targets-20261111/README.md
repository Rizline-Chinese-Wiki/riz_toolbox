# 显式 Judge 初始化目标：2026-11-11

**本批仅完成不可变初始化目标输出，不含原生初始化写入、完整事务或完整 Seek。**

`NativeSeekTargetStatePlan.InitializationTargets` 复制四个有效窗口、当前实际音符的最终 `inChallengeTime` 目标及挑战计数。未初始化时复用已验证的 Judge.notes occurrence／区间 OR 规则，重复成员和已有 true 不重复计数；已初始化时保留原缓存与完整 signed count。所有实际音符引用保留，不合并多个实例。

新增4组正式backend/RunValue测试，当前判定 **200/200**；构建 **0警告、0错误**。[RED](red.log)明确为缺少InitializationTargets/目标DTO，之后[GREEN构建](green-build.log)与[console回归](green-judgements.log)退出0。小范围[独立源码复核](source-review.json)完成，没有已确认的正确性问题；范围不包含HoldGeometry、原生writer或实机。证据与本批源码哈希见[build-test-result.json](build-test-result.json)。

目标数组只读、records不可变，可作为后续writer输入，但不能据此移除 `JudgeInitialization` / `ChallengeFlagsAndCount` RequiredWork；生成目标不写任何native cache、flag、count或ready值。

下一步的实际写入、ready最后发布、条件恢复／原始位／partial failure／unknownfree要求见[next-native-writer-contract](next-native-writer-contract.md)。新的原生writer测试正在另一个批次编写，不在本次200/200结果范围内。本批未启动/安装游戏，未改代理/账号/旧ZIP，未提交推送。B01–B19仍未整体完成。
