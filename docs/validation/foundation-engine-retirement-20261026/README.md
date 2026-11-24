# 逐记录回收、原 factory 释放与 Stop 节点关联

2026-10-26，K 批受控回归已通过，完整游戏底层仍未完成。

本批解除“任一协程未结束就挡住全部记录回收”的限制，已证明终结的记录可以单独撤回映射、重新排空并释放自己的观察根。初始化 Delay 与 WaitUntil 可分别回收。生产许可必须是实际协调器唯一签发的实例，每个根释放前后重新检查全局引擎状态；释放失败不重试。

Controller 五对象的真实独立死亡来源可以被冻结。原 factory 的八个根新增 AfterClose 正向释放路径，然后才能释放参数 binding 的两个根；冻结来源的五个独立观察根仍保留。此接口不生成“所有消费者已结束”的证明，生产全消费者 verifier 尚未完成，整局仍不会被标记为已退休。

六类 Stop 请求新增与真实打开的 Detach scope 的精确代次关联，记录 request/parent/root、Start 序号、node/generation 与 tracker 身份。foreign Stop 使用物理 mask；foreign worker 不读取 provider，owned worker 保留 Unknown。句柄在 Start 返回后的合法确认可在同帧 Stop 关联时刷新，无须等下一帧。关联只产生候选，`EngineCancelled=false` 和取消 Pending 保持。

| 验证 | 实际结果 | 范围 |
|---|---|---|
| 最终 r3 构建 | root session 88038，退出 0；0 错误、17 条测试夹具警告 | Runtime、Adapter 及 AudioContent 测试依赖；含 CS8602/CS0649，非全构建零警告 |
| 完整 Audio/content | 698/698 项通过 | 本轮新增 38 项：逐记录 14、factory 11、取消关联 13；故意保留 Unknown 的组使用既有独立进程隔离 |
| 输入固定性 | 443 份 src/tests 源输入及 3 个原生夹具前后 SHA 不变 | 447 项归档另含 3 个根配置和 r5 生成指纹文件；不宣称完整传递工具链可复现 |
| 生产与游戏 | 未安装、未执行；没有新增原生入口 | 受控回归不能证明 Unity、GC、scheduler、async 或真实游戏取消完成 |

原件：[evidence-index.json](C:/Users/71957/riz-build-cache/engine-record-retirement-r3-20261026/evidence-index.json)，SHA256 `f7b91db7c6f16ac156d7278a79cf7ce1901278ae6faa7543d4e1950bbc8c8f38`；[源码归档](C:/Users/71957/riz-build-cache/engine-record-retirement-r3-20261026/managed-project-sources.zip)，SHA256 `06f0db4c8f546110651f875a58b039a85735c2dffdb90e5e9128bf8ccca3002f`。

失败 r1 的 696/697 与随后 r2 的 697/697 原件保留。首个失败源于测试夹具为 Void0 StopAll 填入参数；生产 ABI 校验没有放宽。r3 新增同帧句柄确认场景后得到最终 698/698。旧 r5 候选包没有包含这些后续源码，本批没有重新冒称其静态结果或安装结果。

下一步按[原生完成施工表](../foundation-engine-production-20261025/next-native-completion.md)补真实 Start 内联回执、scheduler 登记/消费/删除及 wait 发布/撤销和调用责任，再实现取消完成、全消费者 verifier 与剩余资源退休。生产 admission 和 `RequestPlay` 继续关闭。全目标验收见[清单](../../../.docs/foundation-expansion/CHECKLIST.md)。

[本批范围](scope.md) · [正式阶段报告](../../2026-10-26_底层-逐记录退休与取消关联-report.md) · [一页进度](../../../.docs/PROGRESS.md)
