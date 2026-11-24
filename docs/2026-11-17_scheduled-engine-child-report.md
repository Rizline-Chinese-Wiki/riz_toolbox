# 调度内部音调子调用修复

本批实现了此前漏掉的播放路径：Unity 在 `PlayScheduled` 内部把音源当前音调重新提交给 native Engine。旧规则只接受 `LowerSetPitch` 内部的 Engine 子调用，实际游戏因此在恢复时失去来源。新规则限定到固定调用点、同一私有音源关联和实际原调用窗口，保留原始 float 位值及每个调用的退出责任。

新构建、442 项调度原生检查和 338 项既有播放链回归已通过。新预览包已安装，25 个运行文件均匹配；实际游戏已观察到恢复播放，旧来源拒绝未复现，但主线程仍有分钟级阻塞，完整 Speed/Seek 尚未通过。测试游戏已结束。

## 范围

[授权与边界](../work/game-seek-20261117/scheduled-engine-child/scope.md)。这是本地游戏兼容层功能实现，不涉及账号或外部服务。普通逆向报告，flavor=null；无恶意样本、攻击或漏洞结论。

SDK 公共合同和主 README 未改；没有把自有机制测试提升成 stock foundation、正式 Adapter adoption、Clock 或生产准入证明。

## 证据

| ID | 原件 | 观察与边界 |
|---|---|---|
| E01 | [V2 实际游戏结果](../work/game-seek-20261117/engine-child-diagnostic/engine-child-r1-game-result.json) | 旧 V2 当前 EngineSetPitch 的父为 PlayScheduled/Double1；tested=0x7f、matched=0x3f，首败 ParentLowerSlotMatches。只定位旧候选，不验新行为。 |
| E02 | [固定静态链](../work/game-seek-20261117/scheduled-engine-child/closed-profile.json) | 固定双映像及完整 body/RF/UW、注册和 CALL 链；静态可达性，不等于实际 caller 观测。 |
| E03 | [源码冻结](../work/game-seek-20261117/scheduled-engine-child/source-freeze.json) | 1504 项输入。相对 V2 8 个已有文件变化，另有 3 个新增功能文件；本次扩大原生依赖采集，共新增采集 232 项，不等于修改 240 个产品文件。 |
| E04 | [构建原件](../work/game-seek-20261117/scheduled-engine-child/native-build-result.json) | 新两个 native helper 及串行托管控制自然退出 0；托管 11 步均 0，含 Hooks210、Gameplay201、clock14、host3、fresh 机制31。 |
| E05 | [调度原生结果](../work/game-seek-20261117/scheduled-engine-child/native-checks/run-01/result.json) | 8 个独立进程，442 项检查。1.0/1.25 原样保持；回调重入、直接 Engine、错误 caller/arg/cache、worker 拒绝；实际原调用仍执行。仅 native 自有机制。 |
| E06 | [当前字节播放链回归](../work/game-seek-20261117/scheduled-engine-child/checks-runner/checks-r1/result.json) | 新 DLL 和 helper 上 338=Replay113+Resume133+direct-time46+direct-engine46；每进程真实 23 成员、3 pair、原 factory claim。旧 fixture 无本次 stock schedule profile。 |
| E07 | [新包结果](../artifacts/delivery-20261117/scheduled-engine-child-preview-receipt.json) | 35 成员、25 运行文件，全 ZIP 逐字节读回；绑定 E03–E06。 |
| E08 | [实际安装](../work/game-seek-20261117/scheduled-engine-child/install-observation.json) | 11:27:10 本地安装 25 文件匹配；新游戏进程 35548，11:27:12 启动。 |
| E09 | [本包实际游戏结果](../work/game-seek-20261117/scheduled-engine-child/scheduled-r1-game-result.json) | 同 run/generation 恢复到 Playing=true/Paused=false；Resume 按钮至实际播放约 250.94 秒。无来源拒绝日志，未 Ready。正常退出工具超时，等待后精确结束该测试进程。 |
| E10 | [实际主线程调用栈](../work/game-seek-20261115/scheduled-engine-child-r1-after-resume-a-stack.log) | 卡在普通 broker 成员的整组映像校验；嵌套 fresh accessor 反复进入该路径。只读采集，支持可用性问题定位。 |

关键固定字节：

- GameAssembly：`853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。
- UnityPlayer：`43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`。
- 新 Source helper：`26524c62e30ea516344e62d469c91f4d62317aa70c5448b804dd32693140b1f5`，219648B。
- 新 SingleReturn helper：`b6aa468e539d060628699c653c89c977efca6882fae88c43f3dbd73cf6874d5e`，80384B。
- 新 ZIP：`b99935c55dbb44642b771604e00f7f69b1be26b1f06386fba0a769376abc2d67`，37315127B。

各原件包含输入、输出和结果文件的 SHA-256；新包另绑定源码冻结、构建及两组实际检查结果的 SHA-256。

## 发现与实现

F01，旧来源拒绝的具体原因已定位，置信度高。依据 E01 和 E02，问题是固定调度路径的合法 Engine 子调用不在 Lower 父规则内，不能靠修改 float 参数、清失效标记或重发票解决。

F02，新行为在 native 自有机制中通过，置信度高，依据 E03–E06。`EntryDouble1` 在 Before 回调结束后，仅围实际 Original 打开独立 mode 2 窗口，捕获同一 retained wrapper/native 关联、当前 `+0x1F4` pitch bits 和三个 fence。精确子调用须来自 `BB6F4E`、缓存已解析到固定 native Play、owner/thread/epoch/TLS/父槽/深度及关联均相同。Before 回调发生写入、重复消费或错误来源均不能借用窗口。

F03，Managed 接线保留物理责任，依据 E03、E05、E06。成功子调用沿既有 flag1024、Before/return/physical-exit journal，被挂到真实 PlayScheduled 父；不进入语义叶列表、不增加音调 writer。PlayScheduled 的 double DSP 参数不与 float pitch 比较。合法零子路径不授予子完成票，已发生的子调用必须正常返回并物理退出。

F04，本包实际恢复播放已观察，但完整功能仍未通过，依据 E07–E10。接管耗时 142.68 秒；SDK Resume 调度返回耗时 32.15 秒，按钮至实际恢复播放耗时 250.94 秒。无 nativeStatus 拒绝或 support stopped 日志。合法零子路径仍存在，且没有独立记录实际 exact Engine child，因此不能仅凭无拒绝或恢复播放宣称该 native 子路径实机已证明。两次暂停输入均超时，无 SDK Pause 按钮/回执；随后采样到 Paused 只证明状态，不能证明由 SDK Pause 引起。

F05，分钟级主线程阻塞仍影响功能，依据 E09–E10。`ActualSource.VerifyGraph → ReadActive → Adoption.EnterAccessor → FreshCapture.VerifyCurrent → VerifyGraph → Group.VerifyReceivers → ReferenceImageMembers.CaptureSnapshot` 重复整组校验；普通成员还各自重复读取同一 Dobby 映像。下一修复仅合并同一锁定遍历中的重复校验，保留独立原生读取边界。正常 Alt+F4 工具超时，观察后等待 145.77 秒仍无响应，已按 PID、路径和启动时间精确结束 35548 释放资源；不记为正常退出通过。

## 调用路径

P01，由 E01/E02 定位 F01，由 E03–E06 实现并验证 F02/F03：

`Game PlayScheduled@15116D0 → cache1FB0660 → Unity Play@1D7AE0 → BB74C0 → BB6F20 → load native+1F4 → CALL BB6F49 → Engine@BBAD20 → exact return BB6F4E → 原退出与物理 journal → 同一来源票继续`。

新 profile 只验证固定链的支持证据，不安装额外 hook、不发行票或写入权限。原 wrapper 与 Engine 的可变入口仍归现有 broker/pair owner；profile 只封口 wrapper 的未改尾部和其 canonical 文件指纹，不把 live detour 当成原始字节。

## 复现

离线运行使用本地 MSVC 14.29 和 Windows SDK 10.0.19041；不得把下面命令指向其他 helper 后借用本结果。

```powershell
$case = 'C:/Users/71957/Desktop/riz_toolbox/work/game-seek-20261117/scheduled-engine-child'
& "$case/native-checks/Run-ScheduledEngineNative.ps1" `
  -HelperPath "$case/native-r1/RizToolbox.PlaybackSourcePrototype.dll" `
  -HelperSha256 '26524c62e30ea516344e62d469c91f4d62317aa70c5448b804dd32693140b1f5' `
  -OutputDirectory "$case/native-checks/run-reproduce" `
  -VcVars 'D:/riz-build-cache-next/delivery-native-20261107-r1/vcvars64-sdk19041.cmd'
```

输出目录必须不存在，以保留各次原始失败或成功。全部编译命令、输入与编译器信息写入结果目录。338 回归的绑定、构建与调用命令见 `checks-controller-r1.json`；原 native/managed 构建命令见 E04 及其引用的托管结果。

剩余工作先修复上述可用性问题，再验证 SDK Pause、真正 Clock Ready 和 Speed/Seek、七根与实际 DSP。内部 child 仍需独立实际观测；B01–B19 整体未完成。
