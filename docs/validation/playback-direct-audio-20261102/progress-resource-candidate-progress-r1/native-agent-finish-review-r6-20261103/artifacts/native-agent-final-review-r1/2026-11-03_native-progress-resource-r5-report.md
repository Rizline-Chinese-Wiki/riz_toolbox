# 原生Progress资源连续性r5范围报告

报告日期：2026-11-03（Asia/Shanghai）；构建及矩阵原始时间见UTC时间线。flavor=null；通用技术报告与E/F/P最小集。本次为功能实现及owned native回归，不是游戏运行或生产验收。

正常Withdraw后资源票无法清理的功能缺陷已在r5修复。原r3 DLL上六种正常Withdraw清理实际退出1，原件均保留；r5 fresh构建实际退出0，46个模式实际全部退出0，共7969次检查。phase6重试覆盖新实际Start、资源解析、SetClip child、action12/13、same/ABA，以及唯一旧pending Progress身份保留到精确替换。Unknown和未退出物理调用继续拒绝根清理。最终复核时当前10个输入与r5冻结输入及manifest一致；本报告只接受这一组源码和产物。

## 范围与限制

[scope.md](scope.md)列出auth、in_scope、network_profile及边界；合同原件副本见[E-001](#e-001-隔离合同与任务范围)。未安装或启动游戏、调用游戏方法/DllMain、操作账号/UI，未终止、重启或替换任何已有长测，未改主目录产品。

**r3/r4旧结果不验收r5。** r5本次仅关闭Source native helper范围。Commit为owned stub，静态对象仅保持进程期根；SingleReturn opaque receipt/单调epoch、managed真实GCHandle free与唯一Clock组合仍需独立fresh验证。生产provider、未知lower、完整generation、Seek/effect及B01–B19均不在本报告验收，`productionAdmission=false`、`fullGenerationAcceptance=false`、`B01B19Acceptance=false`。

## 根因与改动

`RtPlaybackSourceSetArmed(false)`清Armed并置Withdrawn，不必然表示Unknown。旧`RetireProgressResource`仅接受`Flags==RtSourceArmed`，使正常Known/0-inflight的Dispose/Withdraw路径也无法退票。r5在同一Owner锁内将这一条件改为无Unknown、无Fault，仍要求owner线程、所属ticket、owner物理责任0和ticket initializer物理责任0。退票只到phase5，不恢复任何source权限；已消费的initializer保持完成状态。唯一产品差分见[product-change.patch](product-change.patch)。

7个helper产品输入对r3仅`PlaybackProgressWatch.inc`变化。fixture新增正向和反例；独立runner收集实际exit、原始stdout/stderr及hash，不以未运行或旧包结果代替。

## 实际结果

| 分组 | 模式数 | actualExitCode | checks |
|---|---:|---|---:|
| 原有回归 | 10 | 全部0 | 1478 |
| phase6精确重试 | 12 | 全部0 | 3400 |
| pending Unknown | 6 | 全部0 | 856 |
| 正常Withdraw | 12 | 全部0 | 1355 |
| Withdraw仍Unknown | 6 | 全部0 | 880 |
| 合计 | 46 | 全部0 | 7969 |

12个phase6正向用例各有2次retry回调尝试：第一次stub拒绝提交返回6且不转移，第二次接管成功。每例至少16次旧initializer拒绝、2次物理inflight时拒绝重试。6个pending replacement用例先退出旧host/active watch，再以旧pending作为唯一Progress身份取得新票；新watch接管后旧retire不能清新状态。正常Withdraw用例覆盖future/yield/candidate/phase6/consumed/inflight，共24次成功退票；12个Unknown用例共72次清理拒绝。inflight Withdraw只在真实退出后成功。

旧r3失败矩阵共9个模式：正常Withdraw六模式退出1，Withdraw+Unknown三模式退出0。该失败证据由同一新增fixture实际装载原r3 DLL产生，与r5 fresh46独立保存。[原失败原件](artifacts/native-agent-withdraw-repro-r3-r4/native-retry-result.json)和[r5成功原件](artifacts/native-agent-retry-short-r5/native-retry-result.json)都有逐例exit/stdout/stderr。

## 源码与产物身份

| 身份 | bytes | SHA-256 |
|---|---:|---|
| r5 helper DLL | 146944 | `4fb32af3b0e848ef734a24bf4f9e430a14fdda1cc714936ba2ff8dd873d78342` |
| r5 fixture EXE | 80384 | `ec326ec4e7dc1edc657d675b3a0a68f87834f58f5948f2d73b6be1fc55140821` |
| r5 PlaybackProgressWatch.inc | 27568 | `477a450872fa0f1bda064065240a7bc337f25483c1e029804e94db89130b091e` |
| r5 fixture C++ | 42315 | `38d963e61bac6a98d9932e3cc8adff3a56aaa4ea03134e9686bc646e3400da93` |
| 独立runner | 8788 | `73ea092ae245992f674778cde93efc257a743676e13d8b0444f3f3c3429dede1` |
| 原r3 helper DLL | 146944 | `11535af57a7bd061982eaed26a191c79fb537bf09c2d8f7c2102d8565b92bb1b` |

最终源码核对见[source-audit.json](source-audit.json)：当前10/10输入等于r5 frozen和manifest，DLL/EXE等于build manifest，runner等于矩阵冻结副本。复制关系和原件hash见[copy-manifest.json](copy-manifest.json)，包内固定性见[hash-manifest.json](hash-manifest.json)。审核副本没有覆盖r3/r4/r5任何原始证据。

## Evidence

### E-001 隔离合同与任务范围

- source_ref / artifact_path: [artifacts/contract/IMPLEMENTATION-CONTRACT.md](artifacts/contract/IMPLEMENTATION-CONTRACT.md)
- content_hash: `dbdebfcaf356a37d8045eaa9f2b66649c19183c2b03345b9b66ff9c4b63509f6`
- observed_at: `2026-11-02T16:01:45.632665+00:00`；source_type: `file`；linked_workitem: `n/a`；supersedes: `none`
- raw_excerpt: 不改主目录、不运行游戏、不终止原长测；phase6精确重试与Unknown责任保留。
- repro_command（在本报告目录执行；命令用于第三方复现，封包时没有重跑）：

```powershell
Get-Content -LiteralPath './artifacts/contract/IMPLEMENTATION-CONTRACT.md'
```

### E-002 r3原始helper构建及冻结输入

- source_ref / artifact_path: [artifacts/native-agent-build-r3/native-build-result.json](artifacts/native-agent-build-r3/native-build-result.json)
- content_hash: `37715724441b79ece6a50fa5a8350dc80cb291374d01531015fe3af0532d90f7`
- observed_at: `2026-11-02T16:01:45.632665+00:00`；source_type: `file`；linked_workitem: `n/a`；supersedes: `none`
- raw_excerpt: r3 DLL SHA256=11535af57a7bd061982eaed26a191c79fb537bf09c2d8f7c2102d8565b92bb1b。
- repro_command（在本报告目录执行；命令用于第三方复现，封包时没有重跑）：

```powershell
Get-Content -LiteralPath './artifacts/native-agent-build-r3/native-build-result.json'
```

### E-003 同一新增fixture针对原r3 DLL的Withdraw失败

- source_ref / artifact_path: [artifacts/native-agent-withdraw-repro-r3-r4/native-retry-result.json](artifacts/native-agent-withdraw-repro-r3-r4/native-retry-result.json)
- content_hash: `0f435cfc824b62c07de5ac3062adafdb130846ad36bd87b938b79612a88caabe`
- observed_at: `2026-11-02T16:01:45.632665+00:00`；source_type: `file`；linked_workitem: `n/a`；supersedes: `none`
- raw_excerpt: future/yield/candidate/phase6/consumed/inflight六种正常Withdraw actualExitCode=1；Unknown三反例=0。预期重现退出1。
- repro_command（在本报告目录执行；命令用于第三方复现，封包时没有重跑）：

```powershell
& './artifacts/native-agent-build-withdraw-repro-r4/ProgressResourceNativeFixture.exe' './artifacts/native-agent-build-r3/RizToolbox.PlaybackSourcePrototype.dll' host-withdraw-candidate; $LASTEXITCODE
```

### E-004 唯一helper产品差分：known Withdraw资源票可退

- source_ref / artifact_path: [product-change.patch](product-change.patch)
- content_hash: `6f6493b5586326d1c297504cfa77afe5dbdb013f1833ebd1c9651ff34954fc9f`
- observed_at: `2026-11-02T16:01:45.632665+00:00`；source_type: `file`；linked_workitem: `n/a`；supersedes: `none`
- raw_excerpt: Flags==RtSourceArmed 改为 !(Flags&RtSourceUnknown)，仍要求无Fault、owner和initializer物理责任0、owner线程与所属票。
- repro_command（在本报告目录执行；命令用于第三方复现，封包时没有重跑）：

```powershell
Get-Content -LiteralPath './product-change.patch'
```

### E-005 r5 fresh冻结构建实际退出0

- source_ref / artifact_path: [artifacts/native-agent-build-r5/native-build-result.json](artifacts/native-agent-build-r5/native-build-result.json)
- content_hash: `d43bace1f1ecbefcd8a18186e420dbc0f3c4be45d7faa1bdfe51723725c95568`
- observed_at: `2026-11-02T16:01:45.632665+00:00`；source_type: `file`；linked_workitem: `n/a`；supersedes: `none`
- raw_excerpt: actualExitCode=0，/O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf；build.stderr.log为空。
- repro_command（在本报告目录执行；命令用于第三方复现，封包时没有重跑）：

```powershell
Get-Content -LiteralPath './artifacts/native-agent-build-r5/build.exit.txt'; Get-Content -LiteralPath './artifacts/native-agent-build-r5/native-build-result.json'
```

### E-006 r5 fresh46原生短模式全部实际退出0

- source_ref / artifact_path: [artifacts/native-agent-retry-short-r5/native-retry-result.json](artifacts/native-agent-retry-short-r5/native-retry-result.json)
- content_hash: `350a9998f2366a1d9435fe77320c00d54cdf9cba85bdf4526a5298a0bb5a8d12`
- observed_at: `2026-11-02T16:01:45.632665+00:00`；source_type: `file`；linked_workitem: `n/a`；supersedes: `none`
- raw_excerpt: 46/46 actualExitCode=0；7969 checks；12个phase6正向，各16次旧凭据拒绝、2次inflight重试拒绝；24次正常Withdraw retire；72次Unknown根清理拒绝。
- repro_command（在本报告目录执行；命令用于第三方复现，封包时没有重跑）：

```powershell
& './artifacts/native-agent-build-r5/ProgressResourceNativeFixture.exe' './artifacts/native-agent-build-r5/RizToolbox.PlaybackSourcePrototype.dll' host-phase6-pending-retry; $LASTEXITCODE
```

### E-007 最终当前源码/冻结r5/产物逐项身份复核

- source_ref / artifact_path: [source-audit.json](source-audit.json)
- content_hash: `40d2619319b2a658730509acb7a6500151e4db35a77457eef5b13b3e57a583b8`
- observed_at: `2026-11-02T16:01:45.632665+00:00`；source_type: `file`；linked_workitem: `n/a`；supersedes: `none`
- raw_excerpt: 10/10输入与r5冻结及manifest相等，7个helper输入对r3只改PlaybackProgressWatch.inc；DLL/fixture和runner均匹配各自冻结/manifest。
- repro_command（在本报告目录执行；命令用于第三方复现，封包时没有重跑）：

```powershell
Get-Content -LiteralPath './source-audit.json'
```

### E-008 r4旧28模式历史证据，禁止代验r5

- source_ref / artifact_path: [artifacts/native-agent-retry-short-r4/native-retry-result.json](artifacts/native-agent-retry-short-r4/native-retry-result.json)
- content_hash: `6382960f0e6c1342b6e14a4ef3e28576be7f630af09e61559af3bfcebf755c37`
- observed_at: `2026-11-02T16:01:45.632665+00:00`；source_type: `file`；linked_workitem: `n/a`；supersedes: `none`
- raw_excerpt: r4及原r3 helper均28退出0；无Withdraw覆盖。只用于记录补测迭代，不验r5。
- repro_command（在本报告目录执行；命令用于第三方复现，封包时没有重跑）：

```powershell
Get-Content -LiteralPath './artifacts/native-agent-retry-short-r4/native-retry-result.json'
```

## Findings

完整机器字段见[findings.json](findings.json)。

| F-id | severity | status / confidence | evidence_ids | location / 结论 |
|---|---|---|---|---|
| F-001 | n/a_re | validated / high | E-003, E-004, E-006 | PlaybackProgressWatch.inc:RtPlaybackSourceRetireProgressResource：正常Withdraw资源票无法retire的功能缺陷已在r5修复 |
| F-002 | n/a_re | validated / high | E-005, E-006, E-007 | PlaybackProgressWatch.inc:RetryResourceInitialization / PendingPlayerReplace / PendingProgressReplace：phase6资源替换通过新实际Start精确接管，旧initializer永不复活 |
| F-003 | n/a_re | validated / high | E-004, E-006 | RtPlaybackSourceRetireProgressResource / owned pending-unknown与withdraw-inflight fixture：Withdraw不将Unknown变成可清理状态，物理调用未退出时仍拒绝 |
| F-004 | n/a_re | validated / high | E-005, E-006, E-007, E-008 | source-audit.json / native-build-result.json / native-retry-result.json：r5结果只绑定r5冻结输入和exact产物，r3/r4历史结果不代验 |

## Paths

完整步骤字段与residual_risks见[paths.json](paths.json)。

### P-001 资源替换后的native phase6精确重试

- path_type: `callflow`
- start: 实际Start yield/final及SetClip child形成candidate
- goal: 新initializer和新watch接管且旧identity拒绝

1. 实际Start与reference解析、SetClip child完成，Begin进入phase6 — evidence: E-006 — finding: F-002
2. 纯+30 setter推进resource revision，旧票与旧initializer永久失效 — evidence: E-006 — finding: F-002
3. 保留唯一旧pending Progress身份，新实际Start取得fresh票并通过action12/13接管 — evidence: E-006 — finding: F-002
4. 精确替换player/progress pending watch，晚到旧retire不能清新watch — evidence: E-006 — finding: F-002

residual_risks: Commit为owned stub，不验SingleReturn真正opaque receipt与单调epoch。；静态owned对象不验真实GCHandle root free。

### P-002 Known Withdraw清理缺陷的失败到fresh修复证据链

- path_type: `callflow`
- start: 正常Withdraw清Armed
- goal: Known真实退出后能清理，Unknown仍保留

1. 同一withdraw fixture在原r3 helper上6种正常清理实际失败 — evidence: E-003 — finding: F-001
2. 仅替换retire的Armed依赖，保留Unknown/Fault/双重物理责任检查 — evidence: E-004 — finding: F-001
3. fresh r5构建退出0，46短模式实际全0 — evidence: E-005 — finding: F-001
4. 验证已Withdrawn但Unknown仍拒绝、物理未退时拒绝 — evidence: E-006 — finding: F-003
5. 最终逐项核对当前源码与r5 frozen/manifest/产物 — evidence: E-007 — finding: F-004

residual_risks: 只关闭native helper范围；生产provider、fullgeneration、B01–B19不在本报告验收。

## 时间线与复现

完整UTC时间线见[timeline.md](timeline.md)及[timeline.json](timeline.json)。构建命令原件`artifacts/native-agent-build-r5/build.cmd`使用VS2019 `vcvars64.bat`，C++17 `/O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf`，保持SourceV4/168B、SourceInfo136B、PlayerWatchV2、ProgressWatchV1及ResourceV1/112B。

第三方可在本报告目录直接执行E-003的单例命令观察原r3失败，或E-006单例命令观察r5通过；运行不会安装游戏或加载游戏映像。所有46个已运行模式和退出码以成功矩阵原件为准，封包时没有重复执行。Python runner及PS构建脚本的冻结副本均在artifacts内；若重新构建或调整源码，必须使用另一个fresh源码/构建/证据目录，原命令中的绝对路径需要按新目录重建。

文档技能的原`../tool-index.md`、`../ops/evidence-finding-path.md`相对位置缺失，实际读取同源`reverse-skill-router/tool-index.md`和`reverse-skill-router/ops/evidence-finding-path.md`。纯文本报告使用已验证的`C:/Python314/python.exe`与原有PowerShell；无需工具安装。未引入malware/APT、IOC或ATT&CK归因章节。

## 交付后动作

根代理后续managed/fixed-profile/owned组合须使用上表r5 DLL exact hash并独立fresh冻结。将真实opaque receipt/epoch、GCHandle free、唯一Clock及其所有失败原件放在各自组合证据中，本报告不替这些结果签署。helper产品已停止写入。
