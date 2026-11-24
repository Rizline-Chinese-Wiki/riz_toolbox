# 菜单输入锁与相机交接

2026-10-23。承接 [1298冻结批](../foundation-judge-drain-20261023/README.md)，**1315/1315、25套完整离线检查通过，已冻结暂存未安装。** Runtime **137**、Hooks **117**、AudioContent **450**，新增17组。[范围](scope.md)明确本批未完成的消费者退休部分。

## 生产接线

[菜单账本](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalMenuLedger.cs)和[native backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalMenuBackend.cs)持有GameManager、两组件、两GO和两MultiLock的7份独立root。实际getter、字段、构建SHA、声明方法和ABI均验证后，观察SetActive/Lock/Unlock/UnlockAll；只调用本局仍有权释放的Lock/Unlock，不直接写计数或盲目清锁。

[启动协调器](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)在实际setter后、activation前捕获/开始菜单交接。真实首次场景完成后释放自己的一份normal信用，保留back信用；等待所有相关观察配对并重新验证菜单归属后才继续初始化commit。构造器增加同一broker依赖；Loader仍没有完整生产构造点，用户播放入口继续关闭。

返回恢复只接受实际同局launch transition、registry/session和return owner。它保留其他调用者的锁和相机写入：即使计数或activeSelf又回到旧值，也不能据此找回已撤销权限；后继组件、已销毁对象均跳过恢复。成功后只释放自己的观察根，不签发所有run消费者结束。

## 观察与验证

[标量probe](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.BoundaryProbe.cs)在worker和被抑制重入中仍成对记录，回调仅可操作托管身份/计数。进入时捕获的不可变probe数组在逻辑解绑后仍退出配对，调用中不创建新probe数组。独立early-retained void入口给菜单读前置状态；原观察者的Before→retain顺序继续保留。

[13组菜单检查](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalMenuLedgerRegression.cs)覆盖完整显式信用/恢复序列、既有锁、初始停用、SetActive/锁ABA、平衡外来活动、后继/销毁对象、worker/重入、异常不重试、篡改状态、线程/重复阶段、root未知释放和commit前等待/核对。[4组probe检查](../../../tests/RizToolbox.Hooks.Tests/BoundaryProbeTests.cs)覆盖精确准入、前后顺序、全线程/重入及解绑/fault。

[静态证据](menu-evidence.json)、[282指令](menu-disassembly.txt)、[metadata节选](menu-metadata.txt)由[有界脚本](inspect-menu.py)读取固定GameAssembly的10个范围。MultiLock三个方法的原子增减/清零及TouchBlocker(false)选normal的尾调用均可核对；不加载/执行样本。

本批不证明完整活动退休。getter隐式信用/延迟回调、controller/音频失效、globals/bundle/coordinator释放、Retry/Return完整命令与Loader组合仍缺。新四入口只受控engine加静态验证，未实机；不能继承1298外层Judge fixture为它们签发Dobby/原函数unwind结论。旧安装仍1021，实测延后，无新B项勾选。

## 构建与冻结记录

[完整日志](build.log) / [摘要](build-summary.json)对应最终1315构建，session37899 exit0；此前session58971的[1314过程日志](build-before-commit-validation.log)只记录追加commit核对前的状态，没有冻结1314包。最终完整日志有15条编译warning：14条共享FakeNative CS0649及1条既有结果sink测试CS8602，无编译错误。native helper fixture31261/61358、初始化helper1747和native-only probe18单独记账，不加入1315；完整Runtime继续通过旧生产Start/getter和Judge Dobby fixture，未为本批菜单另跑独立runner。

[Hooks首轮](hooks-targeted-first.log)缺SDK using，补后[第二轮](hooks-targeted-second.log)110/113暴露3组旧Before/retain次序回归。新增单独early-retained通道后恢复旧契约，[第三轮](hooks-targeted-third.log)117/117；没有修改旧断言来迁就实现。[Audio首轮](audio-targeted-first.log)使用net6缺少的HashData(Stream)重载，改用已有SHA256.ComputeHash后[第二轮](audio-targeted-second.log)449/449。随后增加commit前核对的1组检查，最终完整450/450。

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip) / [核对](source-archive-check.json) | 395项，3,992,828原始字节 | `daf55402f51cbb8c18ae3411fbe1b47aa583cedbacd737b4c47e0ba836f557b9` |
| [源码manifest](source-manifest.json) | 395项 | `a40db7985da92bcca0702e4496f7658ff625a6b5eb108bec9b77ba773e2483f1` |
| [暂存ZIP](staged-package.zip) / [核对](package-archive-check.json) | 18项，3,033,576原始字节 | `7ac964a0e2c1354817bed72719705863153f9f10722345984c1100ce1e8c03e2` |
| [完整日志](build.log) | exit0 | `17303c1adbf5fda4dfb0e79322a300aa69681adcc696f6841006c56ea3a2b467` |

[暂存](staging-check.json)/[源一致性](source-integrity-check.json)核对通过；[旧1021安装](previous-installation-check.json)18项和旧326源归档匹配，[原游戏](original-game-check.json)三文件未改。[新旧差分](changes-from-1298.diff)/[清单](changes-from-1298.json)为5新增、7修改、0删除；[静态再核对](menu-evidence-check.json)覆盖10范围/282指令和三输入hash。复核边界见[主代理自查](review.md)。

复现完整离线构建可在仓库根运行：

```powershell
pwsh -NoProfile -File scripts/build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/menu-handoff-reproduce-20261023'
```

此命令不安装或启动游戏；不同机器/冷缓存复现仍属B19未完成矩阵。
