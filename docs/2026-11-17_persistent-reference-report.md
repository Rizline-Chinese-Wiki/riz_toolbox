# persistent-reference-r1 验证与实机报告

**状态：预览；`flavor=null`；截至 2026-11-17 17:28（UTC+08:00）。** 新源码已冻结、736 项检查与 4 个宿主场景通过并安装。新游戏外观发现成功，但音源接管和 Pause 仍分钟级阻塞，未 ClockReady/Speed/Seek；已结束本次游戏。范围见 [scope.md](../work/game-seek-20261117/persistent-reference-r1/scope.md)，时间线见 [timeline.md](../work/game-seek-20261117/persistent-reference-r1/timeline.md)。

## 证据

下表的 `source_ref` 为本工作目录相对路径；复核 JSON 用 `Get-Content -Raw -LiteralPath <source_ref> | ConvertFrom-Json`，复核字节用 `Get-FileHash -Algorithm SHA256 -LiteralPath <source_ref>`；ZIP 成员数用 Python `zipfile.ZipFile(<source_ref>).infolist()` 计数。表中 SHA-256 为证据文件本身的哈希，构建产物和包哈希另见对应回执。

| ID | source_ref / SHA-256 | 直接事实 |
| --- | --- | --- |
| E01 | [source-freeze.json](../work/game-seek-20261117/persistent-reference-r1/source-freeze.json) · `1f44197b6e86781e1f9da1f38a64cfed6bc3a7ed83a8e8b658ab7450e5d12d5f` | 16:52:38 冻结 1512 文件；相对上一冻结有 8 个文件变化；原生代码及公开 SDK 未变。 |
| E02 | [managed-build-result.json](../work/game-seek-20261117/persistent-reference-r1/managed-build-result.json) · `8f7fef8d36abaf47fb9c2f8c73f673cf7ea02ed325fc48ae8c214a2e139c9a08` | Loader、Hook、外观、相机的构建与检查步骤均退出 0；Runtime.dll 为 1,727,488 字节，SHA-256 `2b9ba399cc6e39a404034f2e2b1e2e24f4fa844708ed0178af17a53633b120c3`；未重建原生部分。 |
| E03 | [checks-runner/checks-r1/result.json](../work/game-seek-20261117/persistent-reference-r1/checks-runner/checks-r1/result.json) · `a511e651071f781afb4eb63ff36c188079bd51daa89251b1af4c8b2b4a26288d` | owned playback 子进程诊断 338 项通过，4 个进程；无伪造 owner、receipt 或 traversal；未执行游戏与生产准入。 |
| E04 | [ordinary-checks-r1.json](../work/game-seek-20261117/persistent-reference-r1/ordinary-checks-r1.json) · `8252d2590581012d58116cdff8e7080283b6e82d05b84dd01ec65e95061d039c` | 实际 Dobby ordinary fixture 48 项通过，运行时与候选匹配；未构造 playback group。 |
| E05 | [section-checks.json](../work/game-seek-20261117/persistent-reference-r1/section-checks.json) · `edf01a4a215460010a4b83d908fefe69051aa2d03e664352dd4462a8ab60ae32` | 同一自有 PE `.text` 的两个不重叠片段、真实 ordinary owner 的 6 模式共 28 项通过；`typedGroupCoverage=false`、`dualPeSectionCoverage=false`。 |
| E06 | [extra-checks-controller.json](../work/game-seek-20261117/persistent-reference-r1/extra-checks-controller.json) · `0bb6265cda441fde3a3d49c31ca0fff0e450dbab389807befdb917b26c696007`；[host-checks-r1.json](../work/game-seek-20261117/persistent-reference-r1/host-checks-r1.json) · `71a94a202ccdf5495f8889909e1850fe5babcef912c334fc2d2fd955bad3b8ad` | 受影响链 338+48+28=414 项通过；受控 Loader 宿主 4 场景通过。 |
| E07 | [Hook 结果](../work/game-seek-20261117/persistent-reference-r1/hooks-checks.log) · `3bafea6057548412636638d74f96a642073bba80364087e016522fff1e195714`；[外观结果](../work/game-seek-20261117/persistent-reference-r1/appearance-checks.log) · `bbfe87f4b4d390fc6303bfa0451cf1cb028f18a9430d4869e4eee5bdca5d1470`；[相机结果](../work/game-seek-20261117/persistent-reference-r1/camera-checks.log) · `238d93a8701f39e1ab5b13e0886b8d867b3a9b35f09c5fdbc1bdb58728774dfd`；[预览清单](../work/game-seek-20261117/persistent-reference-r1/package/preview-manifest.json) · `4f01037710457252c6ddca073781649702f2836e7b57d5cae7aa40a054e4f107`；[包清单](../work/game-seek-20261117/persistent-reference-r1/package/artifacts/package-manifest.json) · `43084c9a5383c3b8925488e127ae7490c62eab5b5dac1500c0290affbf2c3fc2` | 本批次 Hook 210、外观 52、相机 60、playback 338、ordinary 48、section 28，合计 **736**；另有 Host 4。预览清单标记实机、非空 ordinary typed group、双 PE 执行节及完整 Seek 均未验证。 |
| E08 | [预览 ZIP](../artifacts/delivery-20261117/RizToolbox-0.3.0-persistent-reference-preview-20261117.zip) · `7cd9511bd01a315d916e11c98d4db3cd40330daf1eac74c83354564724ff2a80`；[install-command.json](../work/game-seek-20261117/persistent-reference-r1/install-command.json) · `dffc92e7cd849ad9c339f59391ec3649035534bbbae3d5f5c8b7c0f9ab5c192d`；[install-observation.json](../work/game-seek-20261117/persistent-reference-r1/install-observation.json) · `1698950a5570168c012238a9536d55cef38fc79d1162860ac40edd8a64e3ebca` | 预览 ZIP 37,369,139 字节、40 成员；安装成功；17:07:18 安装的 25 个运行时文件均匹配。 |
| E09 | [旧包实机结果](../work/game-seek-20261117/occurrence-call-profile/game-r1/game-result.json) · `aeb9f8610e47d416d2146949dd33c9bb98f24e5a00ce879c0792c87cae1912f1`；[旧包 source 栈](../work/game-seek-20261115/occurrence-profile-source-r1-stack.log) · `3ed7d95b8dd39d672448acd511b30f6613109e56c84429f2c991478323f2dda7` | 上一 occurrence-profile 包 source capture 用时 78.402008 秒；Pause 回执至 PreparingClock 用时 388.843861 秒，仍未 ClockReady。source 栈含 `ReferenceImageMembers.CaptureSnapshot`。 |
| E10 | [game-r1/initial-observation.json](../work/game-seek-20261117/persistent-reference-r1/game-r1/initial-observation.json) · `7d0c437ad72513610952271baf06517fcaff6234ae76590247a0152e9e85570a` | 新包游戏进程 PID 12344 于 17:08:48.8851647 启动；17:09:30 的观察标记 `GameVerified=false`。 |

E08 的 ZIP 大小、成员数和哈希由本地归档直接复核；安装事实由安装观察和命令回执约束。上述 736 为**本候选**新执行的托管检查；清单中的继承原生/Examples 检查不计入 736，上一候选的 788 项也不能替代本源码验证。

E11：[本新包实机结果](../work/game-seek-20261117/persistent-reference-r1/game-r1/game-result.json) · `b72b3c49eaaba5d6b2ece3e009f836b6be446806aacd5de8f01b91c5f40f3c2e`；[本会话日志](../work/game-seek-20261117/persistent-reference-r1/game-r1/current-session.log) · `08fb86356b33912ac35e653a13d268e4984dd05284cb48018ecf8a395f06755f`；[最终状态](../work/game-seek-20261117/persistent-reference-r1/game-r1/final-control-status.json)；[精确退出记录](../work/game-seek-20261117/persistent-reference-r1/game-r1/game-exit.json)。仅发一次 Pause，实际 SDK Success 与即时后置成立，并且本会话日志有返回后的独立 lifecycle Paused 样本（见 E12）；最终 SDK mailbox status 仍旧，没有 Resume/Speed/Seek；正常 Alt+F4 后 61.341924 秒未退出，经 PID/路径/启动时间三核结束，确认消失。三个实际 source/Pause/Clock 栈列于结果 JSON。

## 发现

| ID | 严重度 / 置信度 / 状态 | 发现与证据 |
| --- | --- | --- |
| F01 | `n/a_re` / 高 / 已验证旧包表现，候选效果待验证 | 旧包 source 和 Pause 后续阶段耗时长且未 Ready（E09）。旧栈定位到 ordinary 快照校验；一次栈样本本身不证明全部耗时由该函数造成。 |
| F02 | `n/a_re` / 高 / 离线验证通过 | 候选把 ordinary 成员快照限定在一次加锁的 `VerifyCurrentBody` traversal：入口完整捕获，每个执行节独立 canonical/原像/重叠与 patch 检查，完整成员出口及 traversal 的 targets、Dobby、foundation 出口仍执行（E01–E07）。每次独立调用重新取得证据；不保留 group 级成功票。 |
| F03 | `n/a_re` / 高 / 覆盖边界明确 | 28 项只覆盖单 PE `.text` 双片的 section 状态；已有 23 个 group fixture 的 ordinary 成员为 0。非空 ordinary 的 typed group、真实双 PE 执行节、瞬态恢复 ABA 与新游戏行为均未由离线检查证明（E03–E07、E10）。 |
| F04 | `n/a_re` / 高 / 实机仍阻塞 | 新 ZIP 安装与 25 文件匹配成立（E08/E11），实际音源接管 70.221616 秒，SDK Pause 从 sender 启动至回执 76.514717 秒，回执后 337.651954 秒才进入 PreparingClock；未 Ready（E11）。 |
| F05 | `n/a_re` / 高 / 部分功能已证 | 独立校验 active scene 与对象自身已加载场景，真实轨道颜色绑定出现 45 次，证明外观发现恢复；未验证视觉覆盖。相机已经到 graph 检查但仍 TargetExpired，具体首败谓词待新诊断；不签相机可用（E01/E07/E11）。 |

## 路径与结论

**P01（callflow，E01–E07 → F02/F03）：** `VerificationTraversal.Create` 在真实 group 锁内取得 ordinary 完整入口快照；`VerifyCurrentBody` 对每节建立独立 `CompositionSection`，核对成员身份、canonical 原像与重叠，再应用实际 patch；`VerifyMembers` 对同一快照做完整出口验证；`Dispose` 在异常路径补做应有 ordinary 出口，并复核 targets、Dobby 与 foundation。实现位置为 [Group.cs](../src/RizToolbox.Runtime/Hooks/ExactHookBroker.Playback.Group.cs) 与 [ReferenceCodeIdentity.cs](../src/RizToolbox.Runtime/Hooks/ExactHookBroker.ReferenceCodeIdentity.cs)。这条路径只限单次同步 traversal，不能跨 getter、Read、原生消费或回调复用。

**P02（实机观察，E08–E11 → F01/F04）：** 25 个新包运行文件匹配 → 新 run 正常开局 → source 70.221616 秒 → 一次后台公开 SDK Pause 返回 success/immediate true → 337.651954 秒后 PreparingClock。当前三个实际栈均进入嵌套 Group 校验，Clock 栈为 `VerifyMembers → VerifyLocalMemberEvidence → NativePlaybackStockSourceHub.VerifyMemberEvidence → ExecutableSeal.VerifyCore → RangeSeal.Verify`。仍未 Ready，属于功能阻断；不把旧包或离线结果作为新实际成功。瞬态恢复 ABA 不在本候选保证范围内。

**P03（外观发现，E01/E07/E11 → F05）：** active scene 独立核对当前 run，controller/rig/Camera 自身只要求 valid/loaded；保留唯一 exact controller、activeAndEnabled、run/generation 与引用链。实际外观发现恢复。相机保留 graph 拒绝，下一通过首败谓词诊断修复实际引用关系，不通过放宽未知条件获得通过。


E12（19:13续证）：[校正后的实机结果](../work/game-seek-20261117/persistent-reference-r1/game-r1/game-result-reconciled.json) · `063fc49fa2dc420e8f2b9d5c336de6ffc333b0727d4ba28a81961b83abfa1d51`。在唯一Pause回执09:17:47.5240148Z后，native lifecycle采样09:17:47.7510841Z已观察同run Paused=true，相差0.22707秒。旧game-result.json与旧SDK status保留，原“无后续Paused样本”只据stale SDK mailbox而遗漏了日志；本条纠正这一结论。仍未ClockReady，没有Resume/Speed/Seek，不改变分钟级阻断及完整B01–B19未完成。
