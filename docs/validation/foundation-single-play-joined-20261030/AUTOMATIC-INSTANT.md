# 自有 Single listener 自动进入真实 Play operation 与 source 生命周期

最终原件在 `D:/riz-build-cache-next/single-play-automatic-frozen-r4-20261030/`。native `/W4 /WX`、managed build、一个 `automatic-instant` 独立进程均 **actual exit 0**，严格 JSON 合同通过；managed 保留 26 个原 fixture warning。660 个冻结源码文件前后 hash 相同，649 个 r3 固定基底文件和全部新增输入未变。生产和测试 live 源码均未修改，本包所有新增 native/C# fixture 与 runner 都在 D 盘；repo 只增加合同和本报告。

本包只证明实际 Play operation/source **生命周期** 到私有 InstantWork transfer。source 注册和 metadata、outer Play parent、graph/admission/death 仍为受控输入。实际 source site 记录为 **0**，没有 actual Add/queue mutation、非空 journal/reference scope 或 ordinary completion interval 证明。未安装/启动真实游戏，未操作账号；V7/83 exports/FirstInstall 准入、两层正式 reader 和正式 backend 无修改。范围沿用 [scope.md](scope.md)，前置合同与后续依赖见 [NEXT.md](NEXT.md)。`flavor = null`。

## 实际执行的变化

旧 joined fixture 在 native Play callback 内调用 `_work.Begin(Play)` / `_work.End`。本包的 metadata 中准确 Play entry（自有 SEC_IMAGE `module+0x3956f0`）改为跳入 native `Void0/25` entry；native owned Play original只计数和读取 source 状态，不回调 managed OnPlay。实际 Before/Returned 经 `NativeBootstrappedOperationBridge` / `ExactHookBroker` 自动调用 owner CaptureOperation/CompleteOperation，broker Dispose 该 context。没有人工插入 inner Play frame、source record 或 occurrence。

编译 `OperationContext.cpp` 时去掉全局 admission fixture 宏，保留其原样生产 source Enter/PrepareExitReferences/Exit 分支；仅 `TweenAdmission.cpp` 使用原有 `RIZ_TWEEN_ADMISSION_FIXTURE`，`TweenSourceContext.cpp` 使用原有 `RIZ_TWEEN_SOURCE_FIXTURE`。全部在同 DLL 链接，不改生产宏定义或发行 ABI。source transport读真实 `GetInfo`/`ReadRecord` exports并接入已有controlled reader/root set。完整 source 43 binding 的注册身份受控，CALL return labels为自有ASM，本站点均未执行。

outer parent是显式受控Play，fixture只手工保留这个外层上下文使Single membership成立。它创建的InstantWork 1与actual inner Play创建的InstantWork 2分别存在；私有transfer只含后者，不能把outer受控输入算作第二个实际native Play。

| 观察项 | r4实际结果 |
|---|---|
| actual Single original / closed listener / Play original | 各1次 |
| actual Play lane | kind0/slot25，epoch1，entered=exited=1，in-flight/unknown/enterFaults/exitFaults均0 |
| actual Play frame | native sequence2，arg0=arg1=0，Returned exitFlags1；显式保存self/MethodInfo/caller，避免字段struct默认JSON空对象 |
| native source active | actual Play Before=1，owned original=1，After=1，native返回后=0 |
| native source | epoch1，configured/metadataBound=1，bindingCount43，Unknown/Faults/PendingTransactions均0 |
| actual source site记录 | events/transactions/completedTransactions/retainedRecords均0；不视为非空source验证 |
| actual Delivery / production consumer reads | 3 / 2 |
| 私有transfer | child1 → PlayObservation2 → SourceEpoch1/JournalCutoff0 → occurrence `[2]`，该条为InstantWork |
| ordinary completion / root frees / Unregister | 未借完成区间 / 0 / 0 |

`Production`在reader/source replay中保持false，`actualNativeAdmissionVerified=false`。native admission policy未配置claim，原completion fixture的lease仍是受控事实。整体完成、Unity death、实际官方metadata、FirstInstall Ready和生产game objects均未由本结果推导。

## 证据与复现

观察时间 `2026-10-30T10:10:55.785771+00:00`。新进程只运行一次最终r4模式，旧7进程/39项/订阅/native基底未重跑。

| Evidence | 原件，相对最终D目录 | SHA-256 |
|---|---|---|
| E-AUTO-001 | `test-result.json`，含逐TU flags、命令、actual exits、实际frame/source/transfer JSON | `b40defdc2893d378a9ab1359330d9ad38480c430a2e31204da8ad56129a0e1a1` |
| E-AUTO-002 | `source-snapshot.zip`，660个完整源码输入 | `d1b4bf9b17b68ddc998448bb66ad4a5e459e12a489c860a7c76f63da7fee2f22` |
| E-AUTO-003 | `native/RizToolbox.SinglePlayAutomaticFixture.dll` | `ae75f01516c132264e84ff6da7917ab5362e4b9963813a6a7ba6cbaf7262d37e` |
| E-AUTO-004 | `source-before.json` = `source-after.json` | `121d36a50dbe3e81afdc43177d597a165b3afc2285ebc5e1f41065637f9e016e` |
| E-AUTO-005 | `overlay-inputs.json`，7项production native依赖+3项fixture/runner | `b4bd31006d22f31726e2cf4a43dab6dc59fa9aa1ec238c0f28d0b46d5121bbf4` |
| E-AUTO-006 | `automatic-instant.stdout.log` | `fd6c1aec9d7d268deedbd5b1330e13af060aaf401a648533a07595535fa0d195` |

测试程序集SHA-256为`bb4ac17182c39ace87290e71a3c00c21bc38355d093bf742714128dd0a2ad3e5`。所有具体native编译单元flags都在E-AUTO-001；OperationContext没有admission fixture宏，native source生命周期没有被编译掉。D工作输入：

| 输入 | SHA-256 |
|---|---|
| `single-play-automatic-work-20261030/SinglePlayAutomaticFixture.cpp` | `9733c32010b190be0a769a14d41be7a6c4ae15613c5446eb26a51839f0e0c89f` |
| `single-play-automatic-work-20261030/NativeTweenSinglePlayAutomaticTests.cs` | `4aa00d77ebe40580c7cf84095b2fb828fac529c5d1cb58a1049bbb06251afc2f` |
| `single-play-automatic-work-20261030/run-automatic.py` | `b5f9456c98fb50db745053b38d5076909d85a32e0b875e4b63e0df99bfa3c5cc` |

```powershell
C:/Python314/python.exe D:/riz-build-cache-next/single-play-automatic-work-20261030/run-automatic.py --output D:/riz-build-cache-next/single-play-automatic-replay-20261030
```

输出目录必须不存在。使用本机已有.NET8/Python/VS2019 BuildTools，固定基底为`single-play-joined-frozen-r3-20261030/source-snapshot`；runner读取已列明的7项native新增依赖，冻结后再编译，不包含其他owner的live改动。source archive中也保留runner和fixture新源码；两个已有测试文件的受控transport参数/命令路由改写仅发生在D快照，完整最终字节见E-AUTO-002/E-AUTO-004。

r1失败保留：native exit0，managed exit1，仅新fixture遗漏IDisposable→OperationContext cast（CS1503）。r2失败保留：native/managed exit0，进程exit1，Single配置沿用epoch1而实际先配置Play后Single为epoch2；修为实际GetInfo读回，未削弱生产校验。r3三个actual exit0，但struct frame默认JSON为`{}`，r4增加显式frame/native lane字段后重新冻结通过；r3不追加为新覆盖数。四个目录全部保留，未改写失败原件。

F-AUTO-001（validated，category=other，severity=n/a_re，confidence=high，evidence_ids=[E-AUTO-001,E-AUTO-002,E-AUTO-003,E-AUTO-006]）：actual closed listener可通过真实native Play operation进入未改的source生命周期，并使原私有账本为该actual Play签发独立InstantWork transfer；受控部分如上。

P-AUTO-001（path_type=callflow）：自有Single entry → actual Mid/Pre TLS Delivery → 正式Single reader/production Play consumer reader → 自有SEC_IMAGE Play跳转 → actual Void0/25/source Enter → native Before/owned original/Returned → source Exit → actual Single Returned → private transfer occurrence2。各步由E-AUTO-001/003/006的实际计数和E-AUTO-002的固定源码对应。它保留ordinary occurrence义务，不证明非空native queue/source/admission或最终释放。
