# 共用采用与原生回调联合验证

2026-10-24。**1515/1515、25套完整通过，484源/22包冻结未安装；另列真实回调379项/3独立进程。** Runtime160、Hooks171、Visual71、UI/input79、AudioContent573。root唯一builder，最终session 11559真实终态exit0；379是额外fixture断言，未并入25套1515，不代表覆盖率。

共用生产配置通道采用与dispatch core已执行真实callbacks，原函数是自有fixture。它没有生产FirstInstall receipt，没有运行GameAssembly，不解析受控高位MethodInfo/Class指针，也没有真实ledger capture。不能称为固定游戏receipt已触发回调。

## 实际通过的范围

| 模式 | checks / 原native / broker观察 | 真实结果 |
|---|---|---|
| happy | 123 / 6 / 6 | 两broker共一Entry与一次Attach；双sink同physical token、各自context，实际capture/Mid/After/Dispose；worker与foreign metadata仅scalar probe/fault；逻辑解绑、全部解绑、重绑后GC回调 |
| fault | 181 / 6 / 6 | managed After故障不改原返回；原SEH/C++的kind/code/payload不变，异常不发After；每种异常后同线程正常调用恢复context/probe；broker Unknown保留，native bridge Known idle |
| late-attach | 75 / 2 / 1 | 旧调用实际Enter后、Mid前暂停；Attach后经过真实native Mid/Exit，但没有新broker订阅/context/probe交付；随后新调用才交付双sink |

三个进程各bridge factory=1、broker Install/Prepare/Commit=0；slot30/epoch严格对应实际native通道，最终gate active/parked/unknown皆0、probe配平、输入文件hash不变。late-attach的零交付计数属于broker订阅/context/probe，没有单独计raw CLR delegate入口，不能声称其入口次数为0。测试只以weak reference查询bridge，GC不能由测试强根掩盖生产所有权；这仍不是游戏全run/GC退休证明。

生产front保留RequireReady、精确UnityTarget和同一Gate内前后VerifyReady；内部configured tuple不授予生产Ready，不向SDK公开。测试先要求生产front拒绝无receipt的fixture，再走与生产共用的采用core和dispatch；不另写替代broker。Info56不含expectedMidReturnPc：adoption可读回slot/target/original/entry/epoch；mid来自发布者tuple、共享字典严格比对，实际回调再核ReturnPc。生产post-Verify失败撤本次binding/probe且置Unknown，只完成源码复核，未进行该生产分支的动态故障注入。

首次定向session32771因测试缺using RizToolbox.Runtime、使用不存在的WrongThread字段而编译失败；root仅修为正确namespace与SuppressedWrongThread后，第二次定向session35938通过。红日志保留；这是测试编译修正，不是production运行时缺陷。 完整构建保留109条去重编译诊断，原文见build-summary；不宣称零warning或干净环境B19复现。

真实固定游戏receipt的native→CLR回调、Doorstop冷启动顺序、实际IL2CPP metadata/listener/GC、未来来源与连续mutation/provider转交/取消、全部消费者资源退休及完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。 用户“实测稍后”保持，游戏仍旧1021安装18项/其326源归档，原游戏三hash不变。仅B03整项/B18a子项勾选，完整B01–B19 active；没有新安装、游戏/UI操作、游戏进程终止或commit/push。

## 冻结与复现

484源/22暂存包逐项冻结，较CLR启动采用批482源新增2/修改8/删除0。source ZIP SHA-256：04e410017be6e81fd28b0485ff6593c60fa820f17ce434fc3ecd44e916655396；package ZIP：7fcb2d2e26c0242f42567a4fbcd5b95ca14dd9486ce023fe493c0f734451b03c；完整日志：41a44d98a044de16680110701fec48daf8e9ab2630bde6ab6f55a3813ecab914。 build-inputs-before与source-manifest的484条路径及hash完全相同；源码/包ZIP、日志和runner冻结匹配。

仓库根PowerShell7：

~~~powershell
pwsh -File scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/adopted-broker-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll
~~~

实际映像验证需显式ValidationGameAssemblyPath。生产publisher只在隔离映像fixture验证；DONT_RESOLVE不执行游戏代码，不用于生产启动。完整新缓存构建并非全新机器/干净依赖复现。

[完整日志](build.log)、[runner终态](build-run.json)、[汇总](build-summary.json)、[回调JSON](adopted-broker-evidence.json)、[三个进程与hash](native-build/adopted-broker-managed/fixture-result.json)、[构建前输入](build-inputs-before.json)、[源码冻结](source-archive-check.json)、[包冻结](package-archive-check.json)、[差异](changes-from-clr-preinit.json)、[首编译失败](targeted-first-compile-failure.log)、[独立复核](review.md)。

[范围](scope.md)、[下一步](next-integration.md)、[listener设计](native-listener-design.md)、[正式报告](../../2026-10-24_底层-采用回调联合-report.md)。
