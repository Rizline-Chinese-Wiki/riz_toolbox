# Unity 启动状态验证

`runtime-startup.json` 分别记录原生观察器和宿主的启动状态。挂接完成后等候 Unity 回调。首个回调进入宿主时写入 Tick 序号，并在宿主构造和首帧执行的边界更新状态。首个 Tick 正常返回且模式允许时，启动摘要才显示可选写入就绪。[状态字段](../../Runtime-modes.md)

| 检查 | 当前结果 |
| --- | --- |
| 改前复现 | 运行 Loader 夹具时，启动状态缺少宿主状态和 Tick 序号。新增断言失败，22/23。 [原始日志](../../../work/e5-startup-readiness-20261121/red-tests.log) |
| Loader | 24/24。覆盖挂接先返回、回调先到、准入失败、构造失败、首帧失败和仅诊断模式。已有宿主正常后，后续回调不会反复写启动文件。 [当前日志](../../../work/e5-startup-readiness-20261121/current-loader-tests.log) |
| Tick 运行时 | 托管 14/14，自有原生通道四个场景共 209 项。 [当前日志](../../../work/e5-startup-readiness-20261121/current-runtime-tick-tests.log) |
| Release 构建 | 解决方案 0 错误、67 条警告。共核对 1808 项输入。 [构建日志](../../../work/e5-startup-readiness-20261121/current-solution-build.log) · [封存](../../../work/e5-startup-readiness-20261121/validation-r1.json) |
| V5 候选 | 冻结源码协调器 28/28、接收者 10/10、Audio/content 1015/1015，通过两个 V7 映像采用场景。六个 Release 组件 0 错误、2 条可空性警告。 [冻结测试](D:/riz-m2/e5-startup-source-20261121-r1/test-result.json) · [托管构建](D:/riz-m2/e5-startup-managed-20261121-r1/managed-build-manifest.json) · [V7 采用](D:/riz-m2/e5-startup-v7-adoption-20261121-r1/fixture-result.json) |
| 归档与安装入口 | 26 个运行文件及 31 份验证材料。58 个 ZIP 成员逐项读回，确认包内 Loader 包含启动状态实现，隔离 `ValidateOnly` 通过。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-startup-candidate-20261121-r1.zip) · [包验证](../../../work/e5-startup-readiness-20261121/candidate-validation-r1.json) |

本批使用 Loader 与原生通道夹具，并在隔离目录校验当前包。真正的 Unity 首帧、正式 Fresh、固定 `GetTime` 消费者和实机 Speed/Seek 仍待同一新候选验收。
