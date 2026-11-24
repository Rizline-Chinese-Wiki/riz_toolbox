# V5 候选包构建与安装验证

当前候选包含 26 个运行文件。Clock 和播放来源各自的原生助手随包交付，示例模组从 Schema 3 的清单资源映射到宿主的 `BepInEx/RizToolbox/mods`。打包器和两套校验器都按来源清单验证这些文件的哈希。[候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-candidate-20261121-r4.zip) · [归档回读](../../../work/e5-production-admission-20261120/managed-current-20261121/archive-r4.json)

| 环节 | 当前证据 | 结果 |
| --- | --- | --- |
| 托管构建 | [Release 构建](D:/riz-m2/e5-current-managed-20261121-r1/managed-build-manifest.json) · [冻结源码回归](D:/riz-m2/e5-current-source-20261121-r1/test-result.json) | 六组件构建成功，0 错误，2 条可空性警告。协调器 28/28、接收者 10/10、音频与内容 1015/1015。 |
| 生成配置 | [错误哈希拒绝](../../../work/e5-production-admission-20261120/managed-current-20261121/profile-rejection.log) · [编译配置回读](../../../work/e5-production-admission-20261120/managed-current-20261121/official-compiled-admission-replay.json) | 正式脚本核对 V5 配置后编译，去除 V4 本机缓存路径。改动配置后在编译前拒绝。 |
| 原生启动证据 | [此前实机快照](../../../work/e5-production-admission-20261120/live-admission/live-native-admission.json) · [五模块文件核验](../../../work/e5-production-admission-20261120/managed-current-20261121/native-build-verification.json) | 当前编译出的 Runtime 校验通过已有的 36/36 写入快照和主线程见证。原生文件图确认 44 条依赖。 |
| V7 采用 | [映像夹具](D:/riz-m2/e5-current-v7-adoption-20261121-r1/fixture-result.json) | 同版托管源码完成两个映像夹具，共 554 项检查。 |
| 包布局 | [候选清单](D:/riz-m2/e5-current-package-20261121-r4/transport-candidate-package-manifest.json) · [独立反例](D:/riz-m2/e5-current-package-negative-20261121-r1/results.json) | 26 个运行文件、31 份验证材料，ZIP 共 58 个成员。独立校验分别拒绝两个助手的缺失和示例程序集被替换。 |
| 隔离安装 | [26 个场景](D:/riz-m2/e5-current-install-fixture-20261121-r1/test-result.json) | 26/26，覆盖首次安装、重装、旧布局升级、回滚、中断恢复、卸载和只读验证。 |
| 源码与归档 | [脚本差异](../../../work/e5-production-admission-20261120/managed-current-20261121/package-source-diff.json) · [封存汇总](../../../work/e5-production-admission-20261120/managed-current-20261121/validation-r4.json) | 核对 1807 项输入中的五个预期脚本变更，并记录候选 ZIP、构建及检查结果的哈希。 |

此前实机的托管日志停在等待首次 Unity 回调。本轮在离线环境校验已保存的快照和当前候选。Unity 首次回调、正式 Fresh、固定 `GetTime` 消费者和实际 Speed/Seek 仍待同一新候选上的实机验收。[此前实机会话日志](../../../work/e5-production-admission-20261120/live-admission/toolbox-live-session.log)
