# 直接音频控制与精确对象隔离：主目录整合

2026-11-02，普通工程验证，`flavor=null`。当前代码已合入桌面主目录57文件；完整SDK入口长测尚未全部终态。

21:15补充：主目录fresh验证已build0/0、3进程81、新5PE/PDB512全部实际通过。另已确认下一功能缺口：实际Replay／Resume先调用Stop，而本批625837 fixture省略该步骤。新604ebb反例证明本批direct保护会撤回带Stop的正常Clock；它正在独立T增量修复，未合主。下表normal成功只签625837实际执行路径，不能外推为带Stop的实际流程已正确。[新Stop增量报告](2026-11-02_stock-stop-occurrence-report.md)单列。

这批补齐了底层对“谁在操作当前音源”的判断。其他音效、其他player和其他Progress的调用会正常执行，且不撤回当前谱面的控制权限；真正修改当前音源的调用会在原函数执行前撤回旧Clock、writer、预备变速票及SDK控制权。相关后台线程调用会先关闭native权限，由主线程下一次pump撤回托管Clock；游戏字段读取继续依照自己的权限提供，不能重新取得原生变速权。

## 当前范围

主目录为 `C:/Users/71957/Desktop/riz_toolbox`。当前冻结清单889文件，产品输入481＝474个C#、6个项目文件、1个profile JSON。整合前57项逐文件备份位于D盘 `main-apply-r1/before-main.zip`，应用记录逐项保存前后身份；用户原有README改动未修改。实现仍保持SourceV4／168B frame／136B info、PlayerWatchV2／120B，新增私有ProgressWatchV1／96B；完整物理组为18个source入口及独立GetTime，共19个。

只执行自有native fixture和固定DLL副本的DONT_RESOLVE映像检查，不安装或启动游戏，不执行真实游戏方法、DllMain或游戏UI，不使用任何账号。生产Speed/Seek、完整source generation、未知lower／icall／Playable、任意Seek玩法重建、effect及B01–B19仍未完成。

## 已有实际终态

下表相对目录均以 `D:/riz-build-cache-next/playback-progress-control-isolation-r1-20261102/` 为根。每项保留实际命令、PID、exit、stdout／stderr及输入身份。数字按各自范围分列，不把重叠检查相加充当总覆盖率。

[已完成范围索引](combined-main-scope-review-r1/scope-index.json) 已实际核对16份结果及2207个引用文件，逐项哈希一致；同目录保留这些不可变结果的副本及formal-r3进行中快照。此索引的passed只表示已完成范围的原件完整性，不表示剩余SDK矩阵或全部功能完成。`functionalValidationComplete` 与 `formalSdkMatrixComplete` 均保持false。

| 证据 | 实际结果 | 证明范围 |
|---|---|---|
| `main-apply-r1/test-result.json` | passed，889逐项匹配，57文件已合入 | 当前主目录应用与改前备份 |
| `native-checks-r2/test-result.json` | 15进程／4622，全0 | Progress／Start的host、pending、current、foreign、class及迟到退休 |
| `direct-watch-regression-r1/test-result.json` | 94进程／15556，全0 | 新helper上的六direct／watch原生合同 |
| `legacy-regression-r1/test-result.json` | 27进程／7767，全0 | 新helper上的旧五AudioSource leaf隔离 |
| `player-regression-r1/test-result.json` | 20进程／6735，全0 | 新helper上的MUG三wrapper隔离 |
| `original-eight-r1/test-result.json` | 14进程／36675，全0 | 原八role回归；不代替新增ProgressWatch合同 |
| `managed-checks-r5/test-result.json` | fresh build零警告零错误；3进程／81，全0 | 真实四／七根、foreign与first hold、严格owner-pump撤权诊断 |
| `symbols-r2/test-result.json` | 5对PE/PDB、512文档 | r5实际产物与889冻结输入对应 |
| `foundation-r2/full-r1/test-result.json` | 28套／2598，全0 | 新owner-pump产品的托管整树 |
| `foundation-r2/solution-r1/test-result.json` | 零警告零错误 | 同源码solution |
| `foundation-r2/sdk-r1/test-result.json` | 14，全0 | 两个独立SDK插件发行检查 |
| `foundation-r2/release-r1/managed-build-manifest.json` | 六组件Release全0 | 同源码Release构建 |
| `mapped-r2/test-result.json` | metadata69＋joint39＝108，全0 | 新产品和新helper的19成员固定映像安装；游戏执行0 |
| `formal-r2/test-result.json` | 2进程／65，全0 | 初始SDK实际变速后的worker撤权37，player normal28 |

`formal-r3` 使用与r5相同的实际程序集及新Q/n1 helper。20:45核实已实际first102、same54、cross57、foreign61、native0／1各28通过；其余十模式仍运行或排队。这份进行中结果的顶层passed=false不等于十项失败，也不构成整体通过。主目录fresh `main-verification-r1` 正在验证81项及实际PE/PDB，终态前不写为通过。

候选、r5、foundation-r2的889文件逐项比较中，后续差异仅 `scripts/Test-DirectAudio.ps1` 的ValidateSet补齐；runtime、adapter和native实现相同。主目录fresh验证进一步从实际C盘889项建立快照重新编译，避免仅靠D盘旧产物宣称C盘fresh build。native DLL沿用Q/n1的同一冻结编译结果，并核对其每个编译输入与主目录相同。

## Evidence → Finding → Path

| Finding | Evidence | 实际执行路径和结论 |
|---|---|---|
| F1：外国对象不能污染当前票据 | Q原`counterexample-r1`两实际失败；新native-r2及四／七根专项 | foreign receiver → native身份筛选 → 原函数恰好执行；不进入当前journal／fence／初始化hold |
| F2：后台native故障须由owner主动发现 | L fullworker31失败、诊断r6失败及r7旧状态；Q r5严格21、formal-r2完整worker37通过 | worker先Unknown并执行original → owner poll native健康 → 消费fault queue → 旧Clock撤回、SDK旧lease失效，unknown七根保留 |
| F3：关闭native支持不应吞掉独立游戏读数 | 同上Q的两个SDK clock与通用lease断言 | 稳定host无armed session／物理故障 → NotReady或TargetExpired → NativeDescriptor为空、game reading仍可用；新通用lease的SetSpeed仍拒绝 |
| F4：合主源码与验证范围须一致 | candidate清单、main-apply逐项记录、r5和foundation-r2清单及实际PDB | 固定889源码 → 新构建／源码文档身份 → 实际测试结果 → 逐文件应用C盘；长测范围未闭合不签完成 |

L旧19项已全部终态：18成功，1项旧产品worker失败；693为包含失败检查的执行数，不能写成693通过。P和Q改前888的formal-r1均保留自己的范围，其中Q formal-r1实际5进程302通过。它们不用于签当前owner-pump产品的新行为，失败和中断记录不改写。

## 续接与复现

已完成目录不得覆写或重跑。当前长测由 `Run-FormalChecksR3.py` 独立隐藏driver负责，续接时先用CIM核验精确命令、创建时间、PID和结果文件，再消费终态。不得根据耗时或桌面端重启推断测试失败、杀掉活进程或伪造exit。

定向复现入口是仓库的 `scripts/Test-DirectAudio.ps1`：显式传入fresh托管测试程序集、自有fixture、Single／Source helper、Dobby以及全新OutputDirectory，可按Modes选择已有模式。fixture必须是独立profile固定的625837身份；重编recipe不会自动取得原有CALL授权。完整命令以各实际 `.launch.json`／`.result.json` 为准，复现前核对相应源码与四个native输入身份。

下一先收本源码剩余formal终态和主目录fresh产物，封存最终hash索引；随后继续N2未完成范围。当前检查点是 [CURRENT-CHECKPOINT](../../../.docs/CURRENT-CHECKPOINT.md)，下一功能顺序见 [NEXT-FUNCTIONAL-GAPS](../../../.docs/foundation-expansion/NEXT-FUNCTIONAL-GAPS.md)。
