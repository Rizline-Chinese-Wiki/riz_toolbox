# 返回 ABI 2 与协程身份关联：有限复核

2026-10-25。本 agent 是 native transport、原生 fixture 和早期 CLR 联合 runner 作者，对这些文件执行作者自查；对本批 CLR 宽度规则、owner 投影和 identity ledger 执行有限只读交叉复核，不能称独立第三方全审。采用 docs-generator，flavor=null。root 是唯一 builder；本 agent 未构建、执行 fixture、安装、启动游戏或操作 UI，也未修改本批 565 项冻结源码。

本批最终候选是 session **51261**，root 已实际观察 terminal exit 0；本 agent 实读 wrapper、完整日志、正式原件及归档，独立重算 **25 套、1617/1617**，逐项核验 **565 源码、22 暂存包文件**与 ZIP/current hash 一致。修前 session **48324** 虽然也真实通过 25/1617，但其 foreign-mask 用例期望过弱，不能作为修后通过证据；原件完整保存在 [before-mask-fix](before-mask-fix/build-run.json)，finalCandidate=false。

有限复核未发现本次修正范围内新的确定阻塞缺陷。此结论只覆盖[本批范围](scope.md)：Execute 保留完整 uint32 EAX、拒绝新桥采用旧 helper，以及 copied scalar identity 的有限关联。完整 B01–B19、引擎四入口安全首次发布、真实游戏对象/GC、scheduler/future Start 封口和取消完成仍未验收。`EngineCancelled=false`，Pending 保留，RequestPlay 不开放，新包未安装。

## Evidence

source_type=file/log，observed_at=2026-10-25；source_ref 与 artifact_path 为下表链接。以下 hash 均由本 agent 现场计算。源码清单和归档只证明实际声明输入一致，不等于依赖、工具链与完整 B19 矩阵的干净重现。

| E-id | source_ref / artifact_path | SHA-256 | 实际复核范围 |
| --- | --- | --- | --- |
| E-native-api | [EngineNodeContext.h](../../../native/RizToolbox.JudgeContext/EngineNodeContext.h) | `92115b92c0324b1038b855fe783a85f8d5d8f4aabd5f2f73629c645fbb99295b` | 返回 ABI 2；记录版本 1、Frame112/Completion32/Info112 不变 |
| E-native | [EngineNodeContext.cpp](../../../native/RizToolbox.JudgeContext/EngineNodeContext.cpp) | `e75de998e6f34ea708ee603993fa6f83e72ba8564050666c5348ba6b4884b6a5` | Execute 原调用、存储和 entry 均 uint32；native finally 与不读 After node |
| E-types | [NativeEngineNodeContextBridge.Types.cs](../../../src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.Types.cs) | `c248f5eb854d28a6be5b371cce0bf003d57fba8939d7aa043e5bdf8d309b4bbf` | Execute delegate uint；RawReturn 按 kind 校验宽度 |
| E-bridge | [NativeEngineNodeContextBridge.cs](../../../src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.cs) | `97dcc6647787ed8b2d6a0f768eab2f3a2f68a80cf2cfed1bc209d6d5e19f6e85` | 必须 GetAbiVersion==2，actual immutable identity 和一次 Attach |
| E-owner | [NativeRuntime.UnityCoroutineOwner.cs](../../../src/RizToolbox.Runtime/NativeRuntime.UnityCoroutineOwner.cs) | `22264a2b0ee085b802ae9f812916fed0382468bb7a4332e0718dbff2f34c776a` | 借用已保留 receiver root；正式 Object.m_CachedPtr 声明、类型、+10 与前后标量复核 |
| E-identity | [NativeEngineCoroutineIdentityLedger.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeEngineCoroutineIdentityLedger.cs) | `6820c398cbc009c00d1dc77aa22a83f76081cd432cad1df47bf92aa1bac456c3` | owned/foreign 分流、物理 mask、provider 重入/代次/handle 升级、修后责任保留 |
| E-identity-tests | [NativeEngineCoroutineIdentityTests.cs](../../../tests/RizToolbox.AudioContent.Tests/NativeEngineCoroutineIdentityTests.cs) | `1677172cd30d5671bb149654dcb7418aa883d48da2905262e79fbbd9f8e2347f` | foreign-only/owned masked 两种新断言；已有 tracker 使用受控 backend |
| E-joint | [NativeEngineNodeManagedRegression.cs](../../../tests/RizToolbox.Runtime.Tests/NativeEngineNodeManagedRegression.cs) | `4003ac267ca6c185a98e899b20cfe593484adac4e1cbbee4916ee878774c88e2` | 实际 Dobby/helper/CLR/node ledger/identity ledger；provider pair 受控 |
| E-install-research | [engine-installation-research.md](engine-installation-research.md) / [实际 bytes/RF/引用 JSON](research/engine-installation-evidence.json) | 文档 `304c58a5a4120d436e39e776cf72449ce08fa4885f96251da3be19c4c8a782da`；JSON `c44de32c5abdc88b5f75d34c056bf4972f0bda7429505676bd6c2a5c08e2441b` | 固定磁盘启动路径、owner+10、完整 EAX 比较；静态证据 |

本 agent 另核 native fixture 四文件及 runner 与冻结值一致：[fixture header](../../../native/RizToolbox.JudgeContext/fixture/EngineNodeManagedFixture.h) `e3b371f437f01f384a73d018e81fe597f289ab9e6fdba444ef3b62736daecad5`，[fixture DLL source](../../../native/RizToolbox.JudgeContext/fixture/EngineNodeManagedFixture.cpp) `4f5779304cc5ba0c04e07f2fa439c155341ec333eb2ec145d3b71ce014095451`，[fixture EXE source](../../../native/RizToolbox.JudgeContext/fixture/EngineNodeFixture.cpp) `33d080192ce6322162e2c76498c25b287de027670a0f1ddfad02feda49234325`，[native runner](../../../scripts/Test-EngineNodeNative.ps1) `031f8a64c2cd2abccfd86259791612a72d94762d43a795db6cac02603aff3f92`。Result208 布局未变化，旧字段名 returnByte 在 Execute 下承载完整 uint32，不是 bool。

## Findings 与路径

severity=n/a_re，category=design。source-reviewed、自有实际 fixture、受控 metadata/provider 和真实游戏验收是不同层，不合并为生产 Ready。

| F-id / status | evidence_ids / location | 结论与限制 |
| --- | --- | --- |
| F-eax / source-reviewed+validated 自有 native/CLR | E-install-research/E-native-api/E-native/E-types/E-joint | 固定 scheduler pump +447EEA 调 callback、+447EF2 `83 F8 01` 比较完整 EAX；+78F5E0 tail-jmp Execute +78F840。只保 AL 不足。现在 Execute 原调用、result、entry 和 CLR delegate 均 uint32；Start/Release 仍 uint8，Detach void，无 MethodInfo、无 bool 归一化，也不声称完整 RAX/异常 flags 透明 |
| F-version / source-reviewed+validated 自有 native/CLR | E-native-api/E-bridge/E-types | GetAbiVersion 返回 2，bridge 缺 export 或非 2 即拒绝。标量头仍 Version1；新桥不会仅凭相同 Frame112/Completion32 误采用旧 byte-Execute helper。现场 PE 实读最终 helper 共 59 个 export，其中 EngineNode 八个 |
| F-copy / source-reviewed+validated 自有 native/CLR | E-native/E-native-api/E-joint | Before bounded copy 的 owner+58、enum handle+10/type+18/cache+20、ref+60 均为已复制标量。故障保实际 node、其余清零和 Unknown；正常 Start After 仅读实际 R9 输出槽，绝不读输出 node。所有其他 After 不读可能已 free 的 node；ref 不是执行深度 |
| F-unwind / source-reviewed+validated 自有 native/CLR | E-native/E-joint | 原函数一次，SEH/C++ 沿 native finally 展开，native catcher 在 P/Invoke 返回前捕获；原异常保 native Unknown，包括未 Attach 的历史。observer 故障被包含，已有 Complete/Dispose 责任仍配对。永久槽/roots 和只读 adopted 通道没有生产首写凭据 |
| F-owner / source-reviewed+validated 受控 metadata | E-owner/E-install-research | Start icall +1029F0 的实际 root receiver 经 +102AB2/+102ADD 读取 receiver+10 native cached pointer，+102B0F 把该值送 RCX，+102B12 调 +792240。新 Runtime 校验 Object 自身字段而非同名子类字段，从保留 root 读取；不解引用 native owner/node、不在 null cached pointer 时执行 fallback。受控 metadata 回归不证明真实游戏 root、销毁竞态或 first-install 时序 |
| F-identity / source-reviewed+validated 受控及自有联合 | E-identity/E-identity-tests/E-joint | receiver/native owner 分离；identity key 保留 provider owner、receiver、slot、tracker generation、Enumerator，与 node 的独立 engine generation 关联。Start 内实际 Execute 可先于 Start 返回建关联，handle 仅在相同记录允许的方向延后升级。provider 读取前先登记 scope，重入/溢出/不唯一/错父子均 sticky Unknown；没有引擎调用、根释放或取消授信 |
| F-mask / fixed+validated 受控+历史回放 | E-identity/E-identity-tests、下文旧源码 replay | 自有未知 node 遇最近 foreign Start 时，仍 relevant 并先登记 Call；随后置 Unknown，NodeCall/Identity 均空，不读 provider、不借外层 Start、不造 node generation。现有精确 scope 可 Complete/Dispose，Outstanding 可归零，但 Unknown/Pending 不解除。纯 foreign 协程仍不读 provider；已关联 live node 可继续用本代，已 drain 的历史裸地址不借旧身份 |
| F-production / incomplete | E-install-research/E-owner/E-identity | 当前 Doorstop pre-il2cpp_init 不能证明此前 UnityPlayer 四入口未执行；GameAssembly 未初始化不等于 UnityPlayer 无历史。静态 TLS/CRT/UnityMain2 研究未闭合最早安装证明，不首 patch 游戏。下一步 [早期加载自有合同](early-loader-design.md) 仍需实现；[scheduler/cleanup 研究](scheduler-cleanup-research.md) 未替代实际完整责任链 |

P-native：自有 DLL 四 target → actual Dobby Prepare → immutable helper Configure → actual Commit → typed entry → nullable observer 单次快照 → Before copied scalars → original once → copied completion → Exit。8 个完整 EAX 用例包括高 24 位非零且 AL 为 0/1/80/ff 的值，另含 observer fault、free-node 和 worker；nested Execute 实际返回 `0xA5C3FE81`。因此证明的是原始 uint32 透传，不能由 AL 重建或 bool 化。

P-joint：相同实际四 target → helper ABI2 → bridge AdoptConfigured/Attach → 四 dispatcher → 一个共享 actual node ledger + peer sink 的同 frame/complete。联合 runner 另把相同实际 copied frame 送入实际 identity ledger；每个 Start 的受控 IdentityWork 创建自己的 fresh wrapped node ledger，provider receiver/pair/slot/tracker generation/handle 为受控值。此支路确实执行 identity ledger，不代表实际三类 tracker backend 或真实 IL2CPP metadata 已在同一 native 链运行；其 `actualTrackerBackendExecuted=false`、`actualIl2CppMetadataResolved=false` 正确保留。

## 修前候选与最小修复

本 agent 实际打开修前 [565 源码 ZIP](before-mask-fix/source-inputs.zip)，逐项核 CRC/hash，和当前冻结输入比较仅 **两个**文件不同：identity ledger 与 identity tests。修前 ledger hash `8c90dfb75a8d6d58cfd4c370c8495067205ffc9118f9648ac672bff4ee1be185`，修前 tests `898f1c8162640fba8ecd0e9668142c30114efe174f52c9a482ea7ae334a06dda`。

原实现把 nearest foreign Start 纳入 relevant 过滤，因而 owned 未关联 node 可能在登记责任前被排除；原 Foreign 测试还断言其 Unknown=false，所以 48324 全绿不足以发现该语义缺陷。本次只移除这层 relevant 过滤，并在 `_calls.Add` 后、node/provider 回调前加入 owned unknown-node guard；测试改为检查 outstanding 责任和 sticky Unknown，并补无 owned 外层 Start 的同类场景。没有改 native 六文件来掩盖问题。

root 运行的[隔离历史回放结果](mask-before-replay/20261025T034151Z-6b979b54/result.json) SHA `5936c4003a15c2807123eaca37f44258472327ed0de535d39159ca6d14f8217a`：将归档旧 ledger 与当前期望编译进隔离自有项目，确实触发 Foreign 断言，`redObserved=true`。回放 harness 捕获预期失败后 exit0，不能把它称原用例成功或共享项目的一次失败构建；`sharedProjectsRebuilt=false`。本 agent 实读 stdout/result，逐项核生成文件和四份日志 hash 相符。修后 [Audio 专项日志](mask-preflight.log)为 640/640，最终 51261 另完整重建验证。

修前原 log SHA `a3523d6565a66a770de8151fc5db98b6088d61aae4ab9df08fd936c9593af7aa`，source ZIP `a0f547649f03112235e6cf8a29ba62644c7d5c61616c61e5288f220b759823cc`，package ZIP `c472353d7bded439b8842b00234abd54982f6da6df82ca3328a156f36b3aa2bf` 均现场重算一致；保留为真实历史，不写成最终修后证据。

## 最终终态、动态原件与归档

root 已实际观察 51261 terminal0，独立 [wrapper](build.log.run.json) exit0 和 [build-run](build-run.json)一致；本 agent 不把等待中的进度或日志尾部代替终态。原始 [build.log](build.log) SHA `7e86bc8daeb0ff0aa5bfec9f368731dcf146f6d0d8e8d54e5e1b14b636a1a2c7`，独立提取 25 个 suite summary 合计 1617/1617，和 [build-summary](build-summary.json)一致。其中 Runtime177、Audio640；相较旧 1597 的增量20是受控托管 suite 用例，native/CLR fixture 检查数不混进该总数。

本 agent 实读最终 cache `D:/RizToolbox-build-cache/engine-binding-mask-final-20261025/engine-node-native/fixture-result.json` 与 `engine-node-managed/fixture-result.json`；发布的正式 evidence 与 cache JSON 对象逐项相同。现场 PE 读取最终 helper 59 exports，EngineNode GetAbiVersion 等八项齐全，没有加载 DLL。

| 正式证据 | 现场结果 | 限制 |
| --- | --- | --- |
| [native evidence](engine-node-native-evidence.json)，SHA `9e95f70b7fd327c8d1ab2f7ecab692596e9ab62648ec92c5469543c722e9db6b` | 3402 检查；136 originals，124 Enter/Exit，12 无 observer；16 原异常、34 observer 异常、17 worker、8 显式 nested、6 free-node；8 full-width cases/10 completions | 自有函数/页；无 game code、first-install、并发首次发布、取消完成或完整 RAX/flags 透明证明 |
| [managed evidence](engine-node-managed-evidence.json)，SHA `c62f2eff0954ab8569e636c7bab0491dd0045e0e468cf0902eccb1f854cfb112` | 5 独立进程、889 检查；44 originals、28 actual node scopes 和28 peer Complete/Dispose；所有 Outstanding0、Pendingtrue、EngineCancelledfalse | providers/metadata 受控；没有 tracker backend、生产 object backend、生产 EngineNode 挂接、scheduler/future Start 封口或游戏执行 |
| [565 源码 ZIP](source-inputs.zip)，SHA `16c091efadc8718ec708e2f676c1511ba1775f932d6d13bba12327a51824e6e7` | 565 条目 names/CRC/逐条 SHA，与构建前、[manifest](source-manifest.json)和 current 全同 | manifest SHA `4dc6470edcfc07a057b9d7a7ba1edf6c77aa2acd3acb3845a57bbc6badd29858`；不冻结全部依赖和工具链 |
| [22 文件包 ZIP](staged-package.zip)，SHA `801c83bdf788156b7f4fd281e8395a399d2262d3d17a38a7afa3106db9da992d` | 22 条目 names/CRC/逐条 SHA，与 [package manifest](package-manifest.json)和 current staging 全同 | manifest SHA `dfbdb5f0a4849ded5d67e8ee5f221918e32e01e35c2912b24232a682aa2126fc`；staged，未安装 |

托管五 mode 现场重算：normal227/9 originals/9 scopes，original-seh158/8/4，original-cpp154/8/4，detach203/11/7，observer-fault147/8/4。normal 和 detach 的 actual identity chain 在 Start 退出后由受控 provider 升级 handle；原异常和 observer-fault 保 Unknown。完整 EAX 值 `0xABCDEF01` 与 `0xA5C3FE81` 实际穿过 native/helper/CLR completion；不能仅用低 byte 满足断言。五 mode 的 firstInstallReceipt、生产挂接、实际 metadata/backend、scheduler/futureStart、engineCancellation、game/installed 均 false。

本 agent 另现场重算 [旧安装18项](previous-installation-check.json)和[原游戏3项](original-game-check.json)无差异；旧安装对应的 326 条源码 ZIP 实开核 CRC/逐项 SHA，相同 archive SHA `d38b6630002cc680924b4313c758ec05189e0a4edd685b3f6018183ade5f307f`。这些只证明旧安装和磁盘文件未变化，没有替新包取得游戏验收。[相较上批变更](changes-from-engine-node.json)为 +4/~14/-0；565 源码冻结核验全同。

后续仍须证明更早的实际加载/发布闭包，再把真实 receiver root/metadata/provider 与正式生产通道接成同一条链；完整 scheduler、异步旁路、Start 内联清理、future Start 和全部消费者退休也需逐项关闭。自有绿色 fixture、固定映像研究、旧 V3 mapped publication 和未来游戏冷启动继续分别记录，不拼接为 EngineCancelled 或完整底层完成。
