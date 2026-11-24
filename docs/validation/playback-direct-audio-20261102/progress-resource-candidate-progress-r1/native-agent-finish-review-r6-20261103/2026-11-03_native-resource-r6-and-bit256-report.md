# Native resource r6 收尾与 bit256 只读桥接审查

本域收尾通过：原46保留7969checks完整实际通过证据；新增10个responsibility模式保留已过9项，仅以fixture-r2和显式原r6helper补host-responsibility-closed，实际退出0、217checks。逐项选择合计 **56模式、24806checks、0failures**。这是逐模式选证，不是全部重新执行，详见[fixture-scope-proof.md](fixture-scope-proof.md)。

原host计数失败与关机上下文保留。fixture-r2经落盘核实于00:23:29–00:23:30已经完成actual0；本次没有新构建，没有将已完成构建改标为中断。

bit256窄范围只读审查未发现阻塞项：bit只在known armed/noFault的真实初始化物理责任成功分配journal时产生；Before仍需真正current/TLS/CallbackActive/owner/token/fullmemcmp，Returned仍需issued origin。普通closedgate无journal不收；Unknown/Withdrawn无新权限；bit不直接授Clock/Fresh/Source。

## 范围与身份

范围见[scope.md](scope.md)，合同见[IMPLEMENTATION-CONTRACT.md](artifacts/IMPLEMENTATION-CONTRACT.md)。flavor=null；本地代码与证据收尾无IOC/ATT&CK适用项。

| 项 | SHA256 / 结论 |
| --- | --- |
| 实际测试helper r6 | ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c |
| 原fixture r6 | d655568e6f1c0bf0778995f989ac5a6e40381c4f03c4bac57412160fa33831b7 |
| 补host fixture-r2 | 0043a132e7909111452ad9f913edbee271004be73fc024d890134b56b8233c24 |
| live/r6/fixture-r2 helper输入 | 7/7相等；functional可用冻结输入亦相等 |
| Source / Resource wire | 168 / 112 bytes，Source4 / Resource1 |
| 此次产品与测试源修改 / 新构建 | 无 |
| 此次新执行 | host-responsibility-closed一项，runner及fixture observed actual0 |

两构建各10source/20file均按manifest实读SHA/size；所有选择测试stdout/stderrSHA、summary及exit逐项一致。桥接冻结范围为Hub、Hub.ProgressResource、Bridge、Types四文件。[source-audit.json](source-audit.json)及[review-source-final-check.json](review-source-final-check.json)记录精确身份，[hash-manifest.json](hash-manifest.json)覆盖本包全部文件（除自身）。

## 原失败与模式选择

旧host实际责任已是Before/Returned/Receipt3/3/3、NoJournal3。它在Start前没有audio/player根，旧fixture/runner错误要求ordinary4/noJournal5，导致actual1、216checks/2failures。修正只在closed测试分支将host预期改成ordinary2/noJournal3，nonhost仍4/5，并补originals counters断言。产品输入未变。

| marker模式 | 选择的实际运行 | checks | actualExit |
| --- | --- | ---: | ---: |
| host-responsibility-closed | native-agent-responsibility-host-r6-fixture-r2-resume-20261103 | 217 | 0 |
| host-responsibility-worker | native-agent-responsibility-short-r6 | 114 | 0 |
| host-responsibility-seh | native-agent-responsibility-short-r6 | 137 | 0 |
| host-responsibility-class | native-agent-responsibility-short-r6 | 126 | 0 |
| host-responsibility-saturation | native-agent-responsibility-short-r6 | 7803 | 0 |
| responsibility-closed | native-agent-responsibility-short-r6 | 252 | 0 |
| responsibility-worker | native-agent-responsibility-short-r6 | 116 | 0 |
| responsibility-seh | native-agent-responsibility-short-r6 | 139 | 0 |
| responsibility-class | native-agent-responsibility-short-r6 | 128 | 0 |
| responsibility-saturation | native-agent-responsibility-short-r6 | 7805 | 0 |

marker选择计16837checks，加原46的7969为24806。原[失败manifest](artifacts/native-agent-responsibility-short-r6/native-retry-result.json)仍passed=false，新[host补测manifest](artifacts/native-agent-responsibility-host-r6-fixture-r2-resume-20261103/native-retry-result.json)为true。未声称其他9项在fixture-r2重跑，也未声称新originals计数断言已在nonhost执行。

## 桥接核对

1. bit只在真实journal提交产生：resourcePhysical要求ResourceEnabled、FreshBlocked和Start/SetClip/ProgressClip；requiresReceipt要求分类前armed。空row选中后，在同一owner锁内按knownArmed/noFault提交bit、JournalIndex和state1，再开放callback。耗尽则Unknown无bit。 位置：PlaybackSourceContext.cpp:296-347。
2. 真实TLS/current/CallbackActive/owner/token/fullmemcmp：Before经ReadCurrentActual；native current限CallbackActive及kind/slot，SameCall固定owner/epoch/seq/token/receiver/caller/thread/depth；native verify再核TLS、CallbackActive、FindOwner、当前线程及全frame memcmp。 位置：NativePlaybackStockSourceHub.cs:173-198; PlaybackSourceContext.cpp:603-611。
3. 普通closedgate无journal不发Before：例外同时要求资源profile、Dormant、非Armed、bit及三个role，再验证真正Before。普通Dormant无bit则忽略。 位置：NativePlaybackStockSourceHub.cs:244-256; NativePlaybackStockSourceBridge.cs:143-169。
4. Returned固定已登记origin：先需_observations已有issued Before；回读actual并与私有origin完整SameCall匹配；physical只查owner+seq+token的state2row，不从后来FreshBlocked反推资格。 位置：NativePlaybackStockSourceBridge.cs:148-179; NativePlaybackStockSourceTypes.cs:149-164。
5. Unknown/Withdrawn不建立新权限：新Before需要Known且Armed，Unknown非physical拒绝。正常Withdraw只允许已有返回/物理完成责任继续；VerifyAuthority再要求Known/Armed；Unknown physical投影不提供新source权限。 位置：NativePlaybackStockSourceHub.cs:176-198,258-278; NativePlaybackStockSourceTypes.cs:166-175。
6. bit不授Clock/Fresh/Source：全部bit产品引用仅enum、native产生、Hub资格和Bridge origin检查；owner flags无bit，wire168不变。Healthy/EnterFresh/逻辑动作仍独立核owner/fault/FreshBlocked/gate/fences/initializer/resource；Types不公开可设bit属性。 位置：PlaybackSourceContext.cpp:640-646,773-806,847-853; NativePlaybackStockSourceTypes.cs:59-75。

## Evidence → Finding → Path

[evidence.json](evidence.json)有E-001范围、E-002构建、E-003原46、E-004旧失败、E-005fixture-r2、E-006补host、E-007身份/选证、E-008桥接审查、E-009native实现。[findings.json](findings.json)有F-001原生收尾、F-002测试缺陷、F-003真实journal资格、F-004exact-origin及权限边界，均只签本域。[paths.json](paths.json)记录P-001断电后选证与P-002native row/current观察至物理退出，每步关联E/F。[timeline.md](timeline.md)区分原始落盘、根代理报告的关机/重启及新补测，没有把未观测系统事件当成原生日志。

## 独立复核与限制

在本报告目录只读验证整个包：

~~~powershell
& 'C:/Python314/python.exe' './Freeze-NativeFinishR6.py' --verify
~~~

可直接用归档owned fixture及原helper复现host；此处是复现入口，收尾没有重复执行该模式。

~~~powershell
& './artifacts/native-agent-build-r6-fixture-r2/ProgressResourceNativeFixture.exe' './artifacts/native-agent-build-r6/RizToolbox.PlaybackSourcePrototype.dll' host-responsibility-closed
$LASTEXITCODE
~~~

本包只签native owned fixture与冻结bit256桥接静态边界，不签RootRuntime、最新Adapter deriveGraph-before-RetryNative顺序修正、managed firstlate/handofflate、mapped元数据/联合模式、Clock或SingleReturn opaque receipt/epoch组合验收。未访问game/UI/account、未执行游戏、未授生产权限、未改产品或测试源、未终止既有进程、未派代理。

范围、实际退出、哈希、旧失败、逐模式覆盖、手工代码验证、E/F/P及独立复核入口均已记录。
