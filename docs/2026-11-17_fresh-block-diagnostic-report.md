# Fresh-block 诊断离线报告（2026-11-17）

新 native/CLR 同次读取诊断已随当前产物完成 r2 离线验证：**292 = Replay 113 + Resume 133 + direct-time 46**，三个独立进程均退出 0。当前预览 ZIP 共 35 个成员、25 个运行文件，已安装且 25 项哈希匹配。该结果说明拒绝诊断、原生原件执行和清理符合限定用例；尚未证明真实游戏接管或完整 Speed/Seek。

范围来自本次项目任务授权，`flavor=null`，覆盖本地 source ticket 诊断、实际构建、r2 owned fixture 和交付字节核对；本报告不扩展测试或操作 UI。无独立安全 case 的 `scope.md`，此处记录范围。r2 结果的 `stockFoundationExecuted`、`formalStockAdoption`、`formalStockFreshExecuted`、`formalAdoptionExecuted`、`clockExecuted`、`gameExecuted`、`productionAdmission` 全为 `false`；包的 `realGameVerified/fullSeekCoverageVerified` 及安装记录的 `GameVerified` 也为 `false`。

**诊断设计与标量边界。** 新 `RizSingleReturnPrototype_ReadFreshSourceWithDiagnostic` 和 `RtPlaybackSourceCoordinateFreshWithDiagnostic` 共享既有 core；旧导出仍保留。Read 继续按既有 action4 owner-thread 检查→action2 验证次序执行，在原拒绝分支及原锁内生成独立的 `RizFreshSourceFailureV1`，不追加一次 Source 查询。FailureV1 为 80 B、pack=8；已有 info 208 B、snapshot 176 B 不变。新 origin 区分 single-return precheck、source coordinator、joint commit，并附各自 mask 与当前真实 TLS scope。

非零 status 时 info 没有有效身份快照保证，CLR 只解释独立有效的诊断，保留原异常、拒绝顺序及失败票终态。`sourceMaskValid=0` 不能解释成 Source 没有 blocker；当前 scope 也不指认历史置位者。诊断不输出 self/caller/owner/watch 根地址；仅 Single1、Double1、Boolean1、UInt64_1、IcallSingle1 允许 argBits，Reference1 和其他非标量 kind 固定为 0。它不清 flag、不重发票、不恢复失败 CLR ticket，也不授予 Clock 或生产权限。对应四份 native 冻结源见 [native-source-manifest-r2.json](../work/game-seek-20261117/fresh-block-diagnostic/native-source-manifest-r2.json)，两份已应用 CLR 源见 [managed-applied.json](../work/game-seek-20261117/fresh-block-diagnostic/managed-applied.json)。

**实际构建与 r2 执行。** E-02 的新 helpers 位于 `work/game-seek-20261117/fresh-block-diagnostic/native-r1/`；目录名 r1 表示这次产品构建，与 runner 的 r1/r2 修订分开。当前 managed 产品来自 `src/RizToolbox.Loader/bin/Release/net6.0/`。E-03 记录所需 11 步退出 0；[checks-controller-r2.json](../work/game-seek-20261117/fresh-block-diagnostic/checks-controller-r2.json)另记录新 Stage、runner build、cases 三步全部退出 0。实际 runner、绑定和结果分别在 `checks-runner/runner-r2/`、`checks-runner/inputs-r2/binding.json`、`checks-runner/checks-r2/result.json`，不是重用旧 PASS。

| r2 mode | 检查数 | 正向原生调用 / 实际物理退出 | 已接受 schedule receipt | Ticket.Read 成功 / 拒绝 |
| --- | ---: | ---: | ---: | ---: |
| replay | 113 | 6 / 6 | 1 | 16 / 0 |
| resume | 133 | 8 / 8 | 1 | 20 / 0 |
| direct-time | 46 | 1 / 1 | 0 | 1 / 1 |

每个 mode 实际准备/提交 23/23、补丁家族 3/3、独立 roots 4→0、真实 claim 1；均完成原票 release、watch retirement 和 safe cleanup。Replay/Resume 首次拒绝为 null，原鲜票贯穿真实调用树，精确 DSP schedule 位匹配。direct-time 先对**已发行且已 claim 的真实 receipt**做独立 worker raw read：返回 10，sourceMaskValid/scopeValid 为 0，owner 下一次同票 Read 成功；这发生在后续 CLR 拒绝之前，不用于恢复失败票。

随后 direct-time 直接调用 detoured `LowerSetTime(IcallSingle1)`，真实 sourceBefore 的首次原异常保留：

```text
nativeStatus=4; detail=native-status; origin=source; action=2;
singleMask=0x0; sourceMaskValid=1; sourceMask=0x4; scopeValid=1;
role=LowerSetTime; kind=8; sequence=1; parent=0; depth=1;
argBits=0x3f400001; childFlags=0x0
```

`0x4` 对应 AudioBlocked。该 case 的成功含义是**预期拒绝 + 原 lower store/return/physical exit + 安全清理完成**，`freshLifetimePreserved=false`，没有 schedule receipt；拒绝票没有被提升成可用票。这不能推断实际游戏中的同一 blocker。

历史 [baseline 结果](../work/game-seek-20261117/resume-current23-checks/checks-baseline-r1/result.json) E-08 是旧 helper/CLR 的 246=113+133，只证明旧基线；新 292 由 E-04 独立接受。新 runner r1 曾失败（Replay/Resume 通过，direct-time 35 检查后退出 1）：把 enum kind=8 交给历史 `StockFixtureInvoke`，而该 fixture 的分支 8 使用 `(void*, uint64_t, void*)`，并非 `IcallSingle1` 的 `(void*, float)`；实际观察到 argBits=0，不能用其验收新诊断。r2 仅将 runner 改为精确两参 Cdecl delegate 直调 target，使 scalar 位进入正确的 XMM1；未修改 native/CLR 产品或固定 wrapper/lower/Dobby。保留的 [r1 源](../work/game-seek-20261117/fresh-block-diagnostic/checks-runner/source-r1/OccurrenceCases.cs)与 [r2 源](../work/game-seek-20261117/fresh-block-diagnostic/checks-runner/OccurrenceCases.cs)可逐项比较；最终仅以 r2 结果验收。

**交付与安装字节。** 当前 ZIP 为 [RizToolbox-0.3.0-fresh-block-diagnostic-preview-20261117.zip](../artifacts/delivery-20261117/RizToolbox-0.3.0-fresh-block-diagnostic-preview-20261117.zip)，37,314,033 B，SHA-256 `7c8b96594520b20c2a63eccd2ac3d0eaf3d2b2eec8c341819b148257d244bb80`。E-05 记录 35/25 及 ZIP 回读完成；当前只读复核再次确认：35 个成员与 `package-r2` 解包件逐字节一致，manifest 项无哈希/大小差异，13 个替换目的路径与当前 managed/native 原始产物 SHA 一致，安装目录 25 个运行文件与同包 manifest 一致。当前 ZIP 的 checks 绑定指向 E-04 r2 JSON，未借 r1 或旧包验收。

| 关键当前产物 | 字节数 | SHA-256 |
| --- | ---: | --- |
| SingleReturn.Prototype.dll | 80384 | `208b9c04a4b68a1548037aca05c48ec5ae6929865ba042419688f7a0cfea5e69` |
| PlaybackSourcePrototype.dll | 209920 | `50a1c440731a7b11bc51fe61184f1a0cd9208bc66154ff456db632a877c25214` |
| RizToolbox.Runtime.dll | 1697280 | `d8329c359e8129ba5bde23bd0ad06c9ec26c9d1b8bec3fa62ee527e0354343ed` |
| RizToolbox.Loader.dll | 43520 | `9b6f93ac7e47123c9234c3ea4a9e21fa11e1331e8c0185104f52617a3dfa4900` |

E-06 记录安装成功，目录 `D:/SteamLibrary/steamapps/common/Rizline`，包目录 `package-r2`。安装和字节一致性不构成游戏功能通过。

**Evidence（文件观察，2026-11-17；source_type=file）。** 下列 source_ref 均为原件，content_hash 为写报告时实际 SHA-256；观察摘录只概括各原件内容。linked_workitem=n/a；除 E-08 明示历史基线外，验收引用均是当前 r2/本次产品事实。

| E-id | source_ref / artifact_path | content_hash | raw_excerpt / 观察 |
| --- | --- | --- | --- |
| E-01 | [native-abi.md](../work/game-seek-20261117/fresh-block-diagnostic/native-abi.md) | `be862160dc16200013e5f39fd74dc53692570d793f8586860647d98b6a260faf` | 同次读取诊断的 80 B ABI、来源/mask/scope 有效位及仅标量边界。 |
| E-02 | [native-build-result.json](../work/game-seek-20261117/fresh-block-diagnostic/native-build-result.json) | `139716bfbe51998b9b7dfe5fbd46381253306c268a83037eb9dd342982e394ed` | native、managed 两步自然退出 0；新 native 两 DLL 路径、字节数与 SHA。 |
| E-03 | [fresh-block-diagnostic-r1-result.json](../work/game-seek-20261115/fresh-block-diagnostic-r1-result.json) | `61193cc4ea01d008267f19083a64bc94bcc83cf113e593735d7406a53899c41d` | 11 个实际 managed/build/check 步骤均退出 0；不是另一次游戏采用。 |
| E-04 | [result.json](../work/game-seek-20261117/fresh-block-diagnostic/checks-runner/checks-r2/result.json) | `d53992f7e1b6e96006ee99c2523966dc0ba3e8c314c6b3edfaeb42ff7dcd5d23` | r2 新产物三进程全部退出 0，292 检查，三票真实 claim；全部范围开关保留 false。 |
| E-05 | [fresh-block-diagnostic-preview-receipt.json](../artifacts/delivery-20261117/fresh-block-diagnostic-preview-receipt.json) | `41eebbcc5e48fa7d4d5918111dc0f9d483845d1498ac8bc7dedecc20519e7368` | 当前 ZIP 35 members / 25 runtimeFiles，readbackVerified=true、realGameVerified=false。 |
| E-06 | [install-observation.json](../work/game-seek-20261117/fresh-block-diagnostic/install-observation.json) | `88a2570e9bb369a9cb0dbb1338b082d90b9f6df1eecb0e922799c2244edc3115` | 2026-11-17 09:46:51 +08:00 安装 25 文件，FilesMatched=true、GameVerified=false。 |
| E-07 | [launch-request.json](../work/game-seek-20261117/fresh-block-diagnostic/launch-request.json) | `1867ba89b5814d9570d0d3bbc8870d257dc817af2a7605949b1281f53956910f` | 09:46:51 +08:00 经既有官方 Steam 请求 AppId 2272590；GameVerified=false。 |
| E-08 | [result.json](../work/game-seek-20261117/resume-current23-checks/checks-baseline-r1/result.json) | `bdb3e028c33d6b990d408ae27cd7f5797701ee1afa728c426364892d8c3ae606` | 历史 baseline：113+133=246；仅用于区别来源，不接受新字节。 |

只读复核命令（仓库根目录，PowerShell 7；不执行 build/test/game）：

```powershell
$case='work/game-seek-20261117/fresh-block-diagnostic'
$r=Get-Content -Raw -LiteralPath "$case/checks-runner/checks-r2/result.json"|ConvertFrom-Json
$r|Select-Object kind,passed,processes,checks,gameExecuted,productionAdmission
$r.steps|ForEach-Object{[pscustomobject]@{mode=$_.name;exit=$_.exit;passed=$_.passed;checks=$_.actual.checks}}
Get-FileHash -Algorithm SHA256 -LiteralPath 'artifacts/delivery-20261117/RizToolbox-0.3.0-fresh-block-diagnostic-preview-20261117.zip'
Get-Content -Raw -LiteralPath "$case/install-observation.json"
```

其余 Evidence 的 repro_command 为对该行 source_ref 运行 `Get-Content -Raw -LiteralPath` 及 `Get-FileHash -Algorithm SHA256 -LiteralPath`；均为本地既存文件读取，不包含新的功能执行。

**Finding。**

| F-id | 结论与边界 | evidence_ids | severity / category / status / confidence | location |
| --- | --- | --- | --- | --- |
| F-01 | 同次拒绝可区分来源、mask 和当前标量 scope，保持原票拒绝与清理条件。 | E-01, E-04 | n/a_re / design / validated / high | FreshSource diagnostic core、CLR FreshSourceReceipt.Read |
| F-02 | 本次产品构建及 r2 owned 292 检查通过；旧 246 不承担新产物验收。 | E-02, E-03, E-04, E-08 | n/a_re / other / validated / high | native-r1、runner-r2、checks-r2 |
| F-03 | 当前 35/25 ZIP 与 25 文件安装具有同包哈希关联；只证明交付字节。 | E-04, E-05, E-06 | n/a_re / other / validated / high | delivery-20261117、package-r2、安装目录 |
| F-04 | 当前真实游戏拒绝来源及完整 Speed/Seek 未验证。 | E-05, E-06, E-07 | n/a_re / other / candidate / high | 新游戏进程 16288 的待采集会话 |

上述 F-01～03 的复核步骤是按 E 原件核对当前输入/哈希与实际返回；remediation=n/a（诊断实现记录）。F-04 的下一验证是由 root 保留新包安装后会话，再核其真实新 status 与来源；不能以旧 session 或离线原件替代。

**Path P-01（path_type=callflow）。** 起点为本次新 helpers/CLR，目标为同票拒绝原因的可审计离线证据：① 实际 native/managed build 退出 0（E-02/E-03→F-02）；② Stage 用真实绝对路径、大小和 SHA 绑定新字节，runner-r2 对真 23-member group/3-member pair/4 roots 发出一次 claim（E-04→F-01/F-02）；③ Replay/Resume 原票可读，direct-time 先记录独立 worker10，再在真实 lower-time scope 原样返回 status4/source mask0x4，原件及清理完成（E-01/E-04→F-01）；④ 将这些当前输入装入 35/25 ZIP、回读后安装并逐项核 25 文件（E-05/E-06→F-03）。residual_risks：owned fixture 不是 formal stock adoption/Unity/Clock/game 执行，末步没有授予游戏功能通过。

**真实游戏结果（root 追加）。** 本包新进程 **16288**、session `22d698c591684101b7d0fb8fe7a80159`、run `a26a2474bded4ea2aadf99470f2fc9b7` 已进入 Greatness Earth / Axya / EASY4 / FREE。接管141.997524秒返回WaitingForPause；SDK Pause收到精确事件，用时33.49061秒；SDK Resume dispatch32.819183秒，返回时立即恢复未确认。随后Draining→Withdrawn，实际诊断为 `nativeStatus=4; origin=source; sourceMask=0x2; role=EngineSetPitch; kind=8; sequence=11; parent=10; depth=3; argBits=0x3f800000; childFlags=0x0`。无PreparingClock/ReadyExperimental或完整Speed/Seek通过。已正常AltF4退出，窗口与进程消失，无强杀，OSexit未知。

新增 E-09 为[本包实际游戏结果](../work/game-seek-20261117/fresh-block-diagnostic/fresh-diagnostic-r1-game-result.json)，绑定本包SHA、25安装文件与完整会话原件；F-05：当前Engine音调发生时LowerPitchBlocked且没有识别成精确Engine child，**当前scope不证明历史置位者，也没有记录parent10的role**。下一Path P-02是记录同一次原生判定的真实父scope与首个失败条件，再修确定路径，不能把sampledPaused或浮点近似当授权。新V2诊断在独立`engine-child-diagnostic`施工，其后续字节不能借本报告292验收。

关键时间（+08:00）：09:27 旧 baseline246 完成；09:32 应用两 CLR 诊断源；09:33–09:34 本次 native/managed 构建完成；09:44:44–09:44:57 r2 的292完成；09:46:51 25文件安装核对并请求官方 Steam 启动。本文编写过程仅读取现有产物及核对哈希，未运行构建、checks 或 UI。
