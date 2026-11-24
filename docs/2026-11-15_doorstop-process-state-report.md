# Doorstop 进程状态维护报告

2026-11-15 · 普通本地维护 · flavor=null · 本报告截至既有 runtime-r3 与已完成 native r2 证据。

已接纳 native 进程所有权修复：实际 Root native 构建成功，原始头文件八项和实际 Root DLL 六项均有自然退出 0 的回执。修复把 Windows hook/CLR 内部状态改为 PID 加创建时间的私有标记，避免启动器继承旧状态而抑制子进程，并保留用户手动禁用与同进程重复加载守卫。旧 r1 验收因冻结了错误控制器，仅作限定历史证据；新 r2 冻结实际 `__file__` 控制器并保留初始/最终夹具 INI。既有第三次运行诊断确实到达 Unity tick 绑定，随后模块注册抛出 manifest/interval 异常，窗口无响应且 status 未更新。本包未验收 SDK 启动、游戏回合或完整目标；`gameSdkVerified=false`、`sdkGoalComplete=false`、`completeSeekPermitted=false`。

## 范围与证据包

范围见 ZIP 内 `case/scope.md`，时间线见 `case/timeline.md`。本次只生成证据和文档，不操作游戏、网络、UI、Git、checkpoint、NEXT 或 Root 源码。历史运行观察来自此前授权会话，本轮没有重复执行；诊断 v1/v2 overlay 没有接纳到 Root。本报告使用维护结构，不套恶意软件、IOC 或 ATT&CK 章节。后续管理代码修复不属于本证据快照。

可携带包位于 [doorstop-process-state-evidence.zip](validation/doorstop-process-state-20261115/doorstop-process-state-evidence.zip)，入口为[归档说明](validation/doorstop-process-state-20261115/README.md)。所有 ZIP 内成员均在 `hash-index.json`，外部 `archive-receipt.json` 固定 ZIP、索引、checker 与报告的 SHA-256。包只收录项目 native 代码、修复 diff、实际 Root DLL/构建与 LGPL 对应源码包、自己的夹具/控制器/日志、限定失败和已有运行观察；不含游戏二进制、整块缓存或用户配置。

## 验证矩阵与限制

| 层次 | 实际证据 | 结论 | 覆盖边界 |
|---|---|---|---|
| Root 源码应用 | E-001；三文件及此前 Build-Doorstop 原件 | 已应用且与 Root 构建快照闭合 | 只含 native 三文件；application-record 当时 rootNativeFreshBuildPassed=false 的历史字段原样保留 |
| Root native 构建 | E-002；session 70747 / bf1141 / exit 0 | 成功；18 输入未变 | 不是游戏安装或启动验收 |
| 头文件 | E-003；15280c / exit 0；8/8、22 断言 | 成功 | 新进程、别名、父子继承、手动禁用、创建时间、坏标记；CLR 只是 helper 标记行为 |
| 实际 Root DLL r2 | E-004；390b0e / exit 0；6/6 | 成功；实际控制器冻结 | hooks、真实 WinHTTP 转发、继承子进程、手动禁用与 alias detach；未实际触发 CoreCLR 完整初始化 |
| 实际 Root DLL r1 | E-005；2093e5 / exit 0；6/6 | 限定历史证据 | 错冻 generic runner；旧 acceptance 的 d86fda0… 原件保留，正式依据改为 r2 |
| C11 首次尝试 | E-006；exit 2 | 失败已保留 | 固定 SDK 与 /WX 预处理器警告；r2 使用 /Zc:preprocessor- 后编译通过 |
| 诊断首次构建 | E-007；37462 / 9b4289 / exit 1 | 失败已保留；未安装 | Windows CRLF here-string 与固定 LF needle 不匹配；不是功能通过 |
| runtime-r1 | E-008 | 已启用 Doorstop/native | 无当前 CoreCLR/SDK，会话与 status 旧 |
| runtime-r2 | E-009；pid 664 | prepare(38) 安全拒绝 0；原始返回 1 | 当前 SDK 未通过 |
| runtime-r3 | E-010；pid 40780 | ready(49)、CoreCLR/当前 Loader、tick bind | 后续 ModuleManager.Add 异常；status 旧；无响应测试已由 Root 结束；当前 SDK 未通过 |

不把此前已接纳的管理代码/SDK 测试数量折算成本轮游戏启动通过，也不把 ready、CoreCLR、tick 绑定中的任一步单独作为 SDK 可用证据。

## 修复与归档复核

`controls/source-process-state.patch` 记录三个 Root 文件的变动。`riz_process_environment.h` 计算 `PID:creationHigh:creationLow` 并精确比较，畸形、截断、超长或创建时间不同的标记均不能拥有守卫。构建补丁分别在配置、入口和 Mono/IL2CPP bootstrap 使用私有所有权；同进程 alias 的 hook 去重独立于 ignore-disable，退出 alias 不再清除用户手动开关。实际发布后的所有权写入失败采用源码中的 fail-fast 路径；本轮未注入该异常分支，不能宣称它被动态覆盖。

构建产物 `winhttp.dll` 的 SHA-256 为 `dad56eb1f010eebcf09d84c619f179db207d3ba565c3906c070f9554898e7021`。r2 实际控制器 SHA-256 为 `4e6e185e102888e3adefdd1ed87347000c12d2516eef628b2b5e7104f213a1f7`，初始 INI 为 `2dd69acfb74f517c30f7e4262eb351ceb02c83586c115d0cb700fb93265af17a`，最终 INI 为 `99af2022ce2dfef7e48a552b7ab19d5ae420ef72cbcfaa3f5f7984f6e0574cd8`；INI 的变化只发生在隔离夹具目录，作为测试输入/输出分别归档。

在归档目录执行以下命令即可独立核对 ZIP 成员、全部哈希和回执之间的链接；checker 只读 ZIP 字节及内部 LGPL 对应源码 ZIP，不解压、不执行 DLL，不依赖原 Root/D 缓存目录：

```powershell
python ./check_evidence_archive.py ./doorstop-process-state-evidence.zip
```

需要对照外部可信 receipt 时，将其中 `archiveSha256` 作为 checker 的 `--expected-sha256` 参数。此命令验证证据固定性与内部一致性；历史进程的自然退出事实仍取自保留的原始控制器回执与日志。完整重跑需要匹配的 Windows、MSVC 14.29.30133、SDK 10.0.19041.0、PowerShell 7.2+ 及原始构建输入；历史控制器有绝对输出路径和防覆盖守卫，不作为解压即重跑命令。运行观察仅支持离线复核，不能在缺少原游戏环境时复演。实际编译/构建 argv 和时间均保留在 result.json。

## Evidence

### E-001 — 三个 native 文件应用记录与静态修复

- title: 三个 native 文件应用记录与静态修复
- observed_at: 2026-11-15T03:38:15.840643+00:00
- source_type: file
- source_ref: `root-application/application-record.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `00afc1d808350feb23400a475896e678b8230d99dd72662952556715aa40890f`
- artifact_path: `root-application/application-record.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: nativeProcessStateApplied=true；appliedFiles 三项；诊断 overlay 未接纳。
- linked_workitem: n/a（普通维护）
- supersedes: none

### E-002 — 实际 Root native 构建

- title: 实际 Root native 构建
- observed_at: 2026-11-15T03:45:22.457341+00:00
- source_type: command
- source_ref: `root-build/result.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `a8de7da2591b3e34941141b1d8bbeebab6551cea33944cae318baf4aae16c27d`
- artifact_path: `root-build/result.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: session=70747、chunk=bf1141、实际退出 0；18 个输入不变；DLL 与 LGPL 源码产品具哈希。
- linked_workitem: n/a（普通维护）
- supersedes: none

### E-003 — 原始头文件真实父子进程八项

- title: 原始头文件真实父子进程八项
- observed_at: 2026-11-15T02:54:26.366209+00:00
- source_type: command
- source_ref: `header-fixture-r2/result.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `bf9d0ca846dce4d67263963768c70f5970873cde523937ae7b6fc996362ee0fd`
- artifact_path: `header-fixture-r2/result.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: chunk=15280c、实际退出 0；C11/C++17；8/8、22 assertions。CLR 仅为 helper marker。
- linked_workitem: n/a（普通维护）
- supersedes: E-006

### E-004 — 实际 Root DLL r2 六项及真实控制器冻结

- title: 实际 Root DLL r2 六项及真实控制器冻结
- observed_at: 2026-11-15T04:00:17.959992+00:00
- source_type: command
- source_ref: `root-native-fixture-r2/result.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `05f2275bfe2baed67e00f723435df7cb84156d79b3ae5486bed37b7292ef3e43`
- artifact_path: `root-native-fixture-r2/result.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: chunk=390b0e、实际退出 0；6/6；actualControllerSha256=4e6e185e…；初始/最终夹具 INI 分开保留。
- linked_workitem: n/a（普通维护）
- supersedes: E-005（仅替代其验收用途；不删除旧件）

### E-005 — 限定使用的旧 acceptance/r1

- title: 限定使用的旧 acceptance/r1
- observed_at: 2026-11-15T03:51:33.611158+00:00
- source_type: file
- source_ref: `root-application/acceptance-r1.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `d86fda0caa4dc2cbf1169e80dd0a0bc1fb362a6f0bca08d1c806ac4c44869f47`
- artifact_path: `root-application/acceptance-r1.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: r1 chunk=2093e5 实际退出 0/6 项，但复制的是 run_native_proxy_fixture.py，而非实际 run_root_native_fixture.py。
- linked_workitem: n/a（普通维护）
- supersedes: none

### E-006 — C11 Windows SDK 警告失败

- title: C11 Windows SDK 警告失败
- observed_at: 2026-11-15T02:53:50.873940+00:00
- source_type: command
- source_ref: `limited/c11-sdk-warning-r1/result.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `87248793d9a5829be127a5c857c2234bdd6677b83fdf407340d33284a012db78`
- artifact_path: `limited/c11-sdk-warning-r1/result.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: compile-c11 实际退出 2；不作功能通过。
- linked_workitem: n/a（普通维护）
- supersedes: none

### E-007 — PowerShell CRLF needle 失败

- title: PowerShell CRLF needle 失败
- observed_at: 2026-11-15T03:22:27.060917+00:00
- source_type: command
- source_ref: `limited/diagnostic-crlf-r1/failure-record.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `16bae592da1a4941b0f415d1846a87880ddae0f2b47d82c15271b8c6c5fccbdc`
- artifact_path: `limited/diagnostic-crlf-r1/failure-record.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: session=37462、chunk=9b4289、实际退出 1；Judge 0、Doorstop 1；installed=false。
- linked_workitem: n/a（普通维护）
- supersedes: none

### E-008 — 冷启动 Steam relay 运行观察

- title: 冷启动 Steam relay 运行观察
- observed_at: 2026-11-15T03:12:28.000468+00:00
- source_type: log
- source_ref: `runtime/runtime-r1/observation.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `6a48818747e8e7138a4b548a76e666ae284e695b07d88f511e2107d101fd0f17`
- artifact_path: `runtime/runtime-r1/observation.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: Doorstop/native 已加载，CoreCLR=false；旧 status 不作本次验收。
- linked_workitem: n/a（普通维护）
- supersedes: none

### E-009 — operation.prepare 安全拒绝

- title: operation.prepare 安全拒绝
- observed_at: 2026-11-15T03:32:01.821561+00:00
- source_type: log
- source_ref: `runtime/runtime-r2/observation.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `72a9e5b48bba1ea648d3e9a30dee5a783675ef85efc121a33258c1fdea8e536a`
- artifact_path: `runtime/runtime-r2/observation.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: pid=664、stage_code=38、result=0；original-return=1；SDK=false。
- linked_workitem: n/a（普通维护）
- supersedes: none

### E-010 — ready/tick 后模块注册失败

- title: ready/tick 后模块注册失败
- observed_at: 2026-11-15T03:42:22.925174+00:00
- source_type: log
- source_ref: `runtime/runtime-r3/observation.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `b2f9d93afab0caea5a8377469e737d36c62da5a337883eea429e109d971c74f6`
- artifact_path: `runtime/runtime-r3/observation.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: pid=40780、ready=49/result=1；实际日志先 Unity tick bound，后 Module manifest or interval is invalid；旧 status、无响应退出。
- linked_workitem: n/a（普通维护）
- supersedes: none

### E-011 — r2 native 范围验收

- title: r2 native 范围验收
- observed_at: 2026-11-15T04:11:41.667529+00:00
- source_type: file
- source_ref: `root-application/acceptance-r2.json`（ZIP 内路径；原始工具信息见下列摘录及该回执）
- content_hash: `39f563651a1e5117794b223b96351b64d88b6b91650275d012e4ca1be8a0ef58`
- artifact_path: `root-application/acceptance-r2.json`
- repro_command: `python check_evidence_archive.py doorstop-process-state-evidence.zip`（仅离线校验既有字节；不重复运行历史夹具/游戏）
- raw_excerpt: record tool chunk=0a669c、实际退出 0；native=true；gameSdkVerified/sdkGoalComplete/completeSeekPermitted 均 false。
- linked_workitem: n/a（普通维护）
- supersedes: E-005（验收用途）

## Findings

### F-001 — native 所有权修复在限定范围内有效

- severity: info
- category: other
- status: validated
- evidence_ids: [E-001, E-002, E-003, E-004, E-011]
- location: `native/RizToolbox.Doorstop/riz_process_environment.h`、`Apply-ProcessEnvironment.ps1`、`scripts/Build-Doorstop.ps1`
- impact: 子进程不会把继承的外进程私有标记当作自己的所有权；同进程重复 hook、手动禁用与 alias detach 保持守卫。
- confidence: high
- repro_steps: 使用归档 checker 核验静态修改、18 个构建输入、Root DLL 身份及真实父子进程日志；分别核对八项 helper 与六项 DLL 范围。
- remediation: 保留本次 native 修复；单独继续处理实际管理 host 初始化失败。
- optional_attack: empty（普通维护）

### F-002 — 旧 r1 的控制器冻结不充分，r2 补齐可追溯性

- severity: info
- category: other
- status: validated
- evidence_ids: [E-004, E-005, E-011]
- location: `limited/root-native-fixture-r1/actual-run_root_native_fixture.py` 与 `root-native-fixture-r2/run_root_native_fixture_r2.py`
- impact: 旧六项运行仍有日志，但旧 inputs 哈希只固定 generic runner，不能冒充实际 Root 控制器冻结；旧验收受限且原件保持。
- confidence: high
- repro_steps: 对照 r1 inputs 的 run_native_proxy_fixture.py 与实际控制器；对照 r2 actualController、__file__ 复制、初始/最终 INI 和输入哈希。
- remediation: 仅用 acceptance-r2 作为本轮 native 验收，继续保留旧 acceptance/r1。
- optional_attack: empty

### F-003 — 本轮运行证据不支持 SDK 目标完成

- severity: info
- category: other
- status: candidate（运行诊断定位；未修复验收）
- evidence_ids: [E-008, E-009, E-010]
- location: `runtime/runtime-r3/toolbox-new-session.log` 的 ModuleManager.Add / RizlineAdapter 构造栈
- impact: runtime-r3 到达 tick bind 后仍无法完成 host/adapter 初始化；旧 status 和无响应窗口不能作为 SDK 可用依据。
- confidence: high（对已有日志与未完成结论）；后续根因修复不在本包
- repro_steps: 离线核对三次 observation 与其原始日志；runtime-r3 同时读取 tick bind 行和后续异常行。
- remediation: 依据准确异常继续修复管理模块 manifest/interval，再另行验证当前会话、status、能力与游戏行为。
- optional_attack: empty

## Path 与时间线

### P-001 — native 进程所有权调用路径

- path_type: callflow
- start: 固定版本 Doorstop Windows 配置/入口
- goal: 同进程单一 hook 所有者与继承子进程独立所有权
- steps:
  1. action: 配置检查私有 hook marker，同时保留用户 disable/ignore 语义。— evidence: E-001 — finding: F-001
  2. action: 入口发布 hook 后记录 PID 与创建时间；alias 不清理别人的所有权或用户开关。— evidence: E-001, E-004 — finding: F-001
  3. action: 真实子进程拒绝外进程标记，获得自己所有权并保持 WinHTTP 转发。— evidence: E-003, E-004 — finding: F-001
  4. action: 使用实际 r2 控制器、Root DLL 和双份 INI 完成限定验收。— evidence: E-004, E-005, E-011 — finding: F-002
- residual_risks: helper CLR 标记不等于实际 CLR 初始化；本轮六项 DLL 只覆盖代理/hook 路径；所有权写入失败分支未注入。

### P-002 — 已有 SDK 启动诊断路径

- path_type: callflow
- start: 已授权的历史 Steam relay 与诊断候选
- goal: 定位当前 SDK 未完成的阶段
- steps:
  1. action: relay 已加载 native，但缺当前 CLR/SDK。— evidence: E-008 — finding: F-003
  2. action: operation.prepare(38) 安全拒绝，原始 IL2CPP 仍返回。— evidence: E-009 — finding: F-003
  3. action: 后续诊断到 ready(49)、当前 Loader 与 Unity tick bind，随后模块注册异常。— evidence: E-010 — finding: F-003
- residual_risks: 窗口无响应且 status 未更新；不宣称游戏 SDK PASS；诊断 overlay 未接纳到 Root。

# 2026-11-15 证据时间线（UTC）

| 时刻 | Evidence | 事件 |
|---|---|---|
| 02:53:50Z | E-006 | C11 首次编译实际退出 2；保留 Windows SDK 警告被 /WX 升级的失败。 |
| 02:54:26Z | E-003 | 使用固定 SDK 所需的 /Zc:preprocessor- 后，C11/C++17 编译和真实父子进程八项自然退出 0。 |
| 03:12:28Z | E-008 | Steam relay 已启用 Doorstop/native，未观察当前 CoreCLR/SDK 会话；旧 status 不作验收。 |
| 03:20:41Z | E-007 | 诊断候选 Doorstop 构建实际退出 1：CRLF here-string 与 LF 固定上游 needle 不匹配；未安装。 |
| 03:32:01Z | E-009 | pid 664 在 operation.prepare(38) 安全拒绝 0；原始 IL2CPP 返回 1。 |
| 03:38:15Z | E-001 | 应用三个 native 文件；该 application-record 当时的 rootNativeFreshBuildPassed=false 是历史状态。 |
| 03:42:22Z | E-010 | pid 40780 ready(49)、CoreCLR/当前 Loader；日志确有 tick bind，随后模块 manifest/interval 异常；status 未更新，测试窗口无响应并由 Root 结束。 |
| 03:45:22Z | E-002 | Root native 新构建自然退出 0，18 个输入不变。 |
| 03:51:33Z | E-005 | 旧 acceptance 记录 r1 六项成功，但 r1 误冻结泛用 runner；原件保留且限定使用。 |
| 04:00:17Z | E-004 | 新 r2 冻结 __file__ 实际控制器，保留初始/最终 INI；同一 Root DLL 六项自然退出 0。 |
| 2026-11-15T04:11:41.667529+00:00 | E-011 | 仅根据已完成证据写 acceptance-r2；未重跑夹具。 |


## 交付自检

- 已采用普通维护结构与 flavor=null，含授权/范围、Evidence、Finding、Path 和时间线。
- 每个关键结论均关联原始回执/日志与 SHA-256，旧失败与受限旧验收保留。
- 本次命令仅归档和校验；SDK 目标状态保持未完成。
