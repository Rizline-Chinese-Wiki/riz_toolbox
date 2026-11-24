# Unity 首帧回调识别验证

原启动观察器只比对方法名、类型名和命名空间。离线夹具先投递另一程序集的同名 `Update`，旧观察器绑定了该方法和线程。独立用例也复现了静态 `Update` 被接受。

Loader 从版本配置传入程序集。启动匹配核对程序集、声明类型、方法元数据身份和实例 `void Update()` 签名，拒绝有参数、泛型及其实例化方法。首帧通过后沿用已绑定的方法指针和线程。

| 检查 | 结果 |
| --- | --- |
| 改前复现 | 托管 14/16。新增两项分别复现同名程序集方法和静态方法误入。 [日志](../../../work/e5-exact-startup-tick-20261121/red-tick.log) |
| 改后托管准入 | 16/16。依次投递错误程序集、带参数重载和不同方法元数据，随后正确回调获得序号 1、2。签名用例也覆盖非 `void`、泛型和空命名空间元数据。 [日志](../../../work/e5-exact-startup-tick-20261121/tick-admission.log) |
| 原生通道与 Loader | 四个自有通道场景共 209 项，Loader 24/24。 [通道](../../../work/e5-exact-startup-tick-20261121/tick-admission.log) · [Loader](../../../work/e5-exact-startup-tick-20261121/loader.log) |
| 固定版本元数据接口 | 缓存映像的 SHA256 与 `steam-25977441` 相同，导出表包含匹配器使用的 15 个接口。 [导出表核对](../../../work/e5-exact-startup-tick-20261121/fixed-image-exports.json) |
| Release 与输入封存 | 解决方案 0 错误、67 条既有警告。验证前后核对 1809 项源码输入和 6 项构建输出。 [构建](../../../work/e5-exact-startup-tick-20261121/solution-release.log) · [封存](../../../work/e5-exact-startup-tick-20261121/validation-r1.json) |
| V5 候选 | 六个组件以当前冻结源码 Release 构建，0 错误、2 条可空性警告。冻结回归 28/28、10/10、1015/1015，两个 V7 映像夹具共 554 项。 [冻结回归](D:/riz-m2/e5-exact-tick-source-20261121-r1/test-result.json) · [构建清单](D:/riz-m2/e5-exact-tick-managed-20261121-r1/managed-build-manifest.json) · [映像验证](D:/riz-m2/e5-exact-tick-v7-adoption-20261121-r1/fixture-result.json) |
| 归档与安装入口 | 58 个 ZIP 成员逐项核对，包内 Runtime 包含首帧匹配器，隔离 `ValidateOnly` 通过。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-exact-tick-candidate-20261121-r1.zip) · [包验证](../../../work/e5-exact-startup-tick-20261121/candidate-validation-r1.json) |

夹具验证了回调识别和顺序。真实游戏中首个 `GameManager.Update` 是否经过该通道，以及正式 Fresh、固定 `GetTime` 消费者和 Speed/Seek，仍需验收。
