# 原生初始化异常与发布预留

2026-10-23。**1216/1216、25 套完整构建通过，新包仅暂存，实际游戏安装仍 1021。** Runtime131、Hooks99、AudioContent375；相较 [1210 批](../foundation-initialization-isolation-20261023/README.md)新增 Hooks3、Audio3 组检查，并加强原有 Runtime 原生 ABI 用例。完整增量构建没有编译诊断行；这不表示全新系统 clean build 无警告。独立 runner 引用同包 Runtime/SDK/helper 再次通过，编译 0 warning/error。本轮未安装、未操作或重启游戏。

本批补上两处具体缺口：生产 Start/getter 的 original 经 typed native helper 调用，原生异常展开不再经过托管 original 委托；同一 run record 在 native 尝试前留好 controller/progress 身份和固定发布槽，采用图时完成一次后发布。真实 LOCAL 启动协调器尚未接通，`RequestPlay` 保持 OperationUnsupported。

范围见 [scope](scope.md)，有限源码与文件复核见 [independent-review](independent-review.md)，Evidence→Finding→Path 见 [正式报告](2026-10-23_底层-原生初始化异常与发布预留-report.md)。

## 已实现及其证据

| 变化 | 当前实现与实测范围 | 限界 |
|---|---|---|
| Start/getter native original | [Runtime binding](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.LocalInitializationIsolation.cs)使用 Prepare → native Configure → Commit；[typed helper](../../../native/RizToolbox.JudgeContext/InitializationContext.cpp)以 native `__try/__finally` 配对 Exit；managed Enter/Exit 异常被边界包含 | 只覆盖这两个 exact ABI；不承诺其它 original 或任意外部 detour 的展开 |
| 原异常保真及责任 | 真实 C++ caller → Dobby → production broker/helper → C++ original，ordinary/LOCAL × Start/getter × SEH/C++ 共 8 条路径；核对 code/typed payload、线程、原调用一次、After 只在真实正常返回时运行、LOCAL sticky fault、同线程后续 ordinary 与 in-flight=0 | Runtime suite 中是同一个加强用例，不额外算 8 个 suite case；不是目标游戏原调用实测 |
| 独立 native helper | [1747 断言 fixture](native-build/initialization-context/fixture-result.json)验证并发一次配置、typed Start/getter、worker、原 SEH/C++ 保真、Enter/Exit callback containment，以及 Exit 故障不覆盖原异常 | helper fixture 与 production broker fixture 分别证明不同层；1747 不并入 1216 |
| I04 身份及发布存储 | [run record](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunTerminationBoundaryState.cs)在 ReservePending 及 legacy registration 的首个 retain 前创建两枚 token，使用固定 32-slot 数组；token 只在发布前补全，release 发布后永不改写 | 保证这段发布存储预留；不是全系统 native 后零 managed 分配，也不代表全部 I04/历史临时 root 重构完成 |
| 发布稳定性 | [3 组新增回归](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalRunPublicationReservationRegression.cs)读取预留存储；采用中 worker 不见半成品；成功后仍用原 token/数组；检验 value equality/hash、拒绝重写及 progress slot 复用时旧 token 不变 | 新 fixture 没有直接覆盖 controller map 的 withdraw→redrain→slot reuse；不能由 progress slot 测试泛化这一点 |
| 权限及排空 | 已认领 Start/getter 可隔离 Mode+Tooltip，Play 仅 Mode；普通子 scope 遮蔽旧权限；binding 读数包含 native 最外层活动和 managed 工作，未知仍阻止 ready/重绑 | 读数是已进入活动，不能证明未来 callback 不存在；共享 Mode 聚合可能保守重复计数 |

Play、Progress.Update、Mode/Tooltip/Instantaneous/EndRange 的 original 仍走原有 managed 调用路径。本批没有把新的 Start/getter unwind 证明套用于它们。早期 worker/EDX poison/递归/null-abort 检查保留；[18-call native-only probe](native-build/initialization-abi/native-probe-result.json)仍明确 `managedTransportVerified=false`，不能代替 production fixture。

## 冻结与复核

- [build-summary.json](build-summary.json)：1216/1216、25 套，Runtime131/Hooks99/Audio375；[build.log](build.log) SHA-256 `bfaaa24dcefb39afc94917d8d07eed2d5a2321a2265ca315e2613fcb9a0e18bb`。定向 [Audio](audio-targeted-first.log)、[Hooks](hooks-targeted-first.log)、[Runtime](runtime-targeted-first.log) 分别通过 375/99/131。
- [source-inputs.zip](source-inputs.zip)共 371 项，SHA-256 `f3863e3dc70751f6bcfcb85aef8cda1534f0c2875826580ea16ab403c27d57f8`；[source-manifest](source-manifest.json) SHA-256 `7b50b58722d4c4a48e232114efa0348aa1f0f67b250ab0713d92cc755244a901`。[归档校验](source-archive-check.json)及[当前输入核对](source-integrity-check.json)分别固定 ZIP 和冻结时工作树。
- [staged-package.zip](staged-package.zip)含声明的 18 项，SHA-256 `61c40626b73e4c97cc5956dd7ef54af79e1ba3d592d149319cb5434990d2577a`；[package check](package-archive-check.json)、[staging check](staging-check.json)核对当前暂存文件，`installed=false`。它不包含完整 BepInEx/工具链/依赖安装包。
- [独立 runner](independent-native-abi/README.md)的 [7 项输入快照](independent-native-abi/evidence-integrity.json)与实际执行 SHA 均核对；[package-match](independent-native-abi/package-match.json)连接同包 Runtime/SDK/helper、冻结测试源和 native fixture。Runtime SHA `4674418feea8e0e3dccf91eb02d175fb38fc8e88eb34c9d144a1d86f3ce155a1`，helper SHA `805be452ded4333d4ae4986d1428d1b6ee0ad3356ef5153142aa7eba40b1fccc`。
- [旧安装核对](previous-installation-check.json)保持 1021 的 18 个文件及旧 326 项源码归档；[原游戏核对](original-game-check.json)保持 exe、GameAssembly、global-metadata 三 SHA。新旧批次不混作同一安装。

这些结果支持有限的源码/发布输入一致性及离线行为，不是 clean-room 复现、全部 B19 或游戏验收。

可审阅的 [changes-from-1210.diff](changes-from-1210.diff) 与 [变更清单](changes-from-1210.json)比较两份已核 SHA 的冻结 source ZIP：364→371 个输入，21 个实际新增/修改文件；不以当前 Git HEAD 或未冻结工作树代替历史基线。

## 复现命令

完整构建会改写本地新暂存输出；不安装或操作游戏。使用已配置的 VS 2019 x64 C++/MASM、PowerShell 7、.NET 和现有依赖：

```powershell
Set-Location 'C:\Users\71957\Desktop\riz_toolbox'
& .\scripts\build.ps1 -NativeOutputDirectory 'D:\RizToolbox-build-cache\initialization-native-unwind-final-20261023'
```

只重跑同包 production ABI 用例，使用自己的新 runner 目录；没有项目引用，不改生产项目 bin/obj。脚本拒绝已存在的输出目录，因此命令用新 GUID 后缀：

```powershell
Set-Location 'C:\Users\71957\Desktop\riz_toolbox'
& .\scripts\Test-InitializationAbiFixture.ps1 `
  -RuntimeAssembly 'C:\Users\71957\Desktop\riz_toolbox\artifacts\package\BepInEx\plugins\RizToolbox\RizToolbox.Runtime.dll' `
  -NativeDirectory 'D:\RizToolbox-build-cache\initialization-native-unwind-final-20261023\initialization-abi' `
  -NativeHelperPath 'D:\RizToolbox-build-cache\initialization-native-unwind-final-20261023\RizToolbox.JudgeContext.dll' `
  -DobbyPath 'C:\Users\71957\Desktop\riz_toolbox\.tools\bepinex-789\BepInEx\core\dobby.dll' `
  -OutputDirectory ('D:\RizToolbox-build-cache\initialization-unwind-independent-recheck-20261023-' + [guid]::NewGuid().ToString('N')) `
  -SkipNativeBuild
```

执行前先核对以上输入与本批 SHA。后续开发改变工作树或 artifacts 时，应使用冻结 ZIP/runner 输入理解历史结果，不要求旧 manifest 证明新源码未变。

## 完整底层仍需继续的部分

源码搜索尚无 Loader/模块实际调用 bridge Create、handler 构造或 LaunchReservation.Reserve。接下来仍需真实 launch/activation 协调器，Awake/settings/six-globals 归属及条件恢复、commands/result view、Retry/Back、active 消费者结束与退休，再以同包完成游戏普通流程和 LOCAL 连续会话对照。全部范围继续按 [B01–B19 SPEC](../../../.docs/foundation-expansion/SPEC.md) 与 [全范围剩余审计](../foundation-local-termination-20261023/foundation-remaining-audit.md) 推进；本批不新增 B 项勾选。
