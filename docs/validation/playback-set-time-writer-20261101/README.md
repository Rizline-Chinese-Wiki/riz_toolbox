# 播放位置写入使旧时钟失效

2026-11-01，`flavor=null`，范围见[scope](scope.md)。当前已合入的 Source V3 为固定 `AudioSource.set_time@0x1511C00` 增加第10个 source 角色；与 GetTime 共11个物理入口，160B frame/136B info，独立 writer 子协议仍为V1。原15个完整body pin保留，新固定80B setter使总数为16，既有file/DIR64、metadata、RF、leaf与物理故障约束继续生效。

外部位置写入的实际 Before 无条件推进调度序列；同值、改走又改回、写后异常都结束旧时钟和事务凭据。正常 Resume 的合法树则必须从实际固定CALL进入 `SetTime→SetPitch→PlayScheduled`，来源、物理parent、MI与次序分别核对，实际读取保存的播放位置和调度实参。这个观察入口不签任意Seek，也没有补造音符/判定状态重建。

当前源码的27套2465、六DLL Release、solution零错误/零警告、SDK双插件14均已actual0。固定真实映像单normal另外actual0/30，11次Prepare/Commit，游戏方法/DllMain调用为0。SourceV3的原八角色native回归14进程36675通过；新增角色以各自专项核对，不用该八角色回归替代。

专项现已完成：[整合结果](integration-result.json)、[证据索引](evidence-index.json)、[有限独审](final-review-result.json)。独审逐项核最终797源与主目录、8模式混合来源及异常责任，没有新增产品反例；[补充索引](review-index.json)。8个唯一模式728项，来自旧5/233＋fresh3/495；fresh为正常正式Module/SDK442、V2严格拒绝5、异常48。原r3及第一次补验失败保留，不冒中断成功。最终修补仅1个独立tests Program，产品472输入及产品PE/PDB字节未变；该独立项目重编和三进程验证，未重复无关的27套。异常后diagnostic在Tick释放observation前复制，Tick后核已释放；真实Unknown和七根责任继续保留。

完整产品输入口径为472个文件：465个`.cs`、6个`.csproj`和1个固定profile JSON；当前797源清单还包括native、scripts与tests。旧记录的“472个C#”及“7个csproj”是分类错误，以当前清单为准。

证据原件位于 `D:/riz-build-cache-next/playback-set-time-main-integration-r1-20261101/`，专项原件在 `playback-set-time-writer-stage-r1-20261101/`，两模式修补在 `playback-set-time-v3-two-mode-continuation-r1-20261101/`。具体输入/命令/实际退出及补丁由最终索引关联，原中断和失败不会改写为成功。

E-MAIN：797源精确合入；E-BASE：当前源码core/full/Release/solution/SDK；E-FOCUS：六成功模式及后续定向补验；E-MAPPED：单normal映像11组零游戏执行；E-REVIEW：有限源/符号合同独审。

F-TIME（severity=n/a_re，category=other，status=validated，confidence=high，evidence_ids=[E-MAIN,E-BASE,E-FOCUS]，location=AudioSource.set_time/NativePlaybackStockOccurrenceProducer）：已知位置writer进入共同来源序列，外写会撤旧凭据；合法Resume的实际三子调用可被正式受控provider消费。异常与协议拒绝现已分别补验；此结论仍不包括完整生产来源覆盖。

P-TIME（path_type=callflow，E-FOCUS→F-TIME）：实际set_time entry→Before推进scheduling fence→Router/source撤旧控制权→实际原setter一次→Returned/Abnormal与physical exit分开。正常Resume则以固定parent和三个实际child构造完整stock receipt，再进入既有读回/时钟事务。

下一功能在独立D目录实现初始化父协程观察、跨yield阻止新控制、物理退出后七根重捕获和专用同run恢复。当前V3结果不验证该V4增量；生产首次准入、未知lower/inline writer、任意Seek、effects结束及完整B01–B19仍缺。
