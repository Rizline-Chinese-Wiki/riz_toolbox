# S6：同一物理安装上的跨局 logical handoff

本批完成受控私有 Router 的跨局条件交接：正常两局、GetTime 在途、source 在途、native cut 后 source 进入、sticky physical fault 与超过 64 次交接都已实际执行。最终六进程 1390 检查、既有 native bootstrap 11 case / 388 检查均 actual exit 0；独审另有两进程 47 检查，分列不相加。全程没有安装、启动或操作真实游戏、UI、账号。

范围见 [scope.md](scope.md)，过程与失败保留见 [timeline.md](timeline.md)。`flavor=null`；本报告是实现/验证报告，没有攻击目标、IOC 或 ATT&CK 归因。正式 Module/SDK 的稳定 provider 跨 run adoption 仍由后续联合包完成，不能用本私有 handoff 冒认其已通过。

## 实际行为

native 锁序固定为 GetTime `authorityGate → SourceOwner.Lock`，没有 managed callback 在该联合锁内执行。旧 exact publication conditional withdraw 后，实际 GetTime 和 source observer 计数同时为零才能分配新 logical epoch 与初始 revision。phase 2 继续关闭 source；新七根完整构造、旧源责任真实 drain、new route 注册后才允许显式 source-open。随后由真实新 Pause 建 baseline，初始 policy 保持 dormant，唯一 final Clock 直接消费 native admitted revision，再经两 gate 的零在途条件 arm。

旧 completion、旧 rooted source withdrawal/return 闭包、旧 Session、旧 exact publication cleanup、等值 authority copy 和 cloned/consumed native receipt 都没有 successor authority。正常换代不 Dispose/rebind physical lease，不清 registry、Unknown 或 sticky fault。policy node、logical session 和 receipt append-only；65 次实际移交的 epoch / revision / receipt 地址均不复用，整个进程 Dobby Prepare/Commit 始终 9/9。

native GetTime 在 cut 之后不捕获旧 policy/reader，仍 forward stock original；这条线不承诺阻止任意晚来的 stale raw receiver。native source dormant 原调用仍执行并前进 fence，但不发行旧 observer 票。physical Unknown 不是安全释放根的证据；sticky 模式恢复原字节后仍拒绝移交，未知责任七根继续保留。

## 执行结果

| 独立进程模式 | actual exit | checks |
|---|---:|---:|
| `normal` | 0 | 41 |
| `gettime-busy` | 0 | 46 |
| `source-busy` | 0 | 45 |
| `cut-source-enter` | 0 | 46 |
| `sticky-fault` | 0 | 19 |
| `long-sequence` | 0 | 1193 |

`long-sequence` 为 66 局 / 65 次移交，每局真实 Pause → 条件 transfer → dormant → 唯一 Clock arm → actual native GetTime；首局和末局还跑 SetSpeed、stock Replay、Pause/Resume 与所选速率媒体时间。`cut-source-enter` 在 successor constructor 的实际 DSP getter 内进入 native Pause，检查 phase 2、14 根及 observer 数未增加，证明构造 gap 的 source gate 仍关闭。

既有 bootstrap regression 11 case / 388 检查使用本轮实际执行的 `native-build-r2/RizToolbox.SingleReturn.Prototype.dll`，包含 dormant/arm/in-flight/SEH/reader withdrawal。managed PDB 五对 475 文档 checksum 与执行源码一致；native 三对 PE/PDB、六个 COFF 的 C13 source checksum 与冻结源一致。GetTime helper 为 21 exports，新 handoff 七 exports 均 executable 且有唯一 runtime-function 范围。既有 policy/stats/bootstrap wire 仍为 128/112/192，header 与基线相同；正式 V7 没有进入补丁。

独审 [REVIEW.md](D:/riz-build-cache-next/playback-stock-joint-handoff-review-r1-20261031/REVIEW.md) 的两个进程分别验证：constructor 内真实 dormant setter 使 readback 失败后 grant 已消费且不可重试、已知 drain 可释放根；source-open 前真实 GetTime body/page protection 变化后仍全局 sticky，恢复字节与保护后 native open 返回 3 / Unknown 1 / inactive。该物理 probe 先命中 page protection seal，未外推为 byte-only 检测证据。

## Evidence → Finding → Path

| Evidence | source_type / source_ref | SHA-256 | linked_workitem / supersedes |
|---|---|---|---|
| E-RUN：六模式实际执行 | log / [checks-r4](../checks-r4/test-result.json) | `3b5cd31b0375b21d7d9a87019a056536a4a00d81da97eb3f3bc6cbde601b17b1` | S6 / checks-r3 |
| E-SOURCE：772 源 / 23 文件纯增量 | file / [snapshot-r2](../snapshot-r2/test-result.json) | `bdf8efb1b6943a48cdd33ac65d4ebbc6117039e1dc83444265f4c4097ff7b2de` | S6 / snapshot-r1 |
| E-MANAGED：PE/PDB/source | file / [managed symbols](../managed-symbols-r1/test-result.json) | `d9066a180a589d1273ef3ef10c9415b480939652e01041bae487844115407bae` | S6 / none |
| E-NATIVE：native identities/C13/ABI/388 | file / [native symbols](../native-symbols-r1/test-result.json) | `1738443948cd2fa2a08c319706193200395fbd516122df06153bb36786412808` | S6 / none |
| E-REVIEW：两条独立失败边界 | log / [independent result](D:/riz-build-cache-next/playback-stock-joint-handoff-review-r1-20261031/test-result-r2.json) | `65572a9649a8ec0b6bb53d4889d55f2d9047a5c4df93c4eec554eda6e17138ac` | S6 / 独审首次 fixture 重编拒绝 |

这些 Evidence 的 observed_at、原始 artifact、完整 inputs/source/binary hashes 见对应 JSON 与 [manifest](completion.json)。原始 stdout 最后一行分别为 `passed=true`、对应 actual exit 0；旧失败日志和 runner result 均保留。

| Finding | severity / category / status / confidence | evidence_ids | location / impact |
|---|---|---|---|
| F-HANDOFF：双 gate 条件 cut 与 source-closed 构造可连续换代 | n/a_re / design / validated / high | E-RUN, E-SOURCE, E-NATIVE | `SingleReturnHandoff.inc:20`, `PlaybackSourceContext.cpp:470`；仅私有 controlled handoff |
| F-OWNERSHIP：旧精确票和逻辑通知不能撤 successor | n/a_re / design / validated / high | E-RUN, E-SOURCE | `NativePlaybackStockClockRouter.cs:47`；exact route/session/publication/cut 票仍受限 |
| F-FAILCLOSED：构造失败不回退重试，physical fault 不借 logical epoch 复活 | n/a_re / design / validated / high | E-RUN, E-REVIEW, E-SOURCE | `NativePlaybackStockClockRouter.Handoff.cs:87`；Unknown 保留真实责任 |
| F-BOUNDARY：完整 production/source generation 和正式同 Module 跨 run provider 尚未证明 | n/a_re / other / validated / high | E-SOURCE, E-RUN, E-REVIEW | scope 限定；S5 单 Session provider 需后续 adoption |

P-HANDOFF (`path_type=callflow`)：实际旧 Clock/publication（E-RUN）→ exact conditional withdraw（E-SOURCE/F-HANDOFF）→ GetTime 与 source 双零在途 cut（E-NATIVE/F-HANDOFF）→ new host run/七根与 append-only epoch/revision（E-RUN/F-HANDOFF）→ 注册 route 后 source-open（E-RUN/F-OWNERSHIP）→ actual Pause / dormant / 唯一 final Clock / arm（E-RUN）→ 第二局 actual native GetTime。残余风险为 scope 中的生产首次准入、完整 writer/source、正式 provider 跨 run、长期 retained 内存及任意 Seek。

## 冻结与复现

交付使用 [23 文件 patch](../snapshot-r2/changes.patch)、[changed-files](../snapshot-r2/changed-files.json)、[772 源 zip](../snapshot-r2/implemented-source-snapshot.zip) 与 [精确 before zip](../snapshot-r2/before-source-snapshot.zip)。source zip SHA 为 `7083acad187aaf6eba8d7c78c5a502062c7e6a362a8c9dc68f0c9f722844e06e`。baseline 为 S4 zip `8d8f6e...` 的 756 源、共享 Bridge `79e4947...` 以及四个既存 native fixture 文件；S6 patch 排除共享 SEH 修复和 S5 Module 回退。S5 已合主树的 Router partial/Open/Pump/Withdraw 修改须三方保留，不能用整树 zip 覆盖当前主树。

实际命令（在本 case/source 下；OutputDirectory 必须为新的不存在目录）：

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' build tests/RizToolbox.PlaybackHandoff.Tests/RizToolbox.PlaybackHandoff.Tests.csproj -c Release -o ../managed-build-r7 --nologo
& ./scripts/Test-PlaybackHandoff.ps1 -ManagedAssembly ../managed-build-r7/RizToolbox.Gameplay.Tests.dll -FixturePath ../fixture-build-r1/RizToolbox.PlaybackStockFixture.dll -GetTimeHelperPath ../native-build-r2/RizToolbox.SingleReturn.Prototype.dll -SourceHelperPath ../native-build-r2/RizToolbox.PlaybackSourcePrototype.dll -DobbyPath 'C:/Users/71957/Desktop/riz_toolbox/.tools/bepinex-789/BepInEx/core/dobby.dll' -OutputDirectory ../checks-r5
```

本轮实际执行目录为 `managed-build-r6` / `native-build-r2` / `fixture-build-r1` / `checks-r4`。新 [Build-PlaybackHandoff.ps1](../source/scripts/Build-PlaybackHandoff.ps1) 在 `native-build-r3` 已 actual build 0，冻结并以 `/O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf` 编译所有 CPP；该新构建 fixture 的整文件 hash 不自动取得固定 caller profile 准入，实际 positive 使用已精确 pin 的 fixture-build-r1 SHA `ccc23b6f...`。具体 compiler commands 和每个 TU 输入见各 `build.cmd` / native-symbol proof。

旧 managed-build-r1（没有保存 build.exit，不能冒认成功）与 r2–r5、checks-r1/r2/r3 和 snapshot-r1 全保留。最终通过只归属于上述固定 inputs，不将旧 S4 包当新桥或本轮 acceptance。
