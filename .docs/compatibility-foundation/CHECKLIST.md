---
plan: compatibility-foundation
version: 1.0
created: 2026-10-19
status: active
---

# Checklist: compatibility-foundation

完成必须同时具有实现和对应证据；代码存在但游戏内行为未验证时单独注明。

- [x] F01 能力变化驱动任意插件启停，记录用户意愿、等待和故障 → [SPEC.md#lifecycle](SPEC.md#lifecycle)
- [x] F02 自动归属服务与撤销、每插件配置 → [SPEC.md#ownership](SPEC.md#ownership)
- [x] F03 有序强类型扩展点、订阅隔离、类型/版本检查 → [SPEC.md#extension-points](SPEC.md#extension-points)
- [x] F04 独立包、私有依赖隔离、入口/清单验证 → [SPEC.md#plugin-packages](SPEC.md#plugin-packages)
- [x] F05 Runtime/Adapter/Loader 分离、构建配置与符号探测 → [SPEC.md#adapter](SPEC.md#adapter)
- [ ] F06 实际场景/开局/结算事件接入与游戏内验证 → [SPEC.md#adapter](SPEC.md#adapter)
- [x] F07 真实游戏资源目标与多层覆盖/恢复 → [SPEC.md#game-resources](SPEC.md#game-resources)
- [x] F08 移除宿主样例特例，至少三个独立 SDK 插件包 → [SPEC.md#plugin-packages](SPEC.md#plugin-packages)
- [x] F09 构建安装升级、核心与隔离测试、准确文档 → [SPEC.md#distribution-and-validation](SPEC.md#distribution-and-validation)
- [ ] F10 组合运行证据：独立插件、自动清理、资源共存和事件 → [SPEC.md#distribution-and-validation](SPEC.md#distribution-and-validation)

## Verification 2026-10-19

已完成 M1/M2/M3 的代码实现，M4 的离线与启动资源验收已完成。F06/F10 保持未勾选：场景、开局及结算观察器已接入，符号探测、订阅及 26 项边沿/配置检查通过，但启动自测没有进入实际游玩，不能宣称开局、结算或场景切换已经在实际流程验收。

最终构建零警告、零错误；Core 24、PNG 10、Hosting 16、Game 26，共 76 项自动检查。安装/卸载 12 项检查通过。实际游戏运行加载五个独立包，两个包在同一真实 Image 上完成 2→1→0→1→2→1→0 层覆盖、恢复和再次启用，日志无 Error，原始二进制哈希不变。完整证据见 [实现报告](../../docs/2026-10-19_实现-Rizline兼容底层v02-report.md) 与 [运行摘要](../../docs/validation/v02/runtime-summary.json)。

F07 的实际目标是游戏原有 `EdgeCoverCanvas` 的 Image，排除了工具自身面板。主菜单别名共享覆盖栈已实现；启动页没有进入主菜单，别名在真实主菜单的出现仍需后续检查。

## Gameplay verification 2026-10-19 15:22

用户实际游玩后，当前会话记录了 4 次场景通知（GameStart → MainMenu → LevelList → Level → LevelOver）、1 次结算通知、3 个真实主菜单资源别名；事件示例插件确实收到了通知。本次日志无 Warning/Error。结算谱面为 `chart.GreatnessEarth.Axya.0.IN`，分数 66492，`Crashed=true`，这是失败结算路径，不能代替正常通关验证。

本次没有 `game.run.started`，因此开局与结算的 RunId 关联尚未通过，F06/F10 继续保持未完成。证据见 [游玩日志](../../docs/validation/v02-gameplay-20261019/runtime-before-fix.log) 和 [摘要](../../docs/validation/v02-gameplay-20261019/summary-before-fix.json)。

排查发现 Runtime 将 IL2CPP 一字节 Boolean 按默认四字节 BOOL 解码的独立缺陷，可能干扰活跃对象筛选；已统一修复字段/装箱返回值读取和布尔参数写入，并加入只在状态变化时记录的控制器诊断。新增 3 项回归检查后，全量 79 项检查通过。用户退出游戏后，补丁于 2026-10-19 15:32 安装，17 个包文件哈希一致，原始游戏二进制未变；见 [安装校验](../../docs/validation/v02-gameplay-20261019/install-fix-verification.json)。修复后的开局仍待实际游玩复测，不能认定漏报已经解决。

后续职责已明确为公共底层和架构，能力覆盖、任务依赖与扩展设计见 [底层补全计划](../foundation-expansion/README.md)。本清单继续保留 0.2 的实际验收状态。

## Work allocation

| 工作 | 负责范围 | 依赖 |
|---|---|---|
| Core | SDK/Core/核心测试 | 稳定生命周期与服务视图接口先协调 |
| Hosting | 新 Hosting 项目、包测试、独立样例 | SDK 清单字段保持兼容 |
| Adapter | Runtime/Adapter/Loader、真实资源事件 | 使用 Core/Hosting 已协调接口 |
| Integration | 构建安装、运行、文档及验收回写 | 所有模块 |
