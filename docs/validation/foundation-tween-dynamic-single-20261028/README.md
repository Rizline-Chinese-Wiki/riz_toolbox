# Tween 首次动态接入与 Single 事件通道

日期：2026-10-28。本批已冻结、完成统一托管回归和候选打包；当前工作树继续实现下一批退休链，不与本候选混用。完整公共底层 B01–B19 仍未完成，游戏实测按用户安排暂缓。

本批补齐运行中创建的 Tween 在首次真实 Before 就进入来源、子事件、typed onTween 和死亡观察；新 Manager 由当前 owned Tween 参数取得归属。跨 foreign 调用、晚到的日志地址和普通保活引用不能授予 owned 权限。onTween<float> 使用真实 Single ABI、闭泛型类和确切 Play/Execute CALL 返回地址。

## 已取得的证据

| 证据 | 实际结果与边界 |
|---|---|
| `D:/riz-build-cache-next/foundation-single-continuation-native-r3-20261028/native-manifest.json` | `/W4 /WX` 严格构建与实际 PE 导出核验通过；helper SHA256 `ff3be49af9eb55d743a3b183b1dc2e770d3a3ec0ff2dc59b40521a24ea6994eb`。26 operation、57 writes、22 source。未执行游戏。 |
| `D:/riz-build-cache-next/foundation-single-continuation-profile-r2-20261028/profile-verification.json` | 对固定游戏 PE 只读重验 26 个主 RF，并补齐 Single 的 3 个 CHAININFO 续段，函数范围共 647 字节。完整正文、展开信息和 parent tuple 均与编译 profile 相同。 |
| `D:/riz-build-cache-next/foundation-single-continuation-mapped-r3-20261028/fixture-result.json` | 41 个独立映像进程、54,365 个检查通过；Single 末续段 offset 646、展开信息、CHAININFO parent 和 pdata 篡改均拒绝。实际 `DONT_RESOLVE_DLL_REFERENCES`，游戏函数执行数为 0。 |
| `D:/riz-build-cache-next/foundation-single-continuation-bootstrap-r3-20261028/fixture-result.json` | 2,088 检查通过；真实四类 bootstrap ABI、120 次 original、104 次观察，8 个 float 原始位型含 signed zero、Inf、NaN、subnormal。对象与函数均由测试拥有。 |
| `D:/riz-build-cache-next/foundation-single-continuation-opdobby-r3-20261028/fixture-result.json` | 1,546 检查通过；真实 Dobby prepare/commit、四 ABI、92 次观察；不证明游戏首次执行排除或 CLR 实机回调。 |
| `D:/riz-build-cache-next/foundation-single-continuation-admission-r3-20261028/fixture-manifest.json` | 4 个独立 gate 进程、359 检查通过；受控 metadata，不证明最终退出或释放。 |
| `D:/riz-build-cache-next/foundation-dynamic-single-core-r1-20261028/test-result.json` | 冻结源码 Audio/content 953/953、协调器 28/28、对象身份 10/10；含 native cached-pointer 无 Pump 复活负例。源码快照 SHA256 `70b0dce013b07fa0ba4c8dcfe5ea536ddf4a06aef139bfc87d37ba3d002e7782`。 |
| `D:/riz-build-cache-next/foundation-dynamic-single-full-r1-20261028/test-result.json` | 同一冻结源码全部 24 套托管回归 **2063/2063**；Runtime 294、Hooks 203、Audio/content 953。SHA256 `3cc0b838da25a00a5fd8a747f05e0906384f15995a3ab126f8cb4930e8c7142e`。 |
| `D:/riz-build-cache-next/foundation-dynamic-single-release-r1-20261028/managed-build-manifest.json` | 六组件 Release 成功、0 错误；固定生成 profile 的一条既有 nullable warning 保留。SHA256 `02ab97216fb7c5397eb79ba7c331b9ccf332c1218c1bd8abf74161bde02795c5`。 |
| `D:/riz-build-cache-next/foundation-dynamic-single-adoption-r1-20261028/fixture-result.json` | 真实 CLR 采用、两个独立非执行映像进程，525 个检查通过；helper 与冻结托管源码身份一致。 |
| `D:/riz-build-cache-next/foundation-dynamic-single-install-r1-20261028/test-result.json` | **19/19** 临时目录安装/恢复/篡改/只读验证场景通过；只读复制实际游戏输入，真实游戏文件未修改，未执行 native 或游戏。SHA256 `e3e504a7b440b4413b07c01cb1699f9e46c09a0bc531fa4ad2f46c5a9aba3eac`。 |

## 发现与实现

1. Single 的 122 字节主 RF 并非整个函数。此前其余三个 CHAININFO RF 没有纳入发布后校验。本批将 491、28、6 字节续段及各自完整 unwind 固定进 profile，原生 Ready 检查同时核正文、保护、静态 pdata 和 OS RF 身份。独立 PE 验证及包内源码验证重新计算这些事实；入口 operation 行数和写入数维持 26/57。
2. 动态 Tween 在原调用后才补归属会漏掉第一次 Play/Add/Execute。当前实现只接受精确 Before 的保留输入根，绑定 broker 私有 observer、直接物理 parent、listener 当前实际 Invoke 与原生日志水位；发生过未覆盖调用的对象转 Unknown。
3. 新 Manager 不能仅因队列含某个 owned Tween 就取得权限。当前同一 Before 同时验证 receiver 根、已 owned 参数根和 actual Add/Execute；队列中的其他 Tween 仍为诊断保活对象。
4. 死亡凭据的槽位和 epoch 不变，也可能出现 cached native pointer 重新非零。本批在消费凭据时重读实际 lease 并拒绝该情况，无需等待下一次 Pump；拒绝后保留 Unknown 责任和所有根。

## 验证和交付状态

候选为 [foundation-tween-dynamic-single-candidate-r1-20261028](../../../artifacts/foundation-tween-dynamic-single-candidate-r1-20261028/transport-candidate-package-manifest.json)，24 个 payload 与 26 个证据文件，manifest SHA256 `80621f1690547b4fe4918c5750189b0be4885871ae9f1009158debc916f27b31`。独立包内 PE/编译源码/完整 profile 核验及 19 个合成安装场景均已通过；生产准入关闭，未安装真实游戏。失败的第一次 mapped fixture 编译因局部名称遮蔽触发 `/WX`，已修复；失败输出原样保留在 `foundation-single-continuation-mapped-r2-20261028`，不计通过。

非空 listener 的具体效果责任、完整 WorkEnded、联合排空、checked Unregister、各独立持有者的一次根释放尚未交付，`CanRetireWork()` 仍拒绝。全局共享 source-reference hub 与每次 run 的借用必须分开处理，不能按 RootRecords 总数统一 Dispose。真实游戏准入、整局流程和完整 B01–B19 验收仍需后续工作。
