# Kernel 包安装补充：有限审查

2026-10-25。**本次三处补充修改未发现新实质问题，正式安全测试16项、恢复测试13项均通过。** root 实际观察 safety session 66373、recovery session 67106 各终态0；本复核已读取归档终态、结果、合成目录和源码覆盖原件。安装器允许的精确包集合补入 Kernel，测试覆盖其安装/升级/拒绝覆盖/卸载及实际中断恢复。

这是另一个代理对补充改动的只读源码审查，没有构建、执行脚本、启动子进程、操作游戏或修改输入。[inputs-before.json](inputs-before.json)的7项当前哈希逐项一致。旧 session23528 的599源码/23包和全部原件仍属于历史构建；三项预期修改单独记在本补充批，不改写历史“当时未漂移”的记录，也不称旧599源码当前仍全部一致。

## 实际差异

独立从[core源ZIP](../foundation-engine-kernel-20261025/source-inputs.zip)提取旧脚本，与当前文件比较：

| 脚本 | 范围 | 补充冻结 SHA256 |
| --- | --- | --- |
| [Install.ps1](../../../scripts/Install.ps1) | 1 hunk，+1/-0 | `ab401aeb41aaade1f500a6ea02f414d255d14c13dce9da3443b51ac28c607ac9` |
| [Test-InstallSafety.ps1](../../../scripts/Test-InstallSafety.ps1) | 3 hunks，+26/-0 | `0f6d1c902a2a5e0026a817fb5245c81f0c77eab08ea66897d87ad6d5216aa9c7` |
| [Test-InstallRecovery.ps1](../../../scripts/Test-InstallRecovery.ps1) | 2 hunks，+13/-1 | `ee918aec30efe5b8e7abaeaf1fa75ad92a195b96c864e07a37914b8ebe751307` |

Install 只将 `BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll` 加入 expectedPaths。schema2、精确总数23、unique总数、逐路径精确白名单及源文件hash验证均保留；没有把“允许新文件”扩成接受任意插件或绕过所属路径。Install.Common 的全plan先验证、再建立备份/事务、最后写入也没有改动。

Safety在当前实际23项包上安装到 fake-game：首次除了全文件hash遍历，还要求Kernel与package的hash相同且ownership恰1行；重复安装不重复所属行。旧布局模拟通过移除Kernel实体和其所属行，再运行同一实际安装器补入，检查恰1行和原包字节。修改Kernel后update必须报Modified/unowned且保留修改字节，随后恢复原件继续测试。卸载显式要求未改Kernel已删除，同时继续保留修改的SDK/settings与用户生成数据。它没有启动Rizline或调用Kernel entry，文件安装验收不授生产采用能力。

Recovery的Package()现在生成与实际安装器相同的23个路径，补齐Kernel及早先已要求的winhttp、版本文件和两个许可证文件；payload全是合成文本，不宣称运行真实DLL。此前只向`.tools`放winhttp/版本不能满足package合同的问题已消除。

其路径顺序刻意将winhttp排在SDK之前，并写明只测试controlled恢复顺序，不冒充Build.ps1的production排序。因此杀在SDK源复制之前时，winhttp已真实替换，现有检查有实际含义。中断rollback不再硬编码backup index2：从真实journal挑选previous存在、current等于新hash且新旧不同的实际替换行，以该对象在pending里的位置派生备份路径。回滚只在current确为新字节时读取这项备份，故受控子进程会到达所选复制点，不能把从未写过的条目当作已中断恢复。

## 当前范围

[terminal-observations.json](terminal-observations.json)记录 root 对66373/67106两个实际终态0的观察；本复核没有自行持有或轮询这些执行会话。[Safety结果](safety-results.json)实际为16项，其中Kernel首次所属、旧布局新增和修改后拒绝覆盖均有独立结果；[Recovery结果](recovery/results.json)实际为13/13，各行passed。两个run原件也记录0，不能只用“通过”汇总代替终态。修前 [before-fix-run.json](before-fix-run.json)保留实际退出1，[红日志](before-fix-safety.log)中可见 Invalid package manifest；这是真实补充缺陷，不是合成的回放或被覆盖的成功记录。

独立读取 [source-overlay-manifest.json](source-overlay-manifest.json)和 core599清单：恰上述三项before/after不同，其余596项路径/hash相同；7个本次冻结输入当前仍全部匹配。再逐项读取 [final-source-inputs.zip](final-source-inputs.zip)599条与 [source-final-manifest.json](source-final-manifest.json)，以及当前599源码，全部hash一致。组合源码ZIP SHA256为 `45a2000d5c1ca81d8404bad649304dcd2ffde7767d84bda66868bfd20b2f4d28`；这是core23528完整构建加三脚本补充验证的组合归档，**没有第二次完整构建**。

[package-check.json](package-check.json)23项与core23528包清单相同，本次复核还从当前 artifacts/package逐项重算hash吻合。归档 [fake-game末态](raw/safety-fixture/fake-game/BepInEx/RizToolbox/installation.json)所属和pending均为空，Kernel实体已移除而生成的user-data仍保留，和末次正常卸载的限定结论一致。Safety归档results与顶层16项结果相同；恢复原始合成目录保留在raw/recovery-fixtures，没有将合成文本DLL当成实际Kernel执行证据。

[真实游戏保全记录](real-game-preservation-check.json)的旧安装18项与原游戏4项均expected=actual，realGameModified/realGameExecuted为false；这部分是root的实际哈希记录，本复核只核归档字段一致，没有新增真实安装或游戏调用。生产采用状态仍不因文件包安装测试而改变。

恢复矩阵针对受控包顺序的事务机制，实际包安装/旧布局升级由Safety补充；两者不会自动证明所有production排列下的每一个中断点，也没有在真实游戏目录安装。

本补充没有重建或修改23项包字节，没有重新运行25套完整构建，不改Kernel原生实现、早期loader绑定、系统WinHTTP转发或V4发布。`productionKernelAdopted=false`、`kernelEarlyLoaderChainVerified=false`、`EngineCancelled=false`，无游戏执行。
