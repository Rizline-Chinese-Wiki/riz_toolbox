# 原生交接门、唯一 Clock 与实际播放来源联合

后续审查补记：初版S4虽通过27套2465、六DLL、solution0/0、SDK14及该报告专项，独立review进一步证明inactive reader快捷退出会将真实native SEH包装后的managed异常误判成逻辑撤回。原反例r1 SHA `fa30079061d65452ec9af421a32cefb34e5ea6ff864b15b66eda49700e546130`保留；主桥已改SHA `79e4947c066ad91ae56cba29db50cf61f8d091777d4456569a2f2afad3450f07`，递归检测SEH/AccessViolation、wrapped与所有aggregate children。独立精确补丁r2 SHA `cc673ed8114382dd8a9d56e0198dec836a7639c4c40d5b8960745186634c4d95`：新build0/0、五actual child0，所有真实native故障Known=false／owner notified，逻辑Dispose保持Known=true。旧报告交付仅验初版；修正源码的新树验收已闭合：27套2465、六Release、solution0/0、SDK14，联合proof SHA `8c49589b57211060aa180a9351fc7ed9256fa601fb09ea8f6aae9ec07e180a08`，见[修复验证报告](2026-10-31_stock-clock-managed-seh-report.md)。不能使用初版旧结果验收新源码。

2026-10-31，`flavor=null`。本轮实现已经合入主目录19文件；没有安装、启动游戏或操作游戏UI、账号，没有发布新候选。它补上构造期间native policy已经active、最终Clock却尚未赋值的问题，并将既有stock occurrence、私有native writer、七根/API读回和宿主run票接到一个唯一的Clock实例。

本项冻结交付：`D:/riz-build-cache-next/playback-stock-clock-bootstrap-r1-20261031/completion-r1.json`，SHA `b5ebbdbbfdeffbc00de7e60deac95916b80edccca9100e7391fce6d9b13b525d`。最终专项是10个独立进程179项、原有rooted回归15进程326项，以及原生11案例388项，分别actual exit0，不把不同范围相加。合入原件为`D:/riz-build-cache-next/playback-clock-main-integration-r1-20261031/test-result.json`，19文件、756源与当前树逐字节核对；主树完整回归另用全新`foundation-stock-clock-integrated-*`批次验收。

## 行为与实现

| 组件 | 责任 |
|---|---|
| `native/RizToolbox.SingleReturnPrototype` | 新bootstrap信息192B、四个export；保持128B policy／112B stats等原布局。native登记不可复用的真实receipt和epoch，dormant只执行实际original/observer，不捕获policy也不调用Clock reader。实际in-flight为零并满足原receipt/initial policy后，才在同一gate中Arm |
| `NativePlaybackSingleReturnBridge.Bootstrap` | managed确切对象登记与native receipt/full-policy双向读回，复制和旧清理不能取得新policy所有权；Busy保留真实在途责任 |
| `NativePlaybackClockPublication` | 先dormant publication，再由已登记session核唯一final Clock并Arm；既有正常修订仍走native条件Publish和读回，不事后补publisher |
| `NativePlaybackStockClockRouter` | 物理组的稳定回调路由，final Clock槽只赋值一次；独立验证roots/run/source/profile，reader不反调Clock.TryGetTime或Publication.IsCurrent造成递归 |
| `Session` rate source | 每个值事务另持七个独立根，复用原生writer receipt和现有audio＋clock participants，不另造事务器；Dispose不会撤观察者根或物理hooks |

真正Pause完成且已physical exit、actual API读回处于ready paused时，才能建立baseline。构造期间真实GetTime仍返回原值且reader调用为零；Arm后同一最终Clock用于native返回和准备／确认。Replay/Resume继续由原stock函数reset pitch=1，再用私有writer恢复选中rate=2，实际leaf double参数进入Clock，预滚不缩放，start之后才按rate前进。

来源外同值setter、没有合格leaf的stock no-op、宿主局身份丢失会撤旧policy。若停用发生在已经捕获的GetTime reader中，先按自己的精确publication撤回，七个独立根等真正native退出再释放；该正常逻辑退出保留stock bits，不误造物理Unknown。真实reader SEH／Unknown仍保留故障，不能借逻辑退出隐藏。

游戏映像分支强制消费既有私有fixed call profile，并由真实group Module派生唯一的Progress.Update返回地址；新session入口不接受任意caller地址。自有fixture仅测试resolved MethodInfo路径。本轮没有执行真实游戏fixed consumer，不能用它替代真实首次准入或完整生产验证。

## Evidence → Finding → Path

| Evidence | 原件及实际限定 |
|---|---|
| E-NATIVE | case的`native-build-r4/`、`native-gate-r4.*`、`native-proof-r2/test-result.json`：11案例388项，actual original/observer/reader、阻塞原调用时Arm拒绝、实际drain后成功、取消／伪票／fault／旧cleanup和异常记录；2PE/PDB、2COFF、14可执行exports |
| E-CLOCK | `checks-r4/test-result.json`：10进程179，normal、cancelled/run-lost/transition dormant、foreign equal writer、run loss/no-op after arm、dispose-reader、withdraw-return、rate-rollback |
| E-ROOTED | `rooted-checks-r3/test-result.json`：当前生产源码与同一最终native helper，15进程326，保留run/roots/API/fence失效与actual journal drain回归 |
| E-SYMBOLS | `clock-symbols-r2`、`rooted-symbols-r2`：各5PE/PDB／469源文档；两消费者的Sdk/Core/Runtime/Adapter DLL逐字节相同 |
| E-MERGE | case的`handoff-r1`／`patch-review-r1`，及main-integration原件：756源归档、745源before归档、19文件diff、实际git apply与逐字节比较 |

F-CONSTRUCT（`n/a_re/high/validated`）：E-NATIVE/E-CLOCK证明构造gap不再消费半构造Clock，Arm与真实在途计数在native gate中线性化。F-DRAIN（同级别）：读回内停用不提前释放自己的七根，也不将正常撤回变成physical fault；原异常code/flags/args有直接原函数baseline。P-CLOCK（`path_type=callflow`）：actual stock Pause＋host run登记→独立七根/API baseline→native dormant receipt→constructor注入publication的唯一final Clock→实际Arm/readback→native GetTime original→独立DSP/source读回→candidate与最后native gate；stock schedule沿同一Clock修订。

## 保留的失败与当前缺口

早期原生断言错误地要求软件异常flags为0；Windows实际为128，最终直接调用同一原函数作为比较基准，证明dormant路径的code/flags/args相同。managed ctor访问级别错误、fixture原本progress＋offset与媒体值不齐、原值本可恰好等于candidate却被错误断言为必须不同，均有原始失败。读回内停用初次断言要求root数量恰好7，实际runtime_invoke还持临时root；诊断证明七个独立根一直保留，最终检查至少七根、实际drain与最后全部释放。所有中间目录保留；只采用最终388／179／326对应的源码和二进制。

本native helper SHA `9c9a59310227bad89d95e8863784b0a17c9defc4d8cb3d7fc970517238997d05`属于独立prototype，未替换正式FirstInstall V7或游戏候选。metadata synthetic；正式stock provider/module消费、同物理安装跨run条件handoff、完整source generation、未知lower writer、生产首次准入及任意Seek重建仍未完成。default adapter继续不装配真实provider，真实Speed/Seek和production关闭，B01–B19总目标保持未完成。
