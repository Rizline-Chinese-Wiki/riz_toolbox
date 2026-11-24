# 新Kernel早期链：原件有限复核

2026-10-25；flavor=null。本页由新wrapper作者对最终原件做有限自查，不是外部独立审计。审查开始时root唯一构建session17459尚未结束；定稿前已读取[terminal.json](terminal.json)，root实际观察该session退出0。本页逐项复核的是新Kernel的73用例，未重新逐项审计旧publisher57和旧chain73，不能用本页独立签署203项全部语义。所有操作仅为读取、哈希、ZIP逐项核验和离线解析；没有构建、启动child、安装或启动游戏，没有修改604项冻结输入。

## 结论及证据路径

| Evidence | 实际观察 | Finding | Path |
|---|---|---|---|
| E-01：[新Kernel汇总](D:/RizToolbox-build-cache/early-kernel-final-20261025/kernel-cases/fixture-result.json)及73个各自result/process/mapping | 73个用例passed，累计13248项父断言；汇总逐项等于各目录实际result，mapping哈希对应真实bin | F-01：新profile受控fixture矩阵已实际完成；不是只靠aggregate的passed布尔 | P-01：保留原始73目录及终态；其余两组由各自汇总和root终态证明，不混计本页 |
| E-02：[held-inputs](D:/RizToolbox-build-cache/early-kernel-final-20261025/kernel-cases/held-inputs.json)与E-01的files | 38项全部FileShare.Read/writeAndDeleteDenied=true，逐路径before==after==本次读取哈希 | F-02：本组所用二进制、源、runner、manifest、Python及传递verifier在该组期间身份固定 | P-02：将这些实际文件和清单一并归档；锁定不推导全部OS依赖安全 |
| E-03：[最终源码清单](inputs-final-before.json)和[604输入ZIP](source-final-inputs.zip) | 604个ZIP成员逐项匹配清单，当前604文件也全部相同 | F-03：已核实际最终源码，而非沿用第一次失败604快照 | P-03：保留两份不同ZIP和[有意变化清单](intentional-source-final-changes.json)，不修改历史ZIP |
| E-04：[实际debug结果](D:/RizToolbox-build-cache/early-kernel-final-20261025/kernel-cases/normal-debugger/result.json)、[JSONL](D:/RizToolbox-build-cache/early-kernel-final-20261025/kernel-cases/normal-debugger/debug-events.jsonl)和[MMF](D:/RizToolbox-build-cache/early-kernel-final-20261025/kernel-cases/normal-debugger/mapping-final.bin) | 本轮16条debug行：5 Load、0 Unload、4对真实entry/return、3条错RSP诊断、1007为0 | F-04：本次实际观察验证了真实返回栈及清槽；本次没有临时双映射或重复卸载通知 | P-04：结合单列的真实预飞6Load/1Unload证据，保持各轮计数独立；1007原生分支仍未覆盖 |
| E-05：[首次command](command-result.json)、[首次日志](build-and-test.log)、[首次源码ZIP](source-inputs.zip)及[history](history) | 首次构建72个非debug通过，debugger遇重复publisher加载而退出；之后经历生命周期、硬件返回和真实readback三阶段 | F-05：旧唯一Load/共享return-PC int3假设的失败没有被消失或改写成成功 | P-05：保留驱动退出和child退出的区别，正式新组结果不替代历史原件 |

本次离线复核调用`load_all()`读取两份固定SHA历史模块，并重新对最终debug MMF/JSONL执行新独立debugger校验：276项断言通过，生成的debuggerProof与已存result逐字段完全相同。另从实际文件句柄重测六个PE的fileIdentity，全部匹配parent原PE proof。这个动作没有启动新的debugger或child，因此不增加实际进程数或原生分支覆盖。

## 新组73实际计数

73份caseResults与73份process记录按用例目录逐项交叉，均有started/exactOwnedProcess=true、timedOut=false，实际exit与相应result一致。预期拒绝用例的非零退出是其正确结果，不能把它们描述成73次exit0。debug项process记录PID61792是自有debugger驱动，真实MMF被观察child PID60000；这两个进程身份各按自己的证据核验，不能要求二者相同。

| 用例 | 父断言 | 实际原调用 | Observer配对 | 实际捕获异常 |
|---|---:|---:|---:|---:|
| 未发布mode14 baseline | 1145 | 68 | 0 | 8 |
| mode0正常 | 2162 | 86 | 56 | 8 |
| mode1已有调用后拒绝 | 522 | 30 | 0 | 0 |
| mode2已有worker后拒绝 | 523 | 30 | 0 | 0 |
| mode3 worker | 2262 | 92 | 56 | 8 |
| mode15真实递归Busy | 45 | 0 | 0 | 0 |
| 额外normal-debugger | 2437 | 86 | 56 | 8 |
| 新组全部73项，含其余模式及56内部故障 | **13248** | **392** | **168** | **32** |

原调用/配对/捕获数重新由每份event20/40/27统计，并非把父断言当原调用。normal-debugger本轮2437少于预飞2457，来自实际加载行数变化，校验器没有要求固定Load数量来凑数。未发布异常基线是另一个相同Targets binary/RVA的真实child：SEH raw flags为0x80，C++为0x81；后续按kind/faultMode精确比较稳定字段，不mask，也不声称同一个进程或对象。

mode15仍核真实Capture递归冲突和永久Busy：outer1/inside2/later1，最终captureCalls4、flags7、Unknown/failure22，Proxy/Importer/源写均未进入。fault12/13/14是实际受控API结果注入，不能写成OS自然保护/恢复/Flush失败；partial路径的真实字节和共享页保护责任继续由独立ActualMemory快照核验。该组不是25/1617托管全量回归的又一次运行。

## 固定输入和版本未混用

38项held输入包含7个实际二进制，另外31项包含C++/ASM/头文件、builder/runner、manifest、Python及三个实际verifier等；不是38份源码。独立Kernel为`RizToolbox.EngineNodeKernel.dll`，SHA256为`923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662`；new publisher为`RizKernelFixedPublisher.dll`，SHA256为`2ec51fbac5148ee95c8eb2a396abcc23815916b41797a9e2b80ce07113c37a89`。没有把旧JudgeContext重命名后当Kernel。

最终AST变换为fixed6/early40，两份历史文件SHA固定；`verify_workload`与`exception_records`适配前后AST强制相同。只有新profile的debugger改为独立实现，完整fault/receipt/实际内存/baseline路径仍复用旧实现。实际结果的profile为engine-kernel、wire3、publisher2、helper transport2/scalar1；ActualMemory仍1。全零tuple允许尚未InitTargets，任一非零字段则必须满足完整wire3/kind/size/slot身份，不因早期拒绝跳过实际Info/Memory责任。

[PE原件](D:/RizToolbox-build-cache/early-kernel-final-20261025/kernel-cases/pe-imports.json)保留exact8 Kernel导出，仅两个系统导入、无entry/TLS/delay/loadconfig/CLR以及普通静态依赖；publisher和Kernel没有被制造初始化回调。六个PE的fileIdentity由parent同一只读句柄测得并与该句柄读出的完整SHA绑定；每条Load再与parent独立fileid核对。fileid不是对整个Windows loader行为的证明。

最终源码由上批现行599输入演进：12个已有输入有意变化，5个声明新增，实际最终604成员全部已核。该演进由清单明确列出，不能对旧599/current或最初失败604作不变宣称。

## 生命周期、硬件返回及历史边界

本轮5个角色各一个实际Load，最终live base逐项等于MMF header选择；没有实际1006或1007。本次newdebugger不再强制role1/2只能Load一次，而是最多每role8代、全局32实例；凡实际出现非选实例必须完整保留Load/Unload且在ProxyAttach前已关闭，不能筛掉多余行。role3/4/5仍只一个初始化实例、实PE entry/TLS RVA与回调计数精确匹配。

实际初始化为Recorder entry/return、Proxy entry/return、Targets TLS/return和Targets entry/return共4对。全部返回按thread/PC/instance/slot/expectedRsp匹配，expectedRsp为entryRsp+8；before与Set后独立GetThreadContext readback共同证明DR0..3/DR7拥有关系和返回清槽。三条1008实际RSP不同的命中保留pending并置RF续行，之后才有真正精确RSP返回，不把1008计作return。DR6只严格比较B0..3和BD/BS/BT等受控原因，DR7固定bit10允许OS规范化；没有把源码请求的保留位错误描述成实际readback。

历史证据分别保留：

| 阶段 | root实际驱动/child终态 | 能证明与不能证明 |
|---|---|---|
| 首次session31803 | 正式command退出1，producer未进入 | 唯一Load假设拒绝，不是native publisher已失败；首次604 ZIP保留 |
| [初诊49229](history/diagnostic-terminal-observation.json) | 驱动0，三个child均1 | 不能因驱动0称fixture通过；旧raw不完整，不能先签OS重复映射合法 |
| [生命周期96108](history/lifetime-diagnostic-terminal-observation.json) | 驱动0，三个child均1 | 有Load/Unload原始事实，同时暴露旧共享int3返回PC错栈问题 |
| [硬件返回52109](history/hardware-diagnostic-terminal-observation.json) | 驱动0，三个child均0 | 首版硬件返回实际续行；尚缺最终entry/return真实RSP和readback字段，不能代替最终schema |
| [最终metadata预飞69351](history/final-preflight-terminal-observation.json) | 驱动0，三个child均0 | 三份各真实6Load/1Unload/3错RSP，最终字段完整；独立于本轮正式5Load/0Unload |
| [三份离线验证及14负例](history/final-preflight-offline-verifier.json) | 没有新增child/build | 各2457完整离线断言通过；14个受控JSON/shared输入变异被拒绝，属于校验器负例而非native分支覆盖 |

1007原生重复卸载通知在本轮和最终预飞均未命中。代码校验合同虽要求先前精确retired实例及真实VM query不再原MEM_IMAGE，这不能替代实际命中证据。`retiredUnloadNotificationsObserved=false`及`completeOsLoaderLifetimeClosureVerified=false`保持明确；未知系统模块的stderr卸载不是受控五角色生命周期证明，也不能签全部OS闭包。

## 文件哈希与后续范围

| 文件 | 本次实际SHA256 |
|---|---|
| 新组fixture-result.json | `1150244703bb4c0fd8493a278e56e655d8f9626bbeeb0ca418da66293fcba190` |
| 新组held-inputs.json | `66749fd7331e521e9c2e01d73a6fd762efa3a2916ef0c241f1e8d035b74bf5a7` |
| 新组pe-imports.json | `3b5a36e3cc0e193f9eff3913c30ec7b695b3af2c8aca08a58d9a9a3f97690184` |
| 本轮normal-debugger/result.json | `fc7519ccb0d2cc512ae76c5903fe9d2c6db2da9eeb33350013eaac54aaa2f9e0` |
| 最终604 source-final-inputs.zip | `b3e20129db468c400a58ac2e806df9965da897cd56c12ad552857ba9252869be` |
| 首次失败604 source-inputs.zip | `c55f81f36230b9dcd8cc91f979c1a3a7bbaa2f97c5f42b2f967bc31af49f199d` |
| 最终wrapper源码 | `dafb010e1916229a87d415d2880b8d166f76f9cf9be741ed69cf15d459cd0a38` |

复核可用Python3.14标准库重读上述JSON、按73个目录逐项比较caseResults及SHA，再用`zipfile`将inputs-final-before.json的604条逐一与ZIP/current比较。原始构建/child仅由root执行；本页不提供再次启动fixture的命令作为已做操作。

本批仍是自有Targets的固定早期链。实际游戏UnityPlayer固定四入口、生产V4/WinHTTP、Kernel生产AdoptConfigured、真实owner/GC/coordinator接线、scheduler/future及取消退休闭包仍需[production-next](production-next.md)的独立证据。完整B01–B19继续进行；不授gameReady、EngineCancelled或释放Pending/root资格。
