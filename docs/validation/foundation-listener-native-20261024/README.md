# 三处 invokable 的原生前后观察

2026-10-24。**1515/1515、25套完整通过，491源/22包冻结未安装；原生listener另列8349项。** root唯一builder，最终session 45158真实终态exit0。Runtime160/Hooks171/Visual71/UIinput79/AudioContent573；额外native断言不并入25套1515，也不代表覆盖率。

本批实现真实pre CALL→fullsave→tail原callee，与post continuation E9→零栈动态stub→真实CALL观察helper→完整replay。测试只改自有函数，三处pre宽度5/5/6、post宽度6/9/9；两direct与virtual的真实硬件返回地址分别保source+5/+5/+6。原函数没有被C++包装器再次CALL。

## 实际执行与边界

实际callee返回地址核74次、original74次、replay62次；callback内真实stack walk124次，post四指令边界展开224次；pre对真实callee完整状态核68次、post对实际loop叶快照核56次；normal14/原SEH6/原C++6/observer故障case4（实际fault4）/worker2/nested4。 fixture代码写入14处；lateAttach旧call2次，其中null V1 parent observer旧call1次。实际计数与原始bytes见[JSON](listener-native-evidence.json)；不是游戏patch写数。

动态post stub登记零额外栈CHAININFO，post真实快照在0/5/7/9四个指令边界核展开；callback内实际捕获context另走helper→stub→原parent→caller。末E9与局部边界必须结合本机结果看待，不以RtlAddFunctionTable成功代替展开证明。pre真实callee与post实际loop叶快照核GPR、XMM低128、RFLAGS/控制字，post明确计入原inc/mov产生的预期变化；无AVX upper lanes/完整x87数据栈保存承诺，不覆盖prehelper每指令展开。

新增Config80/Observer32/Frame176/Info176及五exports，helper总41。独立listener descriptor在Bootstrap V1 descriptor之后、任何managed Enter之前固定；V1 observer可null，ParentManagedToken0合法，两个发布点不是聚合V2。 lane Configure只配置，不发布游戏指令。pre以callee-owned home[0]=[entryRsp+8]作最终target scratch，保原硬件return槽；这不是不可变canary。child只有当前父top/epoch/self/site/sourceRsp/event/list/index匹配才正常退出；原异常由父finally先清child，再执行旧V1 parent Exit，观察故障置stickyUnknown但不改原函数行为。

virtual一次目标读取位于observer保护之外，读取异常的地址是helper真实指令，不能伪称原CALL RIP。TargetReadFaulted的Abnormal After(token0)可能没有Before，未来managed只能按flags做native cleanup，不能从0推已capture。该分支没有本批动态验收，不冒充原callee SEH/C++用例。 明确未实现/未验分支：dynamic-vtable-mutation、target-read-av、foreign-parent-mask-and-overflow、missing-post-corruption、full-exception-record、prehelper-every-instruction-unwind、all-delegate-leaves、mutation-stopcoroutine-retirement。Post错list/index拒绝与底层Unknown管理传播的源码修正已复核，不冒充新增故障注入通过。

五份定向编译红日志保留：fixture MASM长名称/局部label与代码LABEL适配，以及fixture C++显式对齐padding/Windows RtlVirtualUnwind const参数修正；生产Listener h/cpp/ASM与Bootstrap四源没有因此修改。最终原生定向session68082 exit0后才进行full，不能把编译失败写为原生运行通过或production故障。

本批listener为native only，不是CLR listener/managed ledger接入；没有固定游戏六区发布，不升级FirstInstall V1 Ready144。invokable dispatch不等于每个delegate leaf；真实游戏receipt callbacks、冷启动、IL2CPP/GC、连续mutation/provider转交/取消、全部future来源与消费者退休、完整Retry/Return/Loader仍缺。 上批自有native→CLR broker回调379项/3进程已在本full回归，但没有变成本批listener的CLR证明。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false；完整B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持。

## 冻结与复现

491源/22暂存包逐项冻结，较采用回调联合批新增7/修改3/删除0。source ZIP SHA-256：46996f6dad1e51188491af8355d205a17dfb723ade79dc73f257f90140144194；manifest：de01d24a6963a5ab0418c09c0ef283ced0fcc20b5f0cb4aef62eec2de8fa1e48；package ZIP：ad6159b3cac76533e78aacdfafca0a13c35794def91bc9698f15b935d1e0f519；完整日志：d4bf92cfcea01e3fd52461281e0041203332dc3633b8def7f57b5d45abb2c714。 build-inputs-before与post manifest完全相同，current源/包逐项核对。完整构建保留92条去重编译诊断，不宣称零warning或干净环境B19全矩阵复现。 游戏仍1021安装18项/其326源归档，原游戏三hash不变；没有本批安装、游戏/UI操作、游戏进程终止或commit/push。

仓库根PowerShell7：

~~~powershell
pwsh -File scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/listener-native-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll
~~~

实际映像验证须显式ValidationGameAssemblyPath；DONT_RESOLVE只用于旧隔离映像publisher检查，不执行游戏代码，不是本批六区游戏发布或生产启动模式。

[完整日志](build.log)、[runner终态](build-run.json)、[汇总](build-summary.json)、[listener原生证据](listener-native-evidence.json)、[fixture输入核对](native-build/listener-context/fixture-manifest.json)、[源码冻结](source-archive-check.json)、[包冻结](package-archive-check.json)、[差异](changes-from-adopted-broker.json)、[复核](review.md)、[ABI文档](../../../native/RizToolbox.JudgeContext/ListenerContext.md)。

[授权范围](scope.md)、[下一步](next-integration.md)、[正式报告](../../2026-10-24_底层-监听调用原生层-report.md)。
