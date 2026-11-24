# 私有原生 pitch 写入与不可变后继凭据

当前主目录已合入writer、生命周期、rooted/API读回、宿主run票及S4唯一Clock；最新入口见[Clock联合报告](2026-10-31_stock-clock-bootstrap-report.md)。下文301／17exports和“未合入”的措辞仅描述该历史冻结增量，不验收后续源码；完整provider、跨run、lower writer与production仍未完成。

2026-10-31，`flavor=null`。本项在独立D staging完成执行层实现，**未合入生产工作树**。用户授权范围为离线底层开发、自有fixture和副本映像验证；没有安装/启动游戏、操作游戏UI/账号或调用游戏方法。完整七根/DSP/readback、provider、Clock、跨局与生产准入仍未完成。

当前交付结果：`D:/riz-build-cache-next/playback-stock-writer-stage-r1-20261031/completion-r2.json`，SHA `74c647599e8472a962c2af8317794b7eb004111466af8d5af2c3f7c5a230ed18`。r2只修正diff的换行保留，功能与执行证据未变。八个进程301检查全部实际退出0，其中六个writer进程119项、两个S2定向回归进程182项。当前托管构建0 warning/0 error，5组PE/PDB与457源文档匹配；新独立source helper有17个可执行导出，1组native PE/PDB、2个COFF C13对象及真实ASM CALL/展开元数据匹配。该helper只用于本项，不是正式JudgeContext V7。

## 已实现

新writer V1子协议为224B，已有source frame/info仍160/136B。native保留once-only reservation/nonce、真实root sequence/token、可选确切predecessor与expected source/writer/schedule fences。只有独立ASM唯一CALLsite进入同一detoured SetPitch，实际native current scope中的caller/source/MethodInfo/原bits/parent及frame sequence/token完全匹配时，才消费reservation。managed同时核group自己的bridge与其登记的确切Observation/Returned引用；C#意图bool、复制frame或当前值相等不能签writer attempt。

旧S2 execution receipt的fence不改写。真实writer Before立即使旧票失效；正常Returned、native finally、没有foreign contention且post fences精确成立后，另发行opaque continuation。它保留原root/leaf的责任，并派生绑定新continuation的schedule witness，原doublebits来自当次实际PlayScheduled。私有predecessor链允许继续写入并产生严格增加的post fence；旧writer和复制票不能复活。

实际正向在同一进程只执行9次Prepare/Commit：Pause后native写2→3→2；Replay的原body确实reset1并经过实际schedule，然后在root Returned、outer caller返回前native独立restore2；Resume亦如此，actual schedule为100.25而post-origin为96。最终actual pitch readback=2，总计7次原生setter调用，没有第二次物理安装。

外部同值static alias、实际ABA、同值set_clip和callback期间额外同值setter会推进真实fence或标记native contention，不能取得或保留current continuation。普通逻辑边界保留健康physical group；setter实际写入后SEH则保留写入责任并按原规则撤物理组，不签成功票。producer Dispose不卸载组，observations按实际finally journal释放。

## 原件和复现

所有目录均在 `D:/riz-build-cache-next/playback-stock-writer-stage-r1-20261031/`：

| Evidence | 实际原件 | 范围 |
|---|---|---|
| E-WRITER | `checks-r1/test-result.json`及每模式stdout/stderr/exit | writer-normal、foreign、ABA、alias、source、error，另S2 normal/copies；八进程301项 |
| E-MANAGED | `managed-build-r4/`、`symbols-r4/test-result.json` | 实际build0，5PE/PDB、457文档；符号子进程实际exit0 |
| E-NATIVE | `native-build-r1/`、`native-proof-r1/test-result.json` | actual C++/ASM build0，17 EAT、1PE/PDB、2COFF，独立CALLsite与ALLOC28展开 |
| E-SOURCE | `handoff-r2/changes.patch`、`handoff-r1/changed-files.json`、`handoff-r1/implemented-source-snapshot.zip` | 基于已冻结S2的14文件增量；native CPP/H按当前r14原文件比较；未合repo。r2 patch SHA `d657dbee058afec8210607cbe9a6a3fddfeac1e8f7f67d662055d24c58c3975a` |
| E-SEAL | `handoff-r1/verification-index.json`、`completion-r1.json` | 879文件逐哈希回核、各执行范围分列；不把旧原生矩阵并入检查数 |

原生构建命令保存在`native-build-r1/build.cmd`。托管专项可用staging `source/scripts/Test-PlaybackStockWriter.ps1`，参数是当前managed-build-r4的test DLL、原fixture r14、SingleReturn r4、新native-build-r1 source helper与固定Dobby，输出必须指定全新D目录。`Seal-WriterStage.py`保留完整执行输入与源码来源，拒绝覆盖原handoff。

原编译访问级别错误、一次未实际启动fixture的PowerShell参数组装错误、符号包装器路径/文档汇总错误和陈旧LASTEXITCODE导致的外层错误均保留，详见completion的`preservedIntermediateIssues`。最终检查没有使用这些失败派生结果；symbols-r4是在独立PowerShell子进程中实际退出0的最终证明。

另有一次r1 diff因换行转换未通过git apply检查，失败原件保留。r2从原始字节保留行尾生成diff；在全新 `playback-stock-functional-merge-review-r1-20261031` 中先实际应用S2，再检查并应用S3 r2 patch，全部739源文件与冻结实现逐字节相同。该独立合并审查的 `test-result.json` SHA为 `c54e766454d5e61099e1c4ccde21428c49fe402f08b10c8ae65407cbc0e25390`，生产树仍未改。

F-WRITER（`n/a_re/high/validated`，E-WRITER/E-NATIVE/E-MANAGED）：限定source lane可以把真正owned native setter与同值外部调用区分，并为一次实际attempt产生私有post fence。F-ROOT（`n/a_re/high/validated`，E-WRITER/E-SOURCE）：旧S2凭据保持不可变，新writer continuation保留实际root/leaf责任，未通过重写旧fence或清票签发成功。P-WRITER（`path_type=callflow`）：实际S2 root → native reservation/nonce → 唯一ASM CALL → actual Before/Returned/finally → exact post fence → opaque continuation与实际schedule witness；每步绑定上述执行与源证据。

## 下一联合缺口

当前readback是**自有native fixture实际内存**，没有把它升级为`NativePlaybackActualSource`七个独立root、exact run/scene/source generation、实际NativeRuntime DSP/media/clip/pitch/origin的联合读回。上述native writer池为每owner256个process-retained reservation，容量耗尽明确拒绝；完整provider采用前还须设计与验证持续生命周期，不能声称无限写入覆盖。

后续[生命周期增量](LIFETIME.md)已在另一个D冻结中解决活动名额的结束与复用，11进程1739检查通过；上一17导出helper的结果仅保持本页原范围，不能验后续18导出实现。不同run/scene/source的完整条件handoff仍未实现。

需先完成九成员S1的最后foundation映像验证和证据封存，审查合入S2/S3增量，再在同一新源码中接七根/真实API读回、selected-rate参与者、唯一final Clock与正式provider/module及跨局conditional handoff。不能把getter当前值、额外native导出或本项execution continuation当生产authority；默认真实Speed/Seek保持关闭。
