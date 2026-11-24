# 结果来源补丁工作检查点

2026-10-20，代码和专项回归已落盘并通过。**未构建新包或安装、未做新 hook 实机验收**。666 包实机记录仍仅证明其冻结源码，不证明本工作树补丁。串行构建时段已释放给主代理。

已落盘：

- `src/RizToolbox.Adapter/ResultOriginRegistry.cs`：32 个待消费结果、32 个已消费身份缓存；独立根接口、代次、冲突拒绝、去重与撤回释放。
- `GameEventState.cs`：记录真实 controller/information 对；增加入口来源捕获；`Finish` 关联必须有匹配来源，移除 chart-only fallback。
- `GameEventObserver.cs`：生产 controller 身份传入 state；通过 registry 消费真实结果对象，再发布事件。
- `Hooks/ExactHookBroker.cs`：纯 reference-return typed capture/after，原返回不变、根独立清理；协同接入长按代理的 Boolean partial switch，普通 Bind 拒绝 Boolean。
- `Modules/ExactResultOriginModule.cs`：固定原版 SHA/Unity、RVA `0x3BEA60`、字段布局/返回类型；捕获入口图，核对原返回 metadata，再独立保根交 registry。
- `RizlineAdapter.cs`、`Sdk/LifecycleContracts.cs`：独立可撤回实验能力 `riz.lifecycle.result-origin.observe`。
- Game.Tests 新增来源/代次/容量矩阵；Hooks.Tests 新增纯观察与异常/清理/重入/共享协调器矩阵；RunModification.Tests fixture 使用捕获→注册→消费来源，保留原审计断言。

专项结果：

1. 暂时恢复旧关联谓词运行 Game，73/77；同谱旧 A 结束 B、入口换局、未知来源、撤回四组明确失败。finally 已恢复新谓词。
2. 修复后 Game 77/77；Hooks 43/43；RunModification 17/17。三项串行完成，无编译警告。
3. 日志：`../runtime-transition-fixes-20261020/result-origin-regression-before.log`、`result-origin-regression-after.log`、`result-origin-hooks.log`、`result-origin-audit.log`。
4. 模块字段证据复核：dump `InsideLevelParams.metadata` / `InsideLevelResult.metadata` 均为 ILevelMetadata，父类 LevelParams/LevelInformation 分别 0x20/0x28；原 ControllerOver 静态构造并返回原对象证据仍在原计划引用文件。

下一步由主代理整合：全量构建和模块依赖/能力兼容检查；新包冻结后再实测 ControllerOver 返回→实际结算采样。自然同谱 Retry 后迟到结果、正常/失败结果和原生模块撤回仍待实机证据。正式宿主已有 Skip/Run scope 边界回归保留，新增来源回归直接断言旧结果没有 RunEnded；本批未额外增加“真实宿主 + 迟到结果”的专用整合用例。

未修改 Judgements；不启动或操作游戏。
