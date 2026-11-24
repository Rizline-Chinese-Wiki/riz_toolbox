# Playback attestation 连续分配维护验收

日期：2026-11-15。类型：普通原生维护，`flavor = null`。ROOT 的生产修复已应用，独立新构建与两种精确 `Prove` fixture 均有已有成功记录。此次仅汇总原始证据并封装可携带归档。实际游戏观察使用诊断 helper，验收范围仅为启动和有限界面流程。完整 SDK、完整一局和真实 Speed/Seek 仍未验收。

[范围与授权](validation/playback-attestation-allocation-20261115/scope.md) · [时间线](validation/playback-attestation-allocation-20261115/timeline.md) · [新原生闭环验收](validation/playback-attestation-allocation-20261115/root-native-closure-acceptance.json)

## 维护结论

`Prove` 改为一次 `VirtualAlloc(nullptr, 8192, ...)` 获取连续两页，代码页使用 `PAGE_EXECUTE_READ`，相邻 metadata 页使用 `PAGE_READONLY`，并有 4096 字节实际页大小守卫。生产和诊断 overlay 对称，移除诊断观察后恢复生产逻辑。原有 RAW arena、unwind 注册/验证、ABI 和 profile 约束保持在审计范围内；`Prove` 外发生 LF→CRLF 规范化，因此只声明规范化文本不变。

| 身份 | SHA-256 |
|---|---|
| ROOT 生产 inl | `331ea0bf193fcdeb549abf8b5e699314ae0acaa9ef88ed2cdc586c6bdaec9813` |
| ROOT 生产 helper | `a6b35fc6dead8977df86b25e00ea6975bf88f68fe05a401e3fd86e51d742fb97` |
| 实际受测诊断 helper | `e92f5a095c9ed6341fc7adb53d85ca99c77b0c2534560a0037fc79651c1d59b4` |
| fixture acceptance | `7136f10bf5f5decc16cff7058c8619504f0230a8aeca062acba6337e6a1a40f9` |

精确 fixture 已分别完成 801/802 checks、28 originals、每轮 155 个操作系统 lookup/unwind 边界，最大 metadata 为 228 字节，控制器和两种 EXE 都自然退出 0。归档保留首次 `Windows.h` 缺失失败；首次记录中的旧源哈希只描述当时失败，不能替代后续通过记录。ROOT 新构建使用 `-SkipFixture`，因此不声明重新执行全部原生回归。

不可变 application record 产生于新构建之前，其 `rootNativeFreshBuildPassed=false` 保留历史事实；新的 closure 通过实际构建 result 的 SHA-256 绑定后续成功，不改写旧记录。

## Evidence

下列 `artifact_path` 均为 ZIP 内路径；`source_ref` 为同一路径；`source_type=file`，`linked_workitem=n/a`，`supersedes=none`。每项的离线复核命令均为下节 ZIP-only checker；无需访问原 C:/D: 路径，也不执行归档 EXE。

| E-id | artifact_path / source_ref | observed_at (UTC) | content_hash (SHA-256) | raw_excerpt / title |
|---|---|---|---|---|
| E-001 | `allocation/overlay-manifest.json` | 2026-11-15T05:09:12.151235+00:00 | `8089083b4c99ac5816aced606bd35870079ade9f761e3a2095ddd8f0f76174e6` | 8192 字节单次 nullptr 分配；4096 字节页；RX/R 分离 |
| E-002 | `allocation/source-scope-audit.json` | 2026-11-15T05:09:12.151235+00:00 | `655ac76750a8b0b0100f8c90d57c1dfc593268f25712665caaabf9e72e925efb` | Prove 外仅规范化文本不变；不宣称原始字节不变 |
| E-003 | `allocation/fixture-acceptance.json` | 2026-11-15T04:41:25.790248+00:00 | `7136f10bf5f5decc16cff7058c8619504f0230a8aeca062acba6337e6a1a40f9` | 801/802 checks；28 originals；每轮 155 个 OS lookup/unwind 边界；最大 metadata 228 字节 |
| E-004 | `builds/root-production/result.json` | 2026-11-15T04:56:27.491087+00:00 | `0ca18305066d9de9cf08c762fb58cd35619d0769f462d62fc5cea4673ab75d60` | ROOT 新构建自然退出 0；输入哈希未变；85 exports / V7 / 26 operations / 64 writes |
| E-005 | `runtime/runtime-r2/observation.json` | 2026-11-15T04:53:30.590749+00:00 | `07780b24cede6f4a6ec53dabc3f9969a76cb4cd6ea305e77ce74b600ea4551d7` | 诊断 helper 启动与有限 UI 通过；完整验收为 false |
| E-006 | `root-native-closure-acceptance.json` | 2026-11-15T05:09:12.151235+00:00 | `f08afae9432058009658a938968a8e33a09e7427609c0317d999b3d99b00dc9f` | 绑定 ROOT 实际构建与两种精确 Prove fixture |
| E-007 | `allocation/fixture-r1/test-result.json` | 2026-11-15T05:09:12.151235+00:00 | `9cb20af5ea87519986ab1424e22b3ace671795caa08d161b4df6dc9a280ea859` | 首次因 Windows.h SDK 环境缺失而失败；只保留该失败范围，不冒充通过 |

## Finding → Path

| F-id | title / impact | severity | category | evidence_ids | confidence | location | status | remediation / repro_steps |
|---|---|---|---|---|---|---|---|---|
| F-001 | 连续分配修复已在 ROOT 生产源码与独立构建闭环 | info | other | E-001, E-002, E-003, E-004, E-006 | high | FirstInstallPlaybackAttestation.inl: Prove | validated | 保留修复；运行下节只读 checker 复核来源与构建身份 |
| F-002 | 已有游戏证据只支持启动和有限界面流程 | info | other | E-004, E-005 | high | runtime/runtime-r2/observation.json | validated | 保持完整 SDK/游戏/Seek=false；后续验收另立任务 |

P-001，`path_type=callflow`：start=已有冻结源码；goal=可移交的原生维护证据。步骤：1. 核对两种 baseline/overlay 与 Prove 范围（E-001/E-002→F-001）；2. 绑定两种精确 fixture 与 ROOT 实际新构建（E-003/E-004/E-006→F-001）；3. 独立绑定受测诊断 DLL、23 文件安装记录与有限启动观察（E-005→F-002）。residual_risks：ROOT 生产 helper 未宣称游戏实测；完整原生回归和完整 SDK 未完成。

## 有限运行观察及未完成项

原观察记录 producer ready、original IL2CPP return=1、CoreCLR、Unity tick 与 host initialization，当前状态含 30 个模块（28 active、1 waiting、1 unsupported）。界面覆盖主菜单、选曲、FREE EASY 开始、失焦自动暂停与恢复、返回选曲/主菜单，游戏正常退出。未核验正常结算、精确 Back cancellation 或 B01 全部场景；NormalMode camera target 缺失，appearance getter unsupported；`completeSeekPermitted=false`、`sdkGoalComplete=false`。

## 离线复核与附件

[证据 ZIP](validation/playback-attestation-allocation-20261115/playback-attestation-allocation-evidence.zip)，1418776 字节，80 个索引成员，外层 SHA-256：`408aa09910ce3346a149a049920820f0f4a7013737fc06a507f594367a17f4dd`。索引逐项绑定原始源码/日志/清单/自有 DLL 与 EXE。归档不包含游戏二进制、Dobby DLL、用户配置内容或 253 个安装备份；安装 result 仅保留备份的历史元数据。

在仓库根目录运行：

```powershell
python docs/validation/playback-attestation-allocation-20261115/check_allocation_evidence_zip.py docs/validation/playback-attestation-allocation-20261115/playback-attestation-allocation-evidence.zip --expected-sha256 408aa09910ce3346a149a049920820f0f4a7013737fc06a507f594367a17f4dd
```

该命令只读取 ZIP，验证 CRC、成员索引、跨记录 SHA-256、两种精确 overlay、构建源码包、85 个真实 PE exports 和有限验收边界；不执行归档程序。原生构建控制器和环境 shim 也已归档，重新构建所需 Windows SDK/MSVC 属于历史外部依赖，不属于本次只读复核。

预览交付包仅按已有身份引用：`artifacts/delivery-20261115/RizToolbox-0.3.0-startup-preview-20261115.zip`，36,997,650 字节，SHA-256 `76a3ee353a73c0436547b6fbc13e90a4e27ad3170abc1cd7802ad5842fb8801e`；未重复收入本证据包。旧原生流程和旧启动离线证据包保持独立。

文档自检：已选择普通维护 flavor=null；范围、原始哈希、Evidence→Finding→Path、时间线、复核命令及残余限制齐全；本次没有重建、重跑 fixture、安装或操作游戏。

实际离线复核：335 项检查通过，退出码 0；详见 [核验结果](validation/playback-attestation-allocation-20261115/zip-only-check-result.json) 和 [实际工具回执](validation/playback-attestation-allocation-20261115/tool-observations.json)。

工具引用更正：原闭环中两个历史工具 chunk 误附带了末尾退出码 0，正确值为 ROOT 构建 `f2605c` 与诊断构建 `6264a8`。详见 [独立 provenance 更正](validation/playback-attestation-allocation-20261115/provenance-correction.json)。已发布 ZIP、原 closure、原构建 JSON/log SHA 与验收结论均保持不变，未重跑构建。
