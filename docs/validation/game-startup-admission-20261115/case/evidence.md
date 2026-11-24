# Evidence

共同复核命令（只校验归档，不重跑历史构建）：`python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`。每项 `artifact_path` 都相对归档根目录。

## E-001

- title: 当前 v2 完整冻结图
- observed_at: 2026-11-15T04:27:35.475657+00:00
- source_type: file
- source_ref: runs/r2/result.json
- content_hash: fbb63a9cd13d61b7df8a1625afc7ebaa54b849f568c24168e950ce24f2b39538
- artifact_path: runs/r2/result.json
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: passed=true; completeSolutionInputs=1172; fullFrozenInputsUnchanged=true
- linked_workitem: WI-001
- supersedes: none

## E-002

- title: r2 原始进程句柄 wait 与控制器身份
- observed_at: 2026-11-15T04:27:35.475657+00:00
- source_type: file
- source_ref: runs/r2/supervision/controller-exit.json
- content_hash: 70fb9930f619a7c2c4fb4743d45371f1449d49e87da08ec8991b170b36561f89
- artifact_path: runs/r2/supervision/controller-exit.json
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: supervisor34372/controller43772; actualControllerExitCode=0; original subprocess.Popen.wait process handle
- linked_workitem: WI-001
- supersedes: none

## E-003

- title: Runtime admission 生产实现
- observed_at: 2026-11-15T04:40:31.331832+00:00
- source_type: file
- source_ref: source/src/RizToolbox.Runtime/Hooks/NativeRuntimeInvokeBridge.cs
- content_hash: a3511e035b479e9604d34948f6251a41ae52a26c5826792b594ef16ed1bb6472
- artifact_path: source/src/RizToolbox.Runtime/Hooks/NativeRuntimeInvokeBridge.cs
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: frame/thread -> channel -> fixed filter -> VerifyReady -> channel -> dispatch; ignored calls return before full proof
- linked_workitem: WI-002
- supersedes: none

## E-004

- title: 当前 tick 专项日志
- observed_at: 2026-11-15T04:27:35.475657+00:00
- source_type: log
- source_ref: runs/r2/tick-admission.log
- content_hash: 8da3ff6688067e5306b3e5e31d38aab7e30d87835473266069ec5d6997f128e6
- artifact_path: runs/r2/tick-admission.log
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: 8/8 passed; exceptions/channel mutation/current frame/proof checks
- linked_workitem: WI-002
- supersedes: none

## E-005

- title: v2 模块 overlay 与最小正规数契约
- observed_at: 2026-11-15T04:40:31.331832+00:00
- source_type: file
- source_ref: controls/module-v2/overlay-manifest.json
- content_hash: e1f5442457bd8f1aedf044c26ecd838165669756c18540f3c38d2d20466d6ee3
- artifact_path: controls/module-v2/overlay-manifest.json
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: 2.2250738585072014e-308; bits0x0010000000000000; 30 actual built-ins; no FP hardware state claim
- linked_workitem: WI-003
- supersedes: none

## E-006

- title: 当前 Module31 日志
- observed_at: 2026-11-15T04:27:35.475657+00:00
- source_type: log
- source_ref: runs/r2/suite-Module.log
- content_hash: 6f736cf4203fb61a596ccdcfc77a3fa0a32decb59a7a85527a772bdca17a16b8
- artifact_path: runs/r2/suite-Module.log
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: 31/31 passed including 30 real builtin registrations and actual camera module advancing ticks
- linked_workitem: WI-003
- supersedes: none

## E-007

- title: 零间隔负控
- observed_at: Historical v1 validation; original log has no timestamp
- source_type: log
- source_ref: negative-control/baseline-module-tests.log
- content_hash: 763eccdb40ec828837f229590f38a9402dc79522ebdb5fa4eae297d77581062c
- artifact_path: negative-control/baseline-module-tests.log
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: 29/31; exactly camera production registration and camera advancing-tick checks fail
- linked_workitem: WI-003
- supersedes: none

## E-008

- title: 完成的安装记录
- observed_at: 2026-11-15T04:29:36.803774+00:00
- source_type: file
- source_ref: install/result.json
- content_hash: d5758f8daf4a1443a7e868d978809baf1238643624878e09dc242bc5b4d7b2c1
- artifact_path: install/result.json
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: actualInstallerExitCode=0; naturalExit=true; verifiedPackageFiles=23; userConfigurationPreserved=true
- linked_workitem: WI-004
- supersedes: none

## E-009

- title: 当前自有包清单
- observed_at: 2026-11-15T04:29:36.803774+00:00
- source_type: file
- source_ref: install/package-manifest.json
- content_hash: 27a9e46348807f23098f3008ca05ff61da688338d67c634bf8be91dafe464609
- artifact_path: install/package-manifest.json
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: 23 owned paths; current six DLL and current Examples hash identities
- linked_workitem: WI-004
- supersedes: none

## E-010

- title: 有限游戏启动观察
- observed_at: 2026-11-15T04:33:55.959972+00:00
- source_type: file
- source_ref: runtime-r1/observation.json
- content_hash: b0295c13d99432c9e7e220c51f5986c765333c493617f5cf724bd37301a26c09
- artifact_path: runtime-r1/observation.json
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: PID15924; exact23 installed verified; Loader/tick/host=false; no round; normal exit
- linked_workitem: WI-005
- supersedes: none

## E-011

- title: 当前 native 启动日志
- observed_at: 2026-11-15T04:33:55.959972+00:00
- source_type: log
- source_ref: runtime-r1/native-preinit-current.log
- content_hash: 7ee8b9e14bd2f6a82c89a42039f57919111a76977b5448d58f248b09bceeeef8
- artifact_path: runtime-r1/native-preinit-current.log
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: operation.prove.block-present stage145 result0 row14 dobby0; il2cpp original-return result1
- linked_workitem: WI-005
- supersedes: none

## E-012

- title: v1 原始结果
- observed_at: 2026-11-15T04:22:05.222474+00:00
- source_type: file
- source_ref: runs/r1/result.json
- content_hash: 515e267437e7aa6596f6c0914a3ebdc869c9ca0aab308b4ce418804b76758280
- artifact_path: runs/r1/result.json
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: 10 natural zero steps; 884 ordinary cases; only v1 source scope
- linked_workitem: WI-001
- supersedes: none

## E-013

- title: 具体 RuntimeBridge 的错误与预绑定边界
- observed_at: 2026-11-15T04:40:31.331832+00:00
- source_type: file
- source_ref: source/src/RizToolbox.Runtime/RuntimeBridge.cs
- content_hash: e0fcc49f69ca34a4428d8185cb8e84bb24dbbb82017347aa82cb35788b628031
- artifact_path: source/src/RizToolbox.Runtime/RuntimeBridge.cs
- repro_command: `python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip`；归档只核对记录与字节，历史实际执行须原 Windows 工具链，不在 checker 中运行。
- raw_excerpt: IsMethod metadata predicate before proof; OnProvenAfterInvoke catches existing host tick errors locally
- linked_workitem: WI-002
- supersedes: none

