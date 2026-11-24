# 下一包：自动 Play operation → native source 生命周期 → 私有账本

2026-10-30 只读审计确认：正式 backend 已采用 joined Single reader 与 Play consumer reader，未发现需要改写现有 wiring 的遗漏。下列合同的最小 `automatic-instant` 已在全新 D 盘 snapshot 有限执行，去掉了 inner Play 的人工 Begin/End，结果见 [AUTOMATIC-INSTANT.md](AUTOMATIC-INSTANT.md)。live 生产/测试源码 **0 改动**，V7/83 exports/FirstInstall 准入不变。actual Add/非空 source site/queue 联合仍待后续实现；新正向进程不并入旧 r3 七进程计数。范围沿用 [scope.md](scope.md)，历史见 [timeline.md](timeline.md)。`flavor = null`。

## 当前正式采用与 hash

以下 live 文件均已读取，SHA-256 全部等于 `D:/riz-build-cache-next/single-play-joined-frozen-r3-20261030/source-before.json`：

| 文件，相对仓库根目录 | SHA-256 | 已采用行为 |
|---|---|---|
| `src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.TypedEvents.cs` | `5e2a3059f83a05850f8f4255927949de4f43205ecc6739fae50c57a1f1acbfd0` | binding → `BindSingleExecutingSource`，Capture 借实际 physical parent |
| `src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.SingleDispatch.cs` | `4ce57fca711e8571c84aa4d4d852ba789d682070a9941e7ed174cd5c10aaf157` | `AdoptReady` → `RequireProduction` → callback → shared reader |
| `src/RizToolbox.Adapter/AudioContent/NativeTweenSingleDeliveryReader.cs` | `1eeef17a38a095117c3284d07fbbeb7fcb45585aad8ce41a54dd2aa007bca402` | actual Delivery、metadata 前整批 root、retained Before completion |
| `src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.TypedListenerEffects.cs` | `8a830ff85fcf11fd381738ee0407d7fd39dd12c66280977c9491fa1dd3e6024c` | 正式 effect backend 直接调用 Play consumer reader |
| `src/RizToolbox.Adapter/AudioContent/NativeTweenTypedPlayConsumerReader.cs` | `191fe5300500cc4e9739c694995f796e1ec660e217fd641680b02b47434d8779` | rooted InvokableCall/UnityAction/Play MethodInfo 投影 |
| `src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.TypedListenerEffects.cs` | `6c9a88cc3a9af6c97febfffff93783ba9684260b6966c393723328efb7b97847` | exact child、唯一完整 Play subtree、原 occurrence 事实后签 transfer |
| `src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventBridge.cs` | `f4a39d4196e8deb3a5279e7e3dafe2a1974f190ca3459649fda2d5aeff8a64a8` | production ctor 捕获真实 `NativeFirstInstall.VerifyReady`，采用 production singleton |
| `src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventBridge.Controlled.cs` | `64c51bba67154e43da4b1573b4141f0dd9bbe99492fba9669bf6e3b18906601a` | 共用 TLS/Delivery 构造，不填生产 singleton，不能通过 `RequireProduction` |

已有 r3 `test-result.json` SHA-256 `899f44e481a9402f7cbfcc0fb35e135a44121e56efbda21e71f5fa29d9b5da00`，观察时间 `2026-10-30T09:10:43.931093+00:00`。native/managed build 均 actual exit 0；identity、instant、duplicates、withdraw、unknown、missing-post、copied-read 七独立进程均 actual exit 0 且合同通过。本次没有重跑。source-before 清单 SHA `ee447b28a58dcee7c0408bf1598b309c0fb114b973be286acdedea6440718e93`；archive SHA `5ea28f083cbc16eadc107e39340befadb1ac8a9d9a40e860bc49c227a1dbfd35`；overlay 清单 SHA `c748b943465fb960714ea95fa90d7782c714f5dee786c87c088a1cff48f93822`。原件见 [evidence-index.json](evidence-index.json)。

## 实际缺口

[`NativeTweenSinglePlayJoinedTests.cs`](../../../tests/RizToolbox.AudioContent.Tests/NativeTweenSinglePlayJoinedTests.cs) 的 `OnPlay` 仍手工 `_work.Begin(Play)` / `_work.End`，duplicate 模式还人工 Begin(Add)、SetReady、End(Add)。outer Update/Execute/source 也由 completion fixture 提供。

[`run-checks.py`](run-checks.py) 给全部 C++ 单元统一传 `RIZ_TWEEN_ADMISSION_FIXTURE`，将 `OperationContext.cpp` 中 source includes、Enter、PrepareExitReferences、Exit 全部编译掉。`SinglePlayJoinedFixture.cpp` 的 `RizTweenAdmissionFixture_Validate` 固定返回 false，native DLL 明确没有 native claim。这些是 fixture 边界，不能宣称生产代码故障。

## Producer 入口与可复用实现

所有 native producer/source/references/admission 必须在同一新自有 DLL，使用同一 TLS/epoch。新 runner 逐编译单元记录 flags：

| 单元 | 下一包方式 |
|---|---|
| `native/RizToolbox.JudgeContext/OperationContext.cpp` | 不传 `RIZ_TWEEN_ADMISSION_FIXTURE`，原样执行 actual admission Enter → source Enter → managed Before → owned original → PrepareExitReferences → managed After → source Exit → admission Exit |
| `native/RizToolbox.JudgeContext/TweenAdmission.cpp` | 只在本单元使用该既有宏，Ready/metadata 受控；真实 Claim/Read/Seal、slot/epoch/计数保留 |
| `native/RizToolbox.JudgeContext/TweenSourceContext.cpp` | 使用既有 `RIZ_TWEEN_SOURCE_FIXTURE`，只替代 metadata 校验；真实 ticket/pair/journal 保留 |
| `TweenSourceReferences.cpp`、`TweenSourceEntry.asm`、Single context/ASM | 链接当前源码；仅 fixture DLL 可加私有 source configure/attach wrapper，不能改生产导出合同 |

复用 `SinglePlayJoinedFixture.{cpp,asm}` 的 actual Mid/Pre/Post/closed delegate、`NativeTweenTypedPlayNativeReaderTests.cs` 的 unmanaged metadata/owned SEC_IMAGE/root、`NativeTweenSinglePlayJoinedTests.cs` 的 Delivery/physical scope/正式两层 reader。actual Play entry 为 `Void0/25`；metadata 的准确 Play entry（owned SEC_IMAGE `module+0x3956f0`）转到该 entry。bootstrap actual Target/Original/Entry/Epoch 必须读回。Before/After 经 `BindConfiguredNativeOperationObservation` / `NativeBootstrappedOperationBridge.AdoptBootstrap` 自动向 owner CaptureOperation/CompleteOperation 提供真实 frame/flags，context 由 broker Dispose。managed OnPlay 不再调用 Begin/End。

source transport 从同 DLL 的 `GetInfo/ReadRecord` 读真实值，使用现有 `NativeTweenSource.CreateControlledForTests` 及 root set；Production 保持 false。native source registration/metadata、admission 和 outer parent若受控，逐项单列。Play Before/original/After 内观察 source active count，返回后恢复；不能手工调用 source OperationEnter/Exit。Instant Play 没有 source site 行时，必须记录 `actualPlaySourceSiteRecords=0`，不能虚构 PlayOnStart/Complete 行或以未观测空 source 认完成。严格 Pending 必须原样保留并报告。

native admission 若实际验证，复用 `TweenAdmissionFixture.cpp` 的 owned metadata/Claim/Read/Seal，transport读真实 native exports，不以 `CompletionAdmission.Current` 写成功；若沿用 completion fixture lease，标记 `actualNativeAdmissionVerified=false`。本包不做 Unregister/退休。继续使用 reservations-only retirement基底时也不得签释放结论。

非空 source/duplicate 是后续增量：复用 `TweenSourceFixture.cpp`、`TweenSourceFixtureCaller.asm` 的43个 actual CALL return labels、owned queue、store+barrier，但 parent必须由 OperationContext自动产生。outer Update为Void0/24，Execute为Ref1/19，Single为Single1/31，Play为Void0/25，Add为Ref1/20。需要足尺寸 manager/Tween/queue，当前metadata fixture的0x80字节PlayObject不能承担onStart@0x88/onComplete@0x90/onTween@0x98。Before/After图读当时native存储，不用SetReady写期待图。新引用通过actual ReadCurrentNotice和`BindControlledReferencesForTests`进入reader，不能由copied journal行晚补root。

## 最小 positive 验收

一个 fresh `automatic-instant` 独立进程实际执行 Single/TLS/Delivery 与两个正式 reader，并由闭 UnityAction 调 actual Play lane。Play entered/exited、Before/Returned 各一次，无 skipped/abnormal；self/MethodInfo符合exact consumer；native source active在callbacks内增加、返回后恢复，Unknown为0。私有transfer必须引用该actual Play observation与独立InstantWork，原ordinary ledger义务保留。保存actual frames/flags/native counts/source activity与reader/transfer快照。source registration、outer Update/Execute、非空source site、admission/death仍受控的项目必须分别声明。

不允许直接构造transfer、补source occurrence/journal行、写已完成条目或伪造NativeFirstInstall。不能通过修改正式backend绕过production检查。`productionFirstInstallVerified`、`productionGameObjectsVerified`、`productionReadyIssued`、`installed`、`gameExecuted`均false。若正向触发严格Pending，保留实际失败目录并报告缺口，不补人工事实凑通过。

下一增量`automatic-duplicates`应由actual Add产生两次真实append/count/version变化，再由两次bounded native Update各消费一个队列项；可复用source站点0/2/14/6。不同admission/occurrence ID指向同Tween，第一次退出仍Pending，第二次才全部退出。未执行站点不计覆盖。

## 新冻结依赖

当前OperationContext.cpp SHA `155f39c2dd2e6e6bfbd105562c681fbfa6b1bac86b004adcf94c90062d04461c`；TweenAdmission.cpp SHA `8d03dc0e5714e0b9efa28fa4ac0b3c95998d2d33a0d5afd00e216e73b71d71fb`，均等于r3。下列文件不在r3 source-before清单，本次只有静态hash：

| 相对仓库根目录 | SHA-256 |
|---|---|
| `native/RizToolbox.JudgeContext/TweenSourceContext.cpp` | `592d99156072795790adb253231287fe9e2f5625b52ed43770f612e10ff14389` |
| `native/RizToolbox.JudgeContext/TweenSourceReferences.cpp` | `aa8aef86c04e1cc821001e0b484028e625f8a7c28b54eb5e94539c5e68c8dabb` |
| `native/RizToolbox.JudgeContext/fixture/TweenSourceFixture.cpp` | `85e8594fc376ae17d2c30c29f93b492ba2088d0b590f33adcee42c14885ac73b` |
| `native/RizToolbox.JudgeContext/fixture/TweenSourceFixtureCaller.asm` | `84ea77b092ae40d6b2f1040b7d52d87fe24bbb4afddd16aba9ef6aecb0acd974` |
| `native/RizToolbox.JudgeContext/fixture/TweenAdmissionFixture.cpp` | `2bfcb8df27917bbf0163baad34d7fee73033357aac40c0f07f5a2a7c555c4cfd` |

新runner需冻结实际使用的全部源码/header/ASM/C#/project及自身，不能把旧25overlay当完整native输入。新D目录须原先不存在；保存source archive、前后hash、逐TU flags、build与每进程stdout/stderr/actual exit，失败原件保留。旧7进程不计本包新增，root独立统一compile当前全树。

Evidence→Finding→Path：E-NEXT-001为上述r3 test-result/source/hash（已有执行）；E-NEXT-002为本页current source/hash（只读静态）；E-NEXT-003为[AUTOMATIC-INSTANT.md](AUTOMATIC-INSTANT.md)列明的D-only r4结果与源码。F-NEXT-001（validated/other/n/a_re/high，evidence_ids=[E-NEXT-001,E-NEXT-002]）：正式reader/constructor已采用。P-NEXT-001（callflow）：actual Single→TLS/Delivery→正式reader→actual Play callback/source生命周期→同owner私有InstantWork transfer，已由E-NEXT-003有限验证；非空source站点、native Add/admission/queue与ordinary完成区间仍未验证。
