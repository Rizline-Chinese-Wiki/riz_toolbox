# 真实初始化协程追踪与启动/返回接线

2026-10-23。**1343/1343、25套完整离线检查通过，冻结暂存未安装**；Runtime138、Hooks126、AudioContent468。较[1324协程transport批](../foundation-coroutine-boundaries-20261023/README.md)新增18组Audio检查与1组Hook检查，并增强既有真实Dobby初始化fixture的getter窗口断言。

这批已在生产启动类内使用真实初始化协程tracker，commit和菜单返回均调用其判定。Loader尚未构造完整LOCAL链，RequestPlay继续关闭；实际安装仍1021，无本批游戏实测。这里只追踪getter创建的两条协程，完整活动退休与Retry/Return仍待完成。

## 行为与权限

[tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalInitializationCoroutineTracker.cs)在setter后activation前登记32个上限的process owner，预建两条record及四个context，预留enum/Coroutine四个root槽。生产[native backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalInitializationCoroutineBackend.cs)复用一个public Start/Auto物理patch和两个MoveNext入口；class/field/方法/ABI、profile/SHA全部核验。

Start捕获同时要求同一launch controller、getter的实际返回caller（3BEFDA/3BF0B2）、已验证native getter original窗口、确切nested enum和state0。enum独立root并发布身份后才执行original，所以Start内部同步首Move不会漏掉。Start实际返回的Coroutine分别独立root；null只允许已经同步观察到该enum正常false自然结束的情况。

delegate校验使用官方具名字段metadata和已静态核对的闭delegate构造器：method、method_ptr、invoke_impl、m_target、method_code、非virtual、无multicast列表均须与实际声明方法/目标一致。Delay action@3C1930与Wait predicate@3C1960指向本局controller；Wait action@3B1B60必须是NormalMode自己的缓存闭包实例与精确MethodInfo，不能仅凭共享代码地址认领。

Delay初始seconds位为40466666（约3.1f），允许自定义DeltaTimeProvider的state1计时循环及默认WaitForSeconds的state2；seconds随后可由真实计时递减。WaitUntil使用state0/1。各Move只能捕获已发布的实际enum，要求状态沿已观察的路径推进；正常false后state=-1、delegate身份稳定、Start已正常返回、所有native窗口known且0，才确认两条初始回调结束。

worker/foreign metadata只用托管identity与原子flag撤权；自己的重入、错误状态/回调/句柄、异常/缺after、未知root等保留责任，不重试。观察根可在未开始或两条已结束后单独detach/redrain/free；它不产生全部run消费者结束凭据。

## 生产调用点

[coordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)实际setter后捕获tracker，再将同一tracker交给[菜单账本](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalMenuLedger.cs)。Tick在activation核验后、图验证/commit前固定调用自己的TryConfirmEnded；核验期间若terminal/fault到达还会重新检查pending authority。菜单BeginReturn在取得新的normal信用前也要求两条已结束，防止旧getter回调消耗新return信用。

[HasCurrentNativeGetter](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.LocalInitializationIsolation.cs)只在精确原生getter original窗口返回true，匹配session/generation/controller/info、native frame/token和当前logical authority。before/after验证、ordinary child、worker、managed-only transport和窗口外均不授权。它是额外来源约束，不能代替actual caller/enum/root。

受控backend拒绝生产backend；controlled tracker无法通过生产菜单同launch IsFor校验。生产coordinator必传自己的tracker方法，没有默认“已结束”参数。静态测试接缝与生产权限保持区别。

## 证据

[15组tracker回归](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalInitializationCoroutineRegression.cs)覆盖两种Delay路径、同步首Move、无关controller/caller/enum、owned worker、错误初始state/常量/delegate、重复/角色alias/缺getter窗口、false/退出前等待、状态变化、物理重入、Start/Move原函数异常、回调变化、部分root/install/free失败、nullCoroutine、unknown/负在途/线程和未使用观察者撤回。

[2组commit组合](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalRunLaunchCommitPumpRegression.cs)与[1组menu组合](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalMenuLedgerRegression.cs)确认实际tracker未完成/仍在途时不commit或取return锁，核验中丢authority不能提交。[Hook窗口回归](../../../tests/RizToolbox.Hooks.Tests/LocalInitializationIsolationTests.cs)验证准确作用域和ordinary子调用遮蔽。[真实native初始化fixture](../../../tests/RizToolbox.Runtime.Tests/NativeInitializationAbiFixtureRegression.cs)在真实Dobby/C++original的probe中确认窗口为true，而before/after、Start与完整退出后为false；这不等于在游戏运行了新Adapter backend。

[静态13范围/1731指令](coroutine-disassembly.txt)、[metadata节选](coroutine-metadata.txt)、[证据](coroutine-evidence.json)与[再次核对](coroutine-evidence-check.json)来自固定GameAssembly。相较1324扩充两条closed-delegate构造器、Delay todo与Wait predicate；构造器只取明确有界范围，逐项验证三输入hash、指令字节和3.1/3.0常量。runtimeAcceptance仍为false。

## 构建与冻结

[最终日志](build.log)/[摘要](build-summary.json)为session72753 exit0，完整15条既有测试warning（14 FakeNative CS0649、1结果sink CS8602）保留。首次[1342过程日志](build-before-provenance-strengthening.log)为session54321 exit0；之后新增重入case及增强真实native窗口断言，最终1343才是本批冻结版本。定向[Adapter](adapter-targeted-first.log)、[Audio首轮](audio-targeted-first.log)/[第二轮](audio-targeted-second.log)、[Hooks](hooks-targeted-first.log)均保留。

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | 405项，4,113,981原始字节 | `dec15e2d89dfcdb0dc0c710c939c5640b88d1773f7e809f85d0c3751f4ff3134` |
| [源码manifest](source-manifest.json) | 405项 | `c1f8f49c294839fa693d73d7fb0b32c1421a7b514a70e2a611ab309957bab4e9` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | 18项，3,086,824原始字节 | `4d8d9c7de41e7df42fb1d9bc7432791ceef2c4ae10b2c3e87e5e964183cc86d6` |
| [完整日志](build.log) | exit0 | `5780e31120f3aa7e3e53b03a6f36b75619fa12dd2ca9a2492610dc04f7747dbd` |

[差分](changes-from-1324.diff)/[清单](changes-from-1324.json)为3新增、9修改、0删除，两份冻结源归档各自核对。[暂存](staging-check.json)/[工作树源码](source-integrity-check.json)、[旧1021安装](previous-installation-check.json)18项及旧326源归档、[原游戏三文件](original-game-check.json)匹配。native helper旧fixture31261/61358、初始化1747和native-only probe18单独记账，不加入1343；本批无独立子代理/第二runner审计。

复现完整离线构建：

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/initialization-coroutines-reproduce-20261023'
```

生产适用边界见[scope](scope.md)，复核见[review](review.md)，后续见[真实活动退休计划](next-integration.md)。未安装/游戏操作/B勾选；用户“先继续代码，实测稍后”保持。
