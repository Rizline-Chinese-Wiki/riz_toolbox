# Single V7 候选包证据收尾

现有 `foundation-single-v7-candidate-r2-20261029` 的载荷、证据、源码归档及两项测试终态通过独立只读核验。negative **32/32**，synthetic install **26/26**；本次未重跑测试、构建或打包，未改业务代码。核验时间：`2026-10-30T02:13:07+00:00`。

候选绑定冻结托管快照；并发编辑中的当前工作区在观察时有 **11 个文件**不同于该快照，不能把候选称为当前整个工作区的构建。真实游戏运行、真实安装、production admission 与 execution window 均未证明。

## 证据与结论

| Evidence | 实物及 SHA-256 | Finding |
|---|---|---|
| E-001 | `artifacts/foundation-single-v7-candidate-r2-20261029/transport-candidate-package-manifest.json`<br>`09834a070f97fc067f1b1129692a65624fbffc7a9a744413856e31733f003528` | F-001：24 payload / 31 evidence 的 hash、字节数、完整目录清单均符 |
| E-002 | `D:/riz-build-cache-next/single-v7-package-negative-r1-20261029/test-result.json`<br>`da02699c4da845d199a96ad30fac5b758c8aaa69bee8d81c36c77b62affe50ce` | F-001：32/32、62 输入、32 逐案结果及 72 变异保存文件均符 |
| E-003 | `D:/riz-build-cache-next/single-v7-package-install-r1-20261029/test-result.json`<br>`004847d1bafe9a89ca1e52eba013e0c3708e76ec269e9db583c03e38a9f67168` | F-001：26/26、88 输入及 1,231 产物均符，共 1,857,239,837 bytes |
| E-004 | 候选内 `evidence/managed-source-snapshot.json`<br>`0df5f9bef1804a6218daee3412e51532cb48108bdada40c5361eb261522fa2e7` | F-002：冻结快照与编译源一致；当前工作区差异另记 |

F-001/F-002 均为 `validated / info`。P-001：E-001 清单与归档 → E-004 编译快照绑定 → E-002/E-003 输入、保存结果、日志终行与 PASS 名单 → F-001/F-002。逐文件 hash、93 项去重输入来源和当前差异完整旧值/现值见 [package-evidence.json](package-evidence.json)。

## 精确覆盖

- 24 载荷共 5,945,209 bytes，31 证据共 4,460,292 bytes，无缺失或额外文件。helper SHA-256：`30ec8ed7245432ff81490cc346fb13d03dca060bb0ef9575f90e2a3078779bde`。
- transport V5 / profile 2；operation V7，2528 bytes、26 rows、64 writes；Single capability 1，416 bytes、7 spans、7 writes；22 Tween source rows、83 exports。
- 7 份 ZIP 的逐项源码与完整清单均符：production 20、最终 native 94、admission 9、source 14、retirement 14、references 7、managed snapshot 624。最终 native 94 输入/26 产物、managed 659 编译源引用均符。
- mapped 原结果为 182 场景、256,673 checks；Single source byte 53、block **80 个抽样偏移**、protection 8。4096-byte 整块验证不表示穷举 4096 处故障注入。
- 7 个 partial 场景完整原始 Base64 解码 hash 与磁盘原件相符；29 依赖引用、21 个最终构建 OBJ、92 编译源码均符。94 与 92 的差额仅为 build/export 校验脚本。partial 为编译期 seam `RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE`，`productionDLLInvoked=false`。
- negative 的隔离候选恢复后 56 文件与源候选完全相同；30 个拒绝案例夹在两次成功验证之间。install 实际覆盖隔离 fake-game 的安装、重装、升级、拒绝、事务回滚、中断恢复与卸载；worker `54264` 的 marker、stopped、terminalConfirmed 一致。日志 PASS 名单及终行分别精确对应 32 与 26 项。

## 冻结快照与当前工作区

624 份冻结 `snapshotFiles`、源码 ZIP 和 659 实际编译源路径全部匹配。E-004 顶层 `inputs` 为捕获时工作区引用，观察时下列文件当前 hash 不同。此清单只描述观察时状态；当前并发编辑可继续产生变化。这些文件未被本次收尾修改，也未混入候选。

- `src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.Retirement.cs`
- `src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.SingleDispatch.cs`
- `src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.TypedEvents.cs`
- `src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.TypedEvents.cs`
- `src/RizToolbox.Adapter/GameResourceService.cs`
- `src/RizToolbox.Adapter/Modules/BuiltinModules.cs`
- `src/RizToolbox.Adapter/RizlineAdapter.cs`
- `src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventBridge.cs`
- `src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventPhysicalParent.cs`
- `tests/RizToolbox.GameResources.Tests/Program.cs`
- `tests/RizToolbox.SingleEvent.Tests/Program.cs`

## 范围和历史

两项 package 测试只操作隔离目录与磁盘 PE，`nativeExecuted=false`、`gameExecuted=false`、`installed=false`、`realGameMutated=false`、`productionAdmissionGranted=false`。synthetic install 的真实文件写入发生在测试 fake-game；其 `installed=false` 表示无真实游戏安装。上游 mapped/CLR/owned fixture 的执行范围保留在原证据中，不能推导真实游戏回调或生产窗口完成。本批不含 N1 structured-errors 历史 24/2139。

r1 保留为前次失败尝试；交接记录为 Common 源数旧门 12 调整到 14 后 r2 成功。当前门为 14，r2 completion log 与本核验一致；空的 r1 pack log 无法独立证明退出码，r1 不作为最终通过候选。未发现需要修包或补跑的实质缺口。

## 只读复核入口

以下命令核对三个核心证据的当前 hash，不运行测试。全量复核按 JSON 候选清单、inputSourceHashes、ZIP 清单及原 test-result.artifacts 逐项比较 hash/bytes；异机需映射绝对缓存与只读游戏来源路径。

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath 'C:/Users/71957/Desktop/riz_toolbox/artifacts/foundation-single-v7-candidate-r2-20261029/transport-candidate-package-manifest.json'
Get-FileHash -Algorithm SHA256 -LiteralPath 'D:/riz-build-cache-next/single-v7-package-negative-r1-20261029/test-result.json'
Get-FileHash -Algorithm SHA256 -LiteralPath 'D:/riz-build-cache-next/single-v7-package-install-r1-20261029/test-result.json'
```
