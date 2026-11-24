# 原始结果来源补丁专项证据

日期：2026-10-20。属于 666 包之后的源码；没有安装、没有启动游戏，不能把以下离线通过计入 666 实机验收。

## 行为

固定构建的 `NormalModeInLevelController.ControllerOver@0x3BEA60` 增加纯观察入口：在原方法进入前固定已观察局的 session/run/controller/information，原返回成功后验证类型、receiver 图和 metadata，独立保根保存实际返回对象。

采样只有消费到该真实对象来源，且来源匹配当前开放局时才能发 RunEnded。未知、旧局、冲突、撤回来源只产生未关联 ResultObserved，不能再靠同谱或未知 metadata 给当前 RunId 盖章。场景、暂停、正常结束导致 revision 变化不影响同局来源。

registry 同时最多 32 个待消费对象和 32 个已消费身份缓存。每项独立持有 result/controller/information 根，查找读取活跃 root target。容量满拒绝新项，重复/冲突释放新根，消费缓存淘汰或模块撤回释放所持根；重装代次拒绝旧在途提交。

能力 `riz.lifecycle.result-origin.observe` 为独立、可撤回、Experimental；缺失时只读基础结果仍可观察，关联不回退。纯观察接口没有改返回权限；与原有内部结果替换协调器共享一处补丁时仅观察原始对象，替换对象不能借其来源。

## 红绿结果

| 执行 | 结果 | 日志 |
|---|---|---|
| 暂时恢复旧 chart-only 关联谓词，其他源码相同 | 73/77；4 个来源相关回归失败 | `result-origin-regression-before.log` |
| finally 恢复新来源必要条件后 Game.Tests | 77/77 | `result-origin-regression-after.log` |
| Hooks.Tests | 43/43 | `result-origin-hooks.log` |
| RunModification.Tests，保留原审计断言并用真实捕获/注册/消费 fixture | 17/17 | `result-origin-audit.log` |

上述使用 `dotnet run --project tests/<project> -c Release` 串行执行，无编译警告。旧谓词故障分别为旧 A 结束已 over/暂失的 B、原方法在途换局后借当前局、未知来源退回 metadata 推断、撤回后仍关联。

Hooks 新增测试覆盖入口来源先于协调器 capture，观察原返回先于变换，原返回保持、原异常对象保持、null、类型/root 拒绝、capture/after/release 异常隔离、错误线程、重入、receiver 不匹配、在途停用及每项独立清理。既有变换协调器独占/共享一处补丁测试继续通过。

## 验收边界

这是离线语义修复和固定二进制静态接入，尚不是 ControllerOver hook 实机可用证据。还需完整构建、实际正常/失败结束、同谱 Retry/Back/Skip 后新局、结算页返回、模块撤回与重装的新会话日志。未验证所有历史关闭局的成绩回补，未自动完成 B09 或整个底层工程。

原始具体设计与静态证据索引：`../runtime-visual-scope-cleanup-20261020/late-result-origin-next.md`。工作断点与文件责任：同目录 `result-origin-implementation-checkpoint.md`。
