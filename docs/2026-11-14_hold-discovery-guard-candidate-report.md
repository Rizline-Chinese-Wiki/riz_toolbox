# Hold 对象发现：隔离候选验证

实际 HoldSourceCapture 的 controller 发现路径已接入逐export撤回检查，并在回调撤回前记录已返回的Type、数组及成员引用的根责任。最新939输入候选完成本批离线验证，尚未合主；主图928的原producer main-r3长回归仍在运行。完整SDK、B01–B19和真实Speed/Seek未完成。

| 证据 | 支持的行为 | 实现路径 |
|---|---|---|
| [最新939图与66个自然步骤](../work/hold-discovery-guard-next-20261114/candidate-evidence-r2/run/result.json) | discovery14+既有Hold48，共62项Judgements；Runtime375另列，全部通过 | [实际Backend发现路径](../work/hold-discovery-guard-next-20261114/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.HoldLifecycle.cs) |
| [冻结的Runtime实现](../work/hold-discovery-guard-next-20261114/candidate-evidence-r2/source/src/RizToolbox.Runtime/NativeRuntime.HoldDiscovery.cs) | canonical Type、精确Resources(Type)→UnityEngine.Object[]、完整rooted数组 | [Runtime helper](../work/hold-discovery-guard-next-20261114/src/RizToolbox.Runtime/NativeRuntime.HoldDiscovery.cs) |
| [共享调用核心变更](../work/hold-discovery-guard-next-20261114/candidate-evidence-r2/source/src/RizToolbox.Runtime/NativeRuntime.cs) | 新reference producer显式选择输出移交后再检查；其它调用保持默认顺序，异常方法名读取受guard保护 | [源码复核](../work/hold-discovery-guard-next-20261114/candidate-evidence-r2/controls/source-review.md) |
| [故障与边界用例](../work/hold-discovery-guard-next-20261114/candidate-evidence-r2/source/tests/RizToolbox.Judgements.Tests/HoldLifecycle/HoldLifecycleChecks.Discovery.cs) | 每种已观察metadata export撤回、转移/拒收/抛错、1024槽及1024不同对象、未知释放、原错误保留 | [原root退出](../work/hold-discovery-guard-next-20261114/candidate-evidence-r2/controls/tests-r2-root-exit.json) |

旧路径在多export的Type/Method/invoke/array helper前后检查source，中途仍可能在撤回后继续读取。新路径将Type对象、实际返回数组和成员引用先取得临时根，再完成明确的转移或保留责任，最后检查source。每个数组成员使用一个可终结的publication call，避免1024槽累计耗尽单个call的129条root记录。null与重复槽保持原顺序；重复临时根单次清理。

Backend继续在原有1026容量的HoldRoots中持有Type、数组和最多1024个不同成员，按实际已观察run/controller关联选中对象。扫描清理失败会附加精确HoldDiscoveryCleanupOwner/Failure并保留原操作异常；未知释放不重试。旧liveness检查的故障触发点现在明确等到实际discovery调用之后，避免把对op_Implicit的覆盖误移到新的discovery元数据检查。

固定dump SHA256为`092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb`，Resources第112179行声明返回UnityEngine.Object[]。本批仅校正隔离Hold夹具旧System.Object占位；没有打开新的游戏二进制。

当前tests-r2 controller36992/root27105实际exit0/chunk2c5048，66自然步骤全部成功：六组件/Judgements/Runtime builds W0/E0，新discovery14+alive6/guard10/owner15/Hold8/Stop9=62项Judgements，Runtime375/375另记。939冻结输入及当前overlay未变。较早r1的13项版本虽过，仍单独保留其源码和结果；加入1024个不同对象的实际Backend边界后，r2重新完整运行，没有拼接旧结果验收新源码。

归档2073文件、53,084,389字节；冻结原root75110实际0/chunk1c9dc2，直接归档检查器root93201实际0/chunk50de31，只读归档。summary SHA256：`0c9a8d8c302cea6cf2799f7967bbe0d500a85b0e6f575f4a30c39d937e719401`；当前root receipt SHA256：`3c6c7bdf45ce990e60f5a6b4d739bfbb77e3af38497bb3251cac90fd228ecac5`。Root源码复核，无独立代理执行PASS。

六个最新939合主控制已准备、语法检查及hash复核通过，尚未执行。父producer928接受后直接准备18文件（17 C#+Build.ps1），保留原字节备份，一次合入全部Hold改动，跳过未运行的933/936中间集成。新主图计划六Release/solution和相关检查共67自然步骤（62项Judgements、Runtime375），另行归档验收。父renderer结果不充作939完整SDK回归。

以上不证明全局factory/origin、engine detach/release、scheduler terminal、独立native owner死亡或真实模块/body准入；FullSeek、HoldIdle、FullCoverage、Startup仍false。未安装/启动游戏、操作游戏UI/账号、修改永久环境、提交Git或覆盖旧ZIP；旧881输入预览包未更新。
