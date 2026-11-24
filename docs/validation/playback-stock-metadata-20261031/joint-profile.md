# 固定 image 与私有共同组联合验证

2026-10-31，`flavor=null`。从 root 的成功 core-r1 冻结源独立构建 Gameplay.Tests，固定原版 GameAssembly 的四个 D 盘副本分别完成实际 Dobby 双 Prepare/Commit、两个 profile 的联合验证与 ClockPublication `1→2→3`。实际 **4 个进程 / 84 个检查全部通过**，build 与四个 child 均 exit 0，0 warning / 0 error。pitch 尾部、未 hook 正文或 consumer 改动一旦被观察，恢复字节后仍永久拒绝两个 profile、共同组和成员，真实 native clock authority 也已撤回。

本包遵守[授权范围](scope.md)，[时间线](timeline.md)保留失败与修复过程。IL2CPP exports/MethodInfo 缓冲区和 playback binding graph 明确为 synthetic；它们进入真实 resolver、私有 broker 和 native policy API。GameAssembly 仅以 `DONT_RESOLVE_DLL_REFERENCES` 映射，未调用其 entry、original 或 getter；游戏方法、游戏 DllMain、IL2CPP object allocation 和 source Before/After 均为 0。`productionAdmission=false`，默认真实 provider 仍关闭。

## 实际结果

| 独立 child | 检查 | 实际 exit | 验证终点 |
|---|---:|---:|---|
| normal | 18 | 0 | 两 profile 接受共同组；条件 policy withdrawal 保留物理组，组 Dispose 后双方拒绝 |
| pitch-tail | 22 | 0 | SetPitch `+40` 改动，恢复后共同组、GetTime、SetPitch 和 native authority 均 inactive |
| body | 22 | 0 | ReplayScheduled `+32` 改动，恢复后同上 |
| consumer | 22 | 0 | Progress.Update `+20` 改动，恢复后同上 |

每个 child 均实际执行 **Prepare=2、Commit=2**；GetTime 与 SetPitch 各保留六字节真实 patch、独立 Dobby original、额外原跳板和注册的 unwind receipt。SetPitch instance/static 两个独立 MethodInfo 仍指向同一物理入口；没有复制一个 setter 安装。旧 `GameplayClockCodeProfile` 和新 `NativePlaybackStockCodeProfile` 各自以 attach 与 startup 两条路径接受同一私有安装组，完整固定正文和 metadata 持续再验。

每个 child 的 raw JSON 都记录实际 native 初始 revision 1、Commit 后 revision 2 / rate 2、Restore 后 revision 3 / rate 1；终止后 `nativeAuthorityActiveAfterTermination`、`groupActiveAfterTermination`、`getTimeActiveAfterTermination`、`pitchActiveAfterTermination` 均为 false。GetTime original calls 为 0；source entered/exited/inFlight 在终止前的已知样本为 0，整个执行的 source Before/After 和禁止执行 callback 次数为 0。验证与 publication 没有额外物理安装。

## 失败根因与修复

历史 joint-r1 的 normal 18 项通过，pitch-tail 在第 19 项失败；原日志保留在 `D:/riz-build-cache-next/playback-stock-joint-profile-r1-20261031/pitch-tail.log`。失败断言要求组、两成员与 native policy 都撤回，门槛未降低。

源代码原因是 `PlaybackOwner.RevokeFromGroup` 通过 `MarkBrokerFault` 调用 SingleReturn bridge 的 fault 路径。R4 helper 的真实 `RizSingleReturnPrototype_MarkFault` 只置 sticky `unknown=1`，没有清 `authorityActive`；只撤掉 managed owner 不能代替真实 native policy 撤回。group owner 在 bridge 的同一个 `_gate` 内新增 once-only fault cleanup，保留私有 `_published` 对象，以该对象的确切 revision 调 native conditional withdrawal。它不采用 caller-created authority，也不撤回不匹配的 foreign revision；本地 fault 保持 sticky。

本轮只给原断言增加实际状态诊断和输出读回字段。joint-r2 在实际固定 image patch 上证明修复后策略已 inactive，字节恢复没有重新授权，两个既有 clock/stock profile 都拒绝继续使用该组。

## PE、PDB 与冻结源码

runner 从 `foundation-stock-source-core-r1-20261031/test-result.json` 验证并复制 **720 个冻结输入**到自有 `source/`，在该目录独立 `.NET build`，没有写 core 或 full 验证的 build/obj。`source-before.json` 与 `source-after.json` SHA256 均为 `9cde0c85fb3591a62f70ba027890afebafe6db8c177445df5201428405c82ad8`；构建前 source archive SHA256 为 `47a2fde003c2f1abcf226d53918e0afa5200123feae0a22a3f703514083940d8`。输入、原版游戏文件、native 包、副本和执行产物均保留 hash。

`managed-symbol-source-proof.json` 实读五个 PE 的 CodeView GUID/age/stamp，并与邻接 portable PDB 的 20 字节 ID 匹配；随后验证 PDB 每个 document 的 SHA256 与实际构建文件。共 **467 个 document**，其中 452 个是确切冻结输入，15 个是该次 build 的 `obj/` 生成文件，全部匹配。

| PE / portable PDB | MVID | 已验证 document / 冻结输入 |
|---|---|---:|
| RizToolbox.Sdk | `4115b8c8-27e4-44a7-a3f2-2dbcdb36a3ab` | 31 / 28 |
| RizToolbox.Core | `7add7b31-e5e7-4bab-97b0-5f94737daf75` | 27 / 24 |
| RizToolbox.Runtime | `56bacca5-2145-468f-beaa-f4878bef7a62` | 146 / 143 |
| RizToolbox.Adapter | `52f869ef-0a92-4784-8aa9-4311e2a45874` | 243 / 240 |
| RizToolbox.Gameplay.Tests | `b71e2e2e-ecfe-4142-9e31-4517a17a3b0c` | 20 / 17 |

native 输入保存 73 个文件，包含 GetTime R4 的完整冻结包、source-native R6 的完整 PE/PDB/COFF/source 证据与固定 Dobby。source helper SHA256 为 `a96160f46ca952ed0b6097478dc44efff840127d52d65d3ea43e5ba01c314083`；GetTime helper 为 `90b6133cf6ecd98dd00cd34472d81cd91493419ea4cb06b0aa59ffb0f7f0b355`，Dobby 为 `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded`。本轮只使用 source helper，不执行 owned stock fixture 或重跑 native 旧专项；R6 fixture 的 `.text` DIR64 正向由 native 包独立证明。

## Evidence → Finding → Path

以下路径前缀 `joint/` 指 `D:/riz-build-cache-next/playback-stock-joint-profile-r2-20261031/`。全部 raw evidence 的完整路径和 hash 见[证据索引](evidence-index.json)。所有复现命令受本页离线及零游戏执行范围约束。

| Evidence | source_ref / content_hash | 观察与复现 |
|---|---|---|
| E-JOINT | `joint/test-result.json`，`4c16c8500df365cfc3e495e97451005097c5961b5906a6984dfbb32bc10087b4`；`build.log`，`a164448261d332d01384c8775c30b1833fc44c4641f35b1a213222a722f27664` | 实际 build exit0、四 child exit0、84 项；按下方完整 runner 命令重现 |
| E-JOINT-LOGS | normal `c3aca2dacf059648fc7723799aa9176d41a09e323d9873b9782377bb833f1018`；pitch-tail `4de06ce69fdffef412ffd286cc526b5df492c7c87bf70f698003ac6bc9656ebb`；body `266d9ada908cbdbc5ea0f482418183471beb093c59e0574498878b7d980afce2`；consumer `527c50e8249cae317f0918a9389ef604e514083b5b269760cc4548ea210c5ec5` | `joint/{mode}.log` 与独立 `.exit.txt`；包含双 Prepare/Commit、1→2→3 和 native 策略 inactive 的实际读回 |
| E-JOINT-SOURCE | `joint/source-before.json` / `source-after.json`，`9cde0c85fb3591a62f70ba027890afebafe6db8c177445df5201428405c82ad8`；core proof `8e3778772f18adcae23c2df42714f603e0c72d4e093920fbf298772083b59017` | 720 冻结输入前后同 hash；runner 逐文件 `Get-FileHash` 验证 |
| E-JOINT-SYMBOLS | `joint/managed-symbol-source-proof.json`，`8aa0ae9069c60a43803e547b404c6703610d42c8b980fed3d9c8df1aa6b0e7be` | 实际 PE CodeView ↔ portable PDB ID ↔ 467 个 document 内容；runner 的 `ManagedSymbolProof` 重现 |
| E-JOINT-NATIVE | `joint/native-inputs.json`，`1af32e553196723cb52168480fa7d51e7de1446f1904e50526f5c62e957588be`；R6 build proof `6534334fbe9432ffb8086c0dd3156259b00f29894bb01caf48779e3171e3eeff` | 完整包复制并逐文件核 hash；R6 静态机器/符号证据为独立 native 包的范围，本页不合并其检查数 |
| E-JOINT-R1-FAIL | 旧 `pitch-tail.log`，`bd3352edc463ce30ffb11c74714d4d409d3799699c24f1609ce85484e192bf31`；旧 manifest `207d5c65a9e7786751cab85fc0eefa3617f6a51e057b4149fd78b44931a5de11` | normal18 通过、pitch-tail19 失败的真实历史；不覆盖、不作为新源验收 |
| E-JOINT-FAULT-SOURCE | 冻结 `src/RizToolbox.Runtime/Hooks/NativePlaybackSingleReturnBridge.cs`，`dfbe0c0c10854b5923d9c0e83ff1da744d55fe08a1841e6032ee6b9d020556e9`；R4 `SingleReturnContext.cpp`，`20bc81a06223035bd98877841036dba64a27ecfaf535aea1dbb496108e17fd27` | fault cleanup 的私有 revision 条件撤回；对照 frozen R4 `MarkFault` 只置 unknown 的原实现 |

| Finding | severity / status / confidence | evidence_ids / location / 影响 |
|---|---|---|
| F-JOINT-PROFILES | n/a_re / validated / high | E-JOINT,E-JOINT-SOURCE,E-JOINT-SYMBOLS；`RunGroupMapped`；两 code profile 的 attach/startup 路径接受真实双 hook 私有组，十 metadata 声明与 14 个固定正文未减项 |
| F-JOINT-PUBLICATION | n/a_re / validated / high | E-JOINT-LOGS,E-JOINT-SOURCE；`NativePlaybackClockPublication`；actual native revision1→2→3 与 rate2→1 读回成功，GetTime original0 |
| F-JOINT-REVOCATION | n/a_re / validated / high | E-JOINT-R1-FAIL,E-JOINT-FAULT-SOURCE,E-JOINT-LOGS；bridge `ReportFault` / group；fixed-body/consumer 改动后的 sticky 撤回同时覆盖 managed 组和确切 native policy，恢复字节不复活 |
| F-JOINT-SCOPE | n/a_re / validated / high | E-JOINT-SOURCE,E-JOINT-LOGS；synthetic metadata/graph 与 mapped original；本页证明离线实现证据，不授予生产首次安装或 engine-writer 完整覆盖 |

P-JOINT，`path_type=callflow`：固定 file/import 身份及完整原正文（README 的 E-IMPORTS/E-BODIES）→ fresh IL2CPP 声明解析与两个 pitch MI（E-JOINT-SOURCE/F-JOINT-PROFILES）→实际 Dobby GetTime/SetPitch 私有共同组（E-JOINT-LOGS/F-JOINT-PROFILES）→两 profile 的连续完整验证→ClockPublication 1→2→3（F-JOINT-PUBLICATION）→实际改动已知 pitch 尾部、source body 或 consumer→组 sticky fault 与私有 native revision 条件撤回→恢复字节继续拒绝（F-JOINT-REVOCATION）。残余范围为真实游戏 metadata/运行对象、底层全部 writer、生产首次写入准入、完整 stock mutation/readback/provider 和跨局 handoff。

## 独立复现

使用新的不存在目录；runner 会固定 native 和游戏文件输入、复制成功 core snapshot、独立构建并运行四个 child。它不会重新运行历史 metadata44、旧 return19/159、camera/N1、retirement 或 instant7 专项。

```powershell
$jointOutput = 'D:/riz-build-cache-next/playback-stock-joint-profile-repro-' + [guid]::NewGuid().ToString('N')
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile `
    -File 'C:/Users/71957/Desktop/riz_toolbox/scripts/Test-PlaybackStockJointProfile.ps1' `
    -SourceSnapshotManifest 'D:/riz-build-cache-next/foundation-stock-source-core-r1-20261031/test-result.json' `
    -NativeBuildManifest 'D:/riz-build-cache-next/playback-stock-native-build-r6-20261031/build-result.json' `
    -OutputDirectory $jointOutput
```

runner SHA256 `339eb298a3b6db779fcaefa16c46ea78e2d408fabe596e6fabcccfdda0ced0a2`。四个自有 C# 输入在本轮的冻结 hash：

| 文件 | SHA256 |
|---|---|
| NativeRuntime.PlaybackHooks.cs | `1f2d66b18b790092edd4dc1332342bfcabac6a889ae468340c5ee90850aa9f45` |
| NativePlaybackStockCodeProfile.cs | `aefbf1037d903ea39bc8433ac960de3ded8ff65c54f0243b7b7935798af2fa12` |
| NativePlaybackStockMetadataChecks.cs | `22ffc3f896afbc5c110dc61efcb1e1da631db0c07b94d1fec9e891699d5f01a6` |
| NativePlaybackStockGroupProfileChecks.cs | `55c4f37f26ef96cce1298631154485787cd6abdd52b51ad9120938b1d18e1008` |

旧 metadata44 的冻结源中第三个文件 SHA 为 `33c6aa29bfbd07fefa7d1554a46257af5bc5e576d6ff25e53aaa693f1c8ce1d8`；后来只为共享 fixture 增加 partial 声明。历史 44 项仍只验证其原冻结源，本轮没有把该结果当作所有当前源码的重新验收。
