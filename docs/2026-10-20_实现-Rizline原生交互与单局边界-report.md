# Rizline 原生交互、玩法控制与单局边界修复

本批已产出新候选包，通过 24 个项目、582/582 项完整离线回归。此前正式安装的按钮包已实测按钮、音频调用和一次菜单导航；本批最新暂停、材质参数、Retry 取消及 scope 修复尚未安装，不能计为实机验收。底层完整范围仍以 [SPEC](../.docs/foundation-expansion/SPEC.md) 与[实现状态](IMPLEMENTATION-STATUS.md)为准，任务没有全部完成。

## 范围与交付

用户授权的目标是 PC Mod 兼容底层及自主实测，仓库位于桌面 `riz_toolbox`。此报告覆盖 B01/B02/B05/B07/B09/B11/B13/B14/B15 的本批增量，采用普通实现/逆向报告结构，`flavor = null`。原始游戏二进制保持原样；没有提交或推送仓库。

新候选位于 `artifacts/package/`，准确文件清单为 [package-manifest-latest-candidate.json](validation/runtime-unified-20261020/package-manifest-latest-candidate.json)。它仍需通过项目正式安装器安装；不是包含全部运行环境的最终发布版。游戏原始 build 为 Steam 25977441、Unity 2022.3.62f2、Windows x64，GameAssembly SHA 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。

## Evidence

以下为文件观察，具体时间、序号和命令保留在各产物中。哈希清单为 [implementation-evidence-hashes.json](validation/runtime-unified-20261020/implementation-evidence-hashes.json)。

| ID | 来源 | SHA-256 | 能证明的范围 |
|---|---|---|---|
| E-001 | [完整构建摘要](validation/runtime-unified-20261020/cancellation-final-build-summary.json) | `25d2d242fd5de51f78ec8ddf4bdb475addee50647e32c55f84def487774e30a2` | 24 项目582项通过，包括SDK兼容、模板加载和新增回归；不是游戏运行 |
| E-002 | [按钮包冻结摘要](validation/runtime-unified-20261020/native-button/evidence-summary.json) | `867f326d772b631c4c324fc696ddad820445225325d2e8ba23322f2563ee38b8` | 2434条事件、31次按钮回调、8组音频调用、一次导航、两次开局；无已验证结束 |
| E-003 | [暂停静态证明](validation/gameplay-controls-20261020/static-pause-guard/pause-guard-static-proof.json) | `214e5be7e916c84e0fb7f1fdd21e1354284d2c279584ad346235c169a6d9b05f` | 18函数、实际守卫和停止/暂停/恢复调用链 |
| E-004 | [取消静态证明](validation/lifecycle-cancellation-20261020/cancellation-static-proof.json) | `c7135396c5789f5aaf3f62fd59b1f173dc55eb8dcd7097eca70402c35ec9b587` | Retry正常接受路径的info GameObject失活边沿；Back有额外流程，不能照搬 |
| E-005 | [材质序列化来源](validation/visual-20261020/static-profile-evidence.json) | `a40d9fa8e5938ef2d0c588c0153e45a125186b61b10c043bc842b46e2b4b15f5` | 实际Shader属性类型、Range、Resources引用与输入文件身份 |
| E-006 | [部署状态](validation/runtime-unified-20261020/deployment-status.json) | `26798587aea8ae89f566ab5f1028e0336e1b63153d3adb7b6e60a671cdafedc5` | 当时游戏PID27052仍存活；旧包17个文件仍匹配，新候选未安装 |

最新重打包见 [package-final.log](validation/runtime-unified-20261020/package-final.log)。完整582项通过后仅修改了取消能力的诊断说明，再跑Game54项并重打包，均无编译警告或错误。早先一次.NET6 Stream hash编译错误及一次测试夹具在解绑后仍要求活跃上下文的失败保留，没有抹除；修正后均通过。

## Finding

F-001（已实机验证，E-002）：原始鼠标位置采样未能驱动按钮回调；改为观察自有 UGUI Button.Press 原调用后的语义激活，再经有界主线程队列交付。队列重新检查模态、scope和控件存活，几何路径不再重复触发。真实输入框键入另有独立日志。键鼠动作、双插件焦点及全设备映射仍未验收。

F-002（静态与离线已验证，实机待验，E-001/E-003）：`CanDoOperation`实际为`_pausing && !_over`。此前把它作为Pause公共前置，会拒绝正常游玩中的暂停；只调PauseUI.Pause也不会停止谱面。修复为独立检查原始倒计时/结束/UI gate，先controller.Pause，确认不再Playing，再调用PauseUI.Pause。Resume即时后置同时要求Playing，正常准备倒计时不再被当作已恢复。

F-003（静态与离线已验证，实机待验，E-001/E-004）：新模块只在原Retry正常返回、根住的原info GameObject发生activeSelf真到假边沿、session/run/scene/revision仍匹配时排队取消。上下文在原异常、在途停用、观察或清理失败时均独立释放。取消在下一宿主Update的Pump和采样之前生效，让源命令先写入原局审计，也让旧scope待办先取消。scope不再被旧局结束或重复legacy开局误杀。

F-004（静态与离线已验证，实机待验，E-001/E-005）：两个严格材质profile支持有证据的Color/Float。验证文件hash后typed-load确切资源、比较实际Shader对象，再仅在克隆上写值并读回；未知schema拒绝。它不等于已完成音符、轨道、相机或后处理目标。

F-005（E-002）：旧探针28条审计快照中26条在2048字符处截断。新探针改为紧凑、明确截断标志的有界摘要，并加入实际音频IsActive读取；这项日志改进仍须新会话验证。旧片段不能充当完整审计证据。

## Path 与复核

P-001：原生自有按钮激活 → SDK PCM加载/播放 → EffectActivated → Stop/EffectReleased；E-002支持实际调用成功，不证明扬声器可闻或延迟。一次原始菜单导航调用后观察到LevelList。

P-002：原暂停前置与两段调用链（E-003）→新生产实现→Gameplay30回归（E-001）。尚需在实机多帧确认时间和判定停止、恢复倒计时完成及音乐状态。

P-003：Retry原入口（E-004）→ retained native context → 成功失活边沿 → 普通token队列 → 原局审计先记录 → Pump前RunEnded/Aborted → 匹配单局scope失效。Hooks24、Scope30、Game54、审计16项提供离线证据（E-001），尚无该新模块的真实挂接执行证据。

可在项目根目录复核冻结按钮证据；完整构建会更新本地候选产物：

```powershell
& C:/Python314/python.exe docs/validation/runtime-unified-20261020/inspect-native-button.py
& ./scripts/Build.ps1
```

## 后续验收与当前限制

电脑操作工具的`list_windows`连续超时，重置JS会话后重试仍失败，本批没有继续发出游戏UI输入。旧游戏仍运行时正式安装器拒绝安装，故E-006明确标记候选未部署。用户此前已授权恢复实测，无需重复请求操作许可。

下一轮先退出旧游戏、恢复电脑操作连接，再正式安装并冻结新清单/新session，验证Pause/Resume准备过程、Retry的Aborted与scope先行清理、紧凑审计及材质参数。Back主动退出、正常完成、同谱迟到结果原生origin、完整本地曲包/玩法视觉目标、四种性能基线和20轮稳定性等仍属于完整任务，不因本批回归通过而删减或勾选。
