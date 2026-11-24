# SDK 控制与 Retry 取消实测证据

冻结会话验证了 SDK 暂停后游戏时钟冻结、继续后同局时钟恢复，以及 Retry 对旧局产生 `Aborted` 结束和新 RunId。Skip 调度成功并返回 LevelList，但只观察到控制器消失，**没有验证到 Skip 的 RunEnded/Skipped 结束理由**。本目录不代表全部底层验收完成。

范围仅限本目录冻结日志、状态及安装检查；不读取当前游戏进程。采用 docs-generator 的 Evidence → Finding → Path 结构（flavor = null）。

## 重复检查

在项目根目录运行：

```powershell
C:\Python314\python.exe docs/validation/runtime-cancellation-20261020/inspect-runtime.py
```

脚本仅生成 [runtime-proof.json](runtime-proof.json)。本次检查通过 1064 条连续、单会话事件的长度/哈希，以及 17 个启动前安装文件的记录哈希与包清单匹配。会话为 `20261020T074327281Z-df2283a1652748e9893b06518df5b40f`，时间范围 2026-10-20 15:43:27–15:58:33（UTC+8）。摘要中的每条证据含序号、时间和原始文件行号。

输入为 [导出清单](session-pause-retry-skip/export-manifest.json)、[安装检查](installation-check.json)、[包清单](package-manifest.json) 和 [冻结状态](status-pause-retry-skip.json)。状态 SDK 1.2.0、包及插件版本 0.3.0、游戏指纹一致。安装记录没有替代进程内存中 DLL 的哈希证明。

## Evidence 与 Finding

| ID | 证据序号 | 可以证明的行为与限制 |
|---|---|---|
| E-001 / F-001 | 568、569、578、642、660 | Pause 调度成功、立即条件为 true，之后观察到 Paused；三次游戏时钟值相同，host 前进 356.178 秒、DSP 前进 356.181 秒。游戏时间保持声明的 game units，秒数映射仍未校准。 |
| E-002 / F-002 | 673、674、682、708 | Resume 调度成功，但立即条件为 **false**；随后观察到同 RunId 的 Resumed，708 的游戏时钟已继续前进。调度成功、立即状态和稍后的实际推进是不同证据，不能合并成即时全流程保证。 |
| E-003 / F-003 | 739–755、849 | Retry 凭据关联旧局 `0ae1e5b2f3df46199302bbbbdb2ea002`；751/753 的 RunEnded 为 `EndReason=4 (Aborted)`、`Observation.Source=2 (VerifiedCallback)`、`Result=null`。证据明确来自原始 Retry 返回后当前局 rooted info 的 GameObject `activeSelf true→false`；849 新局 `6abcca943c9a411da4388df930965109` 使用不同 ID。 |
| E-004 / F-004 | 749/750 → 751/753；874/875 → 876/877 | example.events 与 example.panel 的 run scope 都先记录取消、`active=False`，随后才收到普通 Retry 生命周期回调或 Skip 场景回调。此顺序不替代所有资源撤销矩阵。 |
| E-005 / F-005 | 748、782、873、887 | Retry 修改记录在新控制器出现后仍关联旧局；Skip 记录关联新局。15 个 compact 审计均为合法 JSON，跨快照保存的记录身份覆盖序号 1–7。5 条 `Truncated=true` 明确表示仅投影最后四条记录，并非日志把 JSON 截断；投影没有完整 Runs/全部字段。 |
| E-006 / F-006 | 142–162、151、186 | 测试音频加载返回 8000 帧、单声道、8000 Hz、有效；PLAY/STOP 均成功。相同 EffectId 的激活/释放记录完整对应，activeEffects 从 1 变 0，RunId 均为 null。没有由本脚本验证可闻输出、延迟或替换仲裁。 |
| E-007 / F-007 | 864、1064 | 最后一次自有按钮观察的累计 native/original 为 **13/13**，实际 ModuleProbe BUTTON 有 11 条；共享 hook 计数不能等同于全是探针点击。所有已记录 gameplay hook 样本的 native/original 相等，最后 judge 为 **604/604**；NormalMode pause 为 2/2、resume 为 0/0。后者不否定由其他游戏路径产生的恢复推进。 |
| E-008 / F-008 | 867、874–891 | Skip 凭据成功，随后 LevelList 场景及两个 scope 取消可证；889/891 为 `ObservationLost`，EndReason 和 Result 均 null。冻结会话只有 Retry 的一条 RunEnded，不能把 Skip 的控制器消失写成已验证结束或 Skipped。 |

全部五次控制调度凭据（两次 Pause、一次 Resume、一次 Retry、一次 Skip）的 `LocalResultBehaviorVerified` 与 `SubmissionBehaviorVerified` 均为 false。本会话没有 `game.run.finished` 实际结果事件，因此 Retry 的取消结束不能写成正常通关或分数结果验证。日志有 0 个 Error、4 个 Warning；这也不替代未覆盖的验收。

## Path

P-001：自有 Pause 按钮 → 成功凭据 → Paused 观察 → 游戏时钟冻结而 host/DSP 推进（E-001）→ Resume 成功凭据及立即 false → 随后 Resumed 与游戏时钟恢复（E-002）。

P-002：旧局 Retry 调度与旧局审计 → 两 owner scope 先取消 → VerifiedCallback 的 Aborted → 新控制器 → 新 RunStarted，不复用 ID（E-003–E-005）。此路径证明所测 Retry 取消行为，不覆盖其他退出方式或晚到结果来源。

P-003：Skip 调度 → 新局审计 → scope 取消与 LevelList → ObservationLost（E-008）。路径缺少 Skip 的独立结束证据，因此相关验收仍未完成。

尚待完成的内容包括 Skip 结束理由、正常完成与实际结果、其他退出路径、全部输入/模态/资源撤销矩阵、音频内容替换及延迟验收和性能基线。原始事件文件的定值在导出清单中，由脚本重新计算；其余输入的 SHA-256 写入生成摘要。
