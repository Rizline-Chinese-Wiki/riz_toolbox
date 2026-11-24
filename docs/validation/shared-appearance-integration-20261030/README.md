# 共享目标及外观模块整合

2026-10-30。新增独立 `SemanticTargetsModule`，将公共目标注册表和修改协调器从菜单颜色发现模块中移出。菜单和游戏外观各自持有自己的 binding、channel 与原生 roots；空注册表继续提供查询服务。

本批独立快照实际通过 **Module 29/29、GameResources 32/32**。另一个独立快照通过 **Game 79/79**，包含具体 controller/information、RunId 和场景代次的关联检查。外观自身的 40 项检查见[独立报告](../game-appearance-run-guard-20261030/2026-10-30_game-appearance-report.md)。这些证据不合并冒充全树或实机验收。

| Evidence | Finding | Path |
|---|---|---|
| `D:/riz-build-cache-next/appearance-module-r2-20261030/test-result.json`；SHA256 `3e0b43016c95d7c2266d26a995731efd442b1c19ef00659801e361152b611dc3` | 两套实际构建和运行均 exit 0；捕获源码前后 hash 一致 | 新模块的声明、依赖、scoped 服务和共享 provider 所有权接线可继续整树冻结验证 |
| 同目录 `Module-tests.log`，29/29 | 无菜单/无活动局时服务可查询；未知构建在构造原生 provider 前拒绝；生命周期 provider 撤回使旧 Result 服务返回 `ScopeExpired`，其他目标仍可读；恢复产生新视图 | 避免把空场景与模块故障混为一谈，保留 provider 的独立代次 |
| 同目录 `GameResources-tests.log`，32/32 | 两 provider 独立刷新和撤回；已结束 provider 不再写同伴；UI 接口用 canonical key 或 alias 都不能覆盖同伴目标 | 共享 registry 不扩大具体 UI 服务的目标范围 |
| `D:/riz-build-cache-next/appearance-run-identity-r1-20261030/test-result.json`；SHA256 `b5073bf1aa6d698c6a97f2acd34ada5da466acc834738c1e93e52416d0feb1ee`，Game 79/79 | 精确同局匹配可接受 Playing/Paused；换 controller、information、run 或场景，以及失去观察/终止/错误线程时不匹配 | 生产外观读写必须消费实际匹配结果，不能只套用生命周期快照的局名 |

## 实现

- `SemanticTargetsModule` 统一注册 `ITargetService` / `IMutationService`，释放顺序为协调器后注册表。
- `MenuResourceModule` 依赖共享能力；`GameResourceService` 只枚举、刷新、恢复和作废自己登记的目标。
- `GameAppearanceModule` 依赖共享目标、协调器及生命周期能力，在固定 Windows x64 构建构造独立外观服务。
- `RizlineAdapter` 将宿主的 `MatchesCurrentGameplayRun` 传给生产外观模块。后续玩法时钟也在接入这一检查，其新代码需单独验证。
- `.sln`、普通构建及完整离线矩阵纳入 `GameAppearance`；当前矩阵为 26 套，SDK 分发验证相应要求当前完整矩阵。

## 复现与范围

保留的 runner 为 `D:/riz-build-cache-next/run-appearance-module-r2-20261030.ps1`。它从已通过 V7 core-r1 的稳定源码基线，逐个捕获声明的 root/外观 overlay，记录 hash，再编译并执行两个实际程序集。若需重跑，先把 runner 的输出目录改为新的绝对 D 盘路径；它拒绝覆盖旧结果。

r1 的 Module 结果为 28/29：新测试错误地预期旧服务抛 `ObjectDisposedException`，而当前结构化 Result 代理已返回 `ScopeExpired`。修正断言后在 r2 重跑；r1 原件保留。随后独立审查还发现 scoped UI 覆盖缺少本 provider 目标检查，r2 已包含实际修复及正反向用例。

本批没有加载、安装或操作真实游戏。它证明声明范围的模块和共享服务行为；没有验收后续 live 改动、真实视觉效果、完整 LOCAL 退出、生产首次准入或全部 B01–B19。最新 V7 候选的 2233 项冻结结果也不自动覆盖这里的新源码。
