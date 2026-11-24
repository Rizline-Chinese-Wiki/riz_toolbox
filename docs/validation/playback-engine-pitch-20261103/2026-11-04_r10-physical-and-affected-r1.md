# 当前短物理恢复与受影响回归的证据封存

授权见[scope](../scope.md)，flavor=null。E-010/E-011 各有独立冻结输入、终态和保留清单，不把旧结果或活动日志代签当前 formal SDK。

### F-010
- title: metadata引导与Dormant Engine短物理恢复
- severity: n/a_re
- category: reverse_algo
- status: validated
- evidence_ids: [E-010]
- confidence: high
- location: D:\riz-build-cache-next\playback-engine-pitch-draft-r1-20261103
- impact: 保留r4首Start缺metadata反例、r5线程实际失败与r6诊断、r7两套phase误读、r8整组计数误读、r9实际1+1=2及r10严格正向。r10 actual build0/short0，49检查、22实际Prepare/Commit、Reason2、1call/1store、产品Pump自然回收精确row/directCalls0、fresh Start/实际Pause/唯一Clock/roots0；当前5PE-PDB/554文档。根driver17196只确认completion marker0，未观察OS退出，actualExit=null；不把短恢复等同四完整初始化mode或formalSDK。
- remediation: 只按本范围签署；后续新current route、正式SDK与四完整初始化mode各自等待实际终态。

### F-011
- title: 相同503托管61原生输入的受影响基础回归
- severity: n/a_re
- category: reverse_algo
- status: validated
- evidence_ids: [E-011]
- confidence: high
- location: D:\riz-build-cache-next\playback-engine-pitch-draft-r1-20261103
- impact: r8实际Runtime333、UnityBackingMetadata21/roots0、六Release（1既有warning/0error）、独立SDK14及8PE-PDB/526文档。driver40288真实OS handle自然exit0、七child实际0；与r10的503个src及61个native逐项SHA相同，测试差异另列。保留affected-only legacy profile、Release双profile、SDK14 standalone三种构建归属；没有重跑全28，也不签22组formalSDK。
- remediation: 只按本范围签署；后续新current route、正式SDK与四完整初始化mode各自等待实际终态。

### P-004
- title: 首捕获metadata、独立物理计数与有限受影响回归的可复核链
- path_type: callflow
- start: 四host roots与真实完成Start供应native backing身份
- goal: 无预授source/watch/Clock条件下，观察实际Dormant Engine物理完成并恢复至fresh Start/Pause
- steps:
  1. 历次真实反例保留；metadata-only bootstrap后严格区分source phase6与Single phase1/2 — evidence: E-010 — finding: F-010
  2. witness独立核raw Single1和source1、整组2，实际row自然回收并在新Start/Pause后roots0 — evidence: E-010 — finding: F-010
  3. 按503src/61native逐hash确认当前产品归属，仅转用已终态的受影响Runtime/Metadata/六Release/独立SDK14 — evidence: E-011 — finding: F-011
- residual_risks: 新engine-formal-current-r10的route、22组正式SDK和四完整初始化mode尚无可签终态，本报告完全排除其progress/logs；旧R3/R4重启中断不得重开原scope或当成功。未签全28新回归、生产admission或完整B；checkpoint由root另行统一。

## 核验口径

E-010 的当前短恢复是 build/short 两个真实child自然退出0、49项通过；根driver17196的OS exit未被句柄观察，仍为unknown/null。E-011 的driver40288则有独立OS句柄supervisor实际exit0，区别保留。

短mode无formal SDK；独立SDK14是基础分发测试，不是22成员组SDK恢复。四种完整初始化mode的严格谓词保留在r10测试源码，尚未因本scope而签通过。

元数据只供应官方Object/AudioSource/AudioClip类和已初始化静态ID-offset，不建立root/watch/Clock。source phase6与Single admit phase分开；原函数见证期真实GetTime=1、Engine=1、整组=2。r8到r10仅初始化测试文件变化，全部503src与61native一一相同。三构建变体及生成源/Portable PDB文档均单独保留。

精选原件由content-addressed blobs去重，每个原始路径仍有独立SHA/bytes/归属。只选源manifest/ZIP、明确delta、必要PE/PDB与生成源、原始命令/process/log/终态和既有失败，未复制bin/obj/cache整树，也未读取或保留活动长测作为成功证据。
