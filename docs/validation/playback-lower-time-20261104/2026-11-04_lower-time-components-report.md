# Lower-time 已完成子范围报告

本次保管的原生协议已通过 35 个独立场景、3597 项检查；managed 家族候选新构建和旧 22 个 Hook 安装通过；新 owned 双 DLL 已通过 6 个场景、238 项检查。原生协议与 owned 双 DLL 都没有执行 Dobby 安装。三成员机械验证、实际 SourceOwner 采用、23 个完整 Hook 和 SDK 尚未完成。范围见 [scope](D:/riz-build-cache-next/playback-lower-time-r1-20261104/retention-s1-r1/case/scope.md)，时间记录见 [timeline](D:/riz-build-cache-next/playback-lower-time-r1-20261104/retention-s1-r1/case/timeline.md)。

## Evidence

E-001 原生实际终态、E-002 managed 构建与旧安装、E-003 owned 构建与正确 static R2 均由 [retention manifest](D:/riz-build-cache-next/playback-lower-time-r1-20261104/retention-s1-r1/case/evidence/artifacts/retention-manifest.json) 逐原件保管。E-004 为 [全部去重原件](D:/riz-build-cache-next/playback-lower-time-r1-20261104/retention-s1-r1/case/evidence/artifacts/blobs.zip)。复核命令为 `C:/Python314/python.exe -B D:/riz-build-cache-next/playback-lower-time-r1-20261104/retention-s1-r1/seal.py --verify`，只读检查文件和 archive，不重跑功能测试。

### F-001 lower-time 独立 scheduling 协议已通过组件验证
- title: 独立原生协议的撤权与恢复边界已实证
- severity: n/a_re
- category: reverse_algo
- status: validated
- evidence_ids: [E-001, E-004]
- location: native/RizToolbox.PlaybackSourcePrototype/PlaybackLowerTime.inc
- impact: role22/kind8/flag2048、144B typed interruption、原调用前撤权及双侧 drain 已覆盖；重复子调用漏 Unknown 的真实失败与最小修补均保留。
- confidence: high
- remediation: 下一阶段结合真实 owned family 与完整 SourceOwner 验证；Progress/Unity backing 动态替换仍待覆盖。

### F-002 当前 managed 只接纳旧组
- title: 新家族代码编译并保持新角色拒绝
- severity: n/a_re
- category: design
- status: validated
- evidence_ids: [E-002, E-004]
- location: NativePlaybackEnginePitchPatchPair / NativePlaybackSourceHub
- impact: 969 文件候选有9个Runtime变化，最新五项目构建0；旧22 hooks安装11检查/roots0，正角色通过，新role22及2048位拒绝。不得据此签23组或provider。
- confidence: high
- remediation: 完成固定三成员机械验证后再接实际 SourceOwner 私票，禁止伪造 adopt receipt。

### F-003 新 owned 两文件已有可复核固定身份
- title: 新夹具提供真实 lower-time 身份及纠正后的BSS事实
- severity: n/a_re
- category: reverse_algo
- status: validated
- evidence_ids: [E-003, E-004]
- location: owned-time-fixture-build-r1/static-profile-r2/owned-time-profile-facts-r2.json
- impact: 新hash/RVA/RF/UW、registration、完整body与DIR64均来自实际产物；旧提取器把BSS误读为后续raw字节，原错误profile保留，R2接纳八字节zero-fill。owned raw+0x84不是实际Unity字段证据。
- confidence: high
- remediation: S2仅使用正确static R2；不得用旧profile或任意profile DTO铸造issuer。

### P-001 从 actual lower-time 尝试到后续完整接纳
- title: lower-time 离线验证的调用与证据路径
- path_type: callflow
- start: wrapper尾跳或相关direct lower
- goal: 为实际三成员及23组接纳提供已终态组件
- steps:
  1. action: 原生识别真实helper CALL/TLS父子并逐Scheduling撤权 — evidence: E-001 — finding: F-001
  2. action: 核managed候选及旧22组，保留role22拒绝 — evidence: E-002 — finding: F-002
  3. action: 固定新两文件与static R2供下一机械验证使用 — evidence: E-003 — finding: F-003
  4. action: 对每份原件和去重blob独立核SHA — evidence: E-004 — finding: none
- residual_risks: S2三成员、真实SourceOwner采用、S3完整23组、S4 SDK、generation/ABA其他writer/生产DSP/任意Seek/游戏实测均未完成。

## 后续机械修订与合主准备

原三成员S2 R2实际 Prepare3/Commit0，原因是Dobby真实搬走MOV5+PUSH RBP1、跳回target+6，R1只接纳5B。失败、诊断、其他已通过preflight与partial场景另保留在[原R2](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-family-r2/terminal-summary-r2.json)。[机械合同R2](D:/riz-build-cache-next/playback-lower-time-r1-20261104/contract-r2/contract.md)指定19B私有thunk与真实OS unwind边界。新R3尚待实际三成员及tamper验收；原S1保管通过不签此新产品。

R11 fresh-main runner已完成r2语法、容量及只读准备，完整验收仍pending。后续只能在六个原case/E012自然全0及App47实际合主后，新构建三个变体、短52与11实际PE/PDB。此报告未执行主源码应用、构建或功能测试。[准备记录](D:/riz-build-cache-next/playback-engine-pitch-draft-r1-20261103/main-verification-preparation-r11/preparation-result.json)。

本次strict/hash结果：[case-review](D:/riz-build-cache-next/playback-lower-time-r1-20261104/retention-s1-r1/case-review.stdout.json)，独立verify与2334/1210终态：[seal result](D:/riz-build-cache-next/playback-lower-time-r1-20261104/retention-s1-r1/test-result.json)。retention manifest SHA `eec4f83227b71f84e0681d8ac2799932befb8482bd08f23252bc24fc72107e96`。封存器首次prepare因证据扩展范围而失败的脚本及观察记录也已保留，新显式allowlist准备和封存实际0。
