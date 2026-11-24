# 原生自有按钮会话的冻结证据

本会话证明自有按钮回调实际执行、测试音频加载/播放/停止返回成功，以及一次主菜单导航随后到达 LevelList。它不证明 B14、B15 或全部底层任务完成。两次真实开局均没有可验证的结束/结果事件；完整修改审计也受日志截断影响。

本次整理使用 docs-generator 的证据链结构（flavor = null）。范围仅包括本目录冻结文件，不接触仍在运行的游戏，也不重新归属来源不明的点击。

## 复核

在项目根目录运行：

```powershell
C:\Python314\python.exe docs/validation/runtime-unified-20261020/inspect-native-button.py
```

检查脚本验证导出清单中的文件长度及 SHA-256、单会话、连续序号、状态会话/事件数、游戏指纹、SDK 与插件版本，并生成 [evidence-summary.json](evidence-summary.json)。本次输出为：

```text
Verified 2434 frozen events; 31 buttons; 8 audio cycles; 1 menu navigation; 2 run starts / 0 verified finishes; 2 complete + 26 truncated audit snapshots. Full acceptance remains unproven.
```

会话为 `20261020T044257527Z-6d89f92920c54e4c987431041cd2c0f6`，冻结范围为 2026-10-20 12:42:57 至 14:55:21（UTC+8），序号 1–2434，导出没有丢弃不完整尾行。该时间跨度不等于持续执行了两小时验收。

## Evidence

每条事件的文件与行号、时间、原始序号，以及下表引用的具体数据，均保存在生成摘要的 `sourceRef` 中；原始 JSONL 是最终依据。

| ID | 冻结来源 | 观察 |
|---|---|---|
| E-001 | [session/export-manifest.json](session/export-manifest.json)、[session/session.json](session/session.json) | 2434 条外层合法 JSON，单会话且序号连续；三个事件文件及 session.json 的长度/哈希与清单相符。 |
| E-002 | [package-manifest.json](package-manifest.json)、[status-at-export.json](status-at-export.json) | 清单记录 17 个文件；包版本 0.3.0、SDK 1.2.0、状态中的插件版本及已知游戏指纹一致。状态没有提供已加载 DLL 的哈希，不能独立证明 17 个文件在启动时逐一匹配。 |
| E-003 | 序号 101–116，12:43:21–12:43:31 | audio 按钮 → 加载返回 8000 帧、单声道、8000 Hz、有效 → EffectActivated → PLAY 成功；stop 按钮 → EffectReleased → STOP 成功。此时没有 RunId，记录明确为 unassociated。 |
| E-004 | 序号 191–198，12:44:43 | navigation 按钮 → 唯一目标可用 → NAVIGATION RECEIPT 成功 → 观察到 MainMenu → LevelList。 |
| E-005 | 序号 492、509–510、832 | 第一次真实开局后，pause 按钮收到 CanDoOperation 拒绝。稍后观察到同谱面的第二个不同 RunId。 |
| E-006 | 序号 675–993，12:56:01–12:56:33 | 额外按钮实际执行；其中音频 EffectActivated/Released 带两次已观察 RunId，Retry 有一次成功调度。诊断日志没有记录操作人，不能归属为全由 agent 操作。 |
| E-007 | 序号 723–746、829–833 | Retry 成功凭据关联第一个 RunId，随后 Level → ToLevel → Level，出现新的 RunStarted；两个观察插件的 run scope 在 735/736 取消。旧控制器在 740/742 明确是 ObservationLost，EndReason/Result 未被验证。 |
| E-008 | RUN MODIFICATIONS 序号 79、110 及其余 26 条 | 仅前两条内部 JSON 完整可解析；110 中首个 EffectId 同时出现在记录和 ActiveEffects。余下 26 条原始 message 均为 **2048 字符**并截断在 JSON 值中，解析失败被逐条保留，没有丢弃。 |

协调任务在日志之外观察过 12:43–12:45 的 agent 操作，因此摘要将该时间段标成外部观察窗口；12:56 的额外点击只归类为“实际执行、操作来源未建立”。日志本身不具有操作者身份字段。

## Finding

| ID | 结论及证据 | 范围限制 |
|---|---|---|
| F-001 | 自有 UI 按钮可驱动实际 SDK 操作；31 个 BUTTON 记录包含 audio、stop、snapshot、navigation、pause、retry、skip、resume（E-003–E-007）。 | 不覆盖快捷键、模态冲突、多插件和所有挂载点矩阵；拒绝凭据不等于操作成功。 |
| F-002 | 8 组音频加载、EffectActivated、PLAY、EffectReleased、STOP 依次出现，所有播放/停止凭据成功；其中首组无 run 关联，后 7 组带已观察 RunId（E-003、E-006）。 | 此脚本不能证明扬声器可闻、准确延迟、替换仲裁矩阵或停止后的完整审计状态。 |
| F-003 | 一次导航凭据之后确实观察到 LevelList；一次 Retry 凭据之后观察到同谱不同 RunId 的新开局（E-004、E-007）。 | 本会话 **0 对**可验证的开始/结束关联；没有 game.run.finished 或 lifecycle RunEnded，不把 ObservationLost 写成 abort/结束。 |
| F-004 | 修改审计目前不足以验收：28 个快照中 26 个被截断（E-008）。 | 导出哈希与外层 JSON 完整，并不能补足被截断的内部 Records、Runs、ActiveEffects、Counters；不能宣称审计全部通过。 |

六次 pause 凭据均由游戏的 `PauseUIControl.CanDoOperation` 拒绝，属于可验证的保护行为。本目录不能据此宣称 SDK 暂停、继续已实测成功。日志有 4 个 settings/data 等待或挂起 Warning，0 个 Error；零 Error 也不替代完整验收。

## Path

P-001（自有按钮调用）：按钮实际激活（E-003）→ PCM 音频加载成功 → 原生播放返回 → 修改记录激活 → Stop 返回与释放观察；该路径支持 F-001/F-002。其余声学输出与仲裁矩阵仍需独立验收。

P-002（菜单导航）：自有按钮激活（E-004）→ 检查目标可用 → 原方法调度返回成功 → 随后的场景观察为 LevelList；该路径支持 F-003，只有这一条导航目标被本次证据覆盖。

P-003（重试与观测缺口）：第一个真实 RunStarted（E-005）→ Retry 调度（E-007）→ 场景切换与 scope 取消 → 旧控制器 ObservationLost → 第二个 RunStarted。缺少实际结束/结果证据，因此保持“开始/结束关联未完成”。

下一轮应把修改审计分成低于日志上限的独立 JSON：计数器、run 摘要、逐条 record、逐条 active effect，带明确的页数/总数；在 Stop 与 Retry 后核对释放状态，避免再次输出超过长度限制的整棵快照。完整的错误信息及原始前后缀已保存在摘要的 `runModificationAudit.malformedSnapshots`。

## 文件定值

| 文件 | SHA-256 |
|---|---|
| package-manifest.json | `d9ca7b50bb5bffb7a7dab4634d9477aa096627400ce1ca40415cb202e05b349f` |
| session/export-manifest.json | `3272a9d725660c8654c2141884b005fa26f69b20c60fec3336af0449e49d7c03` |
| status-at-export.json | `b38bc03b10036ce8a90c9819afaa0b7507890f7724662e4ad1578fcdeac0800a` |

事件文件的 SHA-256 来自导出清单，并由脚本重新计算验证。生成摘要没有取代原始证据。
