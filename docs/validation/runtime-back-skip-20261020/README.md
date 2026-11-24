# 603 检查包的 Back / Skip 实测

正式安装后的所选会话证明：原生 Back 请求及匹配终止边沿产生旧局 `Aborted`；SDK Skip 的精确原调用返回后排队，凭据和审计先完成，再产生新局 `Skipped`。两名 owner 的 run scope 均在普通生命周期回调前失效。没有为这些操作制造实际结算结果，也没有重复结束或 ObservationLost。

此证据只属于本目录包与会话；不能把上一包的 Pause/Resume/Retry 结果迁移成此包全部重测通过。完整 B01–B19 仍未验收，正常通关、迟到结果来源及许多公共能力矩阵仍缺。

## 重复检查

在项目根目录运行：

```powershell
C:\Python314\python.exe docs/validation/runtime-back-skip-20261020/inspect-runtime.py
```

脚本只读冻结输入，生成 [runtime-proof.json](runtime-proof.json)。已实际运行通过：**1095 条单会话连续事件、导出文件长度及哈希、17 个启动前实际安装哈希与包清单一致、24 项目603/603离线摘要**。会话为 `20261020T101511620Z-c6589396de114bf2aac7a3f1dfe89a7a`，时间 2026-10-20 18:15:11–18:18:52（UTC+8）。每个摘录保留原序号、时间与 `sourceRef` 文件行号。

输入包括 [导出清单](session-back-skip/export-manifest.json)、[安装检查](installation-check.json)、[包清单](package-manifest.json)、[冻结状态](status-back-skip.json)、[Player 日志](Player-back-skip.log) 和 [构建摘要](build-summary.json)。原始构建摘要的 `nativeAcceptance` 是构建时状态；本目录独立实测证明记录随后安装与运行，不回改历史摘要。安装器输出成功且17项哈希匹配；外层调用曾因读取旧 LASTEXITCODE 误报失败，已在安装检查中记录，没有重复安装。

## Evidence → Finding

| ID | 事件序号 | 可验证结论 |
|---|---|---|
| E-001 / F-001 | 492、529–537 | 旧局真实开始后，Back 请求529只保留 token；530匹配原生 Update 的 `_terminated` 与 controller `_skip` 边沿后排队。533/535 的终止为 VerifiedCallback、Aborted、Result=null。请求本身没有提前结束。 |
| E-002 / F-002 | 531/532 → 533/535 | example.events 与 example.panel 的 scope 先取消，均 `active=False`，普通终止监听者随后收到事件。 |
| E-003 / F-003 | 782、790、899、922 | 新控制器出现时旧 Pause 审计仍关联旧 RunId；新局使用不同 RunId。最终两条记录分别为旧局 Pause 与新局 Skip，没有串局。此会话没有 Retry 记录。 |
| E-004 / F-004 | 890–906 | Skip 原方法返回时验证 controller `_skip false→true` 与同一 rooted PauseUI `enabled true→false`，891排队；893成功凭据、899旧生命周期审计先完成，再于902/904派发 Skipped、Result=null。 |
| E-005 / F-005 | 900/901 → 902/904 | 新局的两个 owner scope 在普通 Skip 终止回调前失效，保持与 Back 相同的边界顺序。 |
| E-006 / F-006 | 492、790、533/535、902/904、537、906 | 两个真实开局、每名 owner 各两条一致的结束、宿主仅两条结束；没有真实 game.run.finished、合成 Result、重复终止或 ObservationLost。 |
| E-007 / F-007 | 77；冻结 status | 键盘为 RuntimeAvailable；Mouse.current 因 disabled 被降为 SymbolVerified、跳过控制读取并允许重探测；手柄 Unsupported。快照不能证明快捷键边沿已执行。自有 UGUI 按钮独立于此鼠标采样路径。 |
| E-008 / F-008 | 91–96；冻结 status | Retry/Back/Skip 模块实际 active，版本化观察能力 available；运行时共22个模块 active，设置 override 等待实际设置页面。RuntimeProbed 只证明接入就绪，不代表各模块完整验收。 |

旧 RunId 为 `114a7ad4e92746d5a67ee776fe4d2b0d`，新 RunId 为 `9ffd5c2f4bf9454189fd77ee184a39a3`，谱面一致。最终原生按钮 native/original **7/7**、judge **106/106**；所有保存的计数样本一致。这是共享 hook 的累计观察，不把全部次数归属为某一个插件按钮。

全部保存的紧凑审计内部 JSON 有效、无截断，Recorded=2、Unassociated=0、activeEffects=0。Result/submission 的实验操作标志仍为 false。零 Error 不能替代性能或完整功能验收。

## Path

P-001：真实旧局开始 → Back 请求 → 匹配终止 Update 边沿 → 排队 → 两 owner scope 失效 → Aborted 普通回调 → LevelList（E-001/E-002）。

P-002：同谱新局使用新 ID → Skip 精确返回及两个状态边沿 → 排队 → 成功凭据及原局审计 → 两 owner scope 失效 → Skipped 普通回调 → LevelList（E-003–E-006）。

上述路径证明所测 Back/Skip 终止，不覆盖全部退出方式、同谱迟到结果来源、正常完成和快速重复进入。

## 尚未验收

MaterialProbe 窗口已存在，材质模块实际就绪；本次冻结会话没有材质参数按钮操作，所以 **Color/Float 创建、原生参数写入读回及恢复仍待实测**。主代理后续操作应使用另一份冻结证据，不改写此会话范围。

上一582检查包的暂停冻结、继续推进及 Retry Aborted/scope/audit证据在 [runtime-cancellation-20261020](../runtime-cancellation-20261020/README.md)。更早的失败结算、UI编辑、音频及导航证据分别保留在各自目录。它们可共同说明工程进展，不构成当前包全能力验收。

后续仍需正常通关、迟到结果、判定身份/时间单位、作用域压力、真实输入/焦点及双挂载、独立玩法视觉目标、本地曲包音乐/封面与替换矩阵，以及四基线/20轮资源稳定性和升级验证。原始事件定值在导出清单中；输入文件 SHA-256 由 inspector 写入摘要。
