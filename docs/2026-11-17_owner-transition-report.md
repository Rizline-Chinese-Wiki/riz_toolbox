# 相机归属、失败切换等待与映像缓冲修复

当前预览已完成新源码构建、**832 项检查与 4 个受控宿主场景通过**，于 2026-11-17T19:59:32.4576019+08:00 安装25运行文件匹配。新游戏 PID3844 已从 Steam 正式入口启动，本会话已结束；position-X与scale-X真实绑定成功，尚未override验证。source61.075015秒，SDK Pause Error19；独立Paused因果未证明，307.269065秒后才PreparingClock，无Ready/Speed/Seek或B01–B19/生产准入。`flavor=null`。见 [scope.md](../work/game-seek-20261117/owner-transition-r2/scope.md) 与 [timeline.md](../work/game-seek-20261117/owner-transition-r2/timeline.md)。

## 交付与复核

[预览 ZIP](../artifacts/delivery-20261117/RizToolbox-0.3.0-owner-transition-preview-20261117.zip)，37,373,255 字节，40 成员/25运行文件已完整读回，SHA-256 `5c62946509b5f47fe2bcfd0c1fa8625f7e720c16f00683742a525db5c256ab14`。

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath 'artifacts/delivery-20261117/RizToolbox-0.3.0-owner-transition-preview-20261117.zip'
Get-Content -Raw -LiteralPath 'work/game-seek-20261117/owner-transition-r2/source-freeze.json' | ConvertFrom-Json
Get-Content -Raw -LiteralPath 'work/game-seek-20261117/owner-transition-r2/directed-checks-r1.json' | ConvertFrom-Json
```

准确构建/运行命令、子进程、绑定输入与退出码见回执。脚本默认不覆盖旧结果；复现实验应使用新输出目录。源码与产品对应关系由freeze和staged binding约束，未使用旧包结果验收新源码。

## Evidence

| ID | 来源 / SHA-256 | 直接事实 |
| --- | --- | --- |
| E01 | [game-result.json](../work/game-seek-20261117/helper-image-r2/game-r1/game-result.json) · `fe23b4dd2232f095d3faecc045d15835323d6e942153e43e00a9218e73fa3353`；[旧包实际Retry栈](../work/game-seek-20261115/helper-r2-retry-r1-stack.log) | 旧helper-r2 source65.2372419秒；相机首败camera-outside-rig-transform；Retry报无armed predecessor但full Group反复进入，Pause Error19/原生0调用。只定位问题。 |
| E02 | [camera-static-evidence.json](validation/game-camera-scalar-20261030/camera-static-evidence.json) · `6563099a05be6dfc3236a31de30c8271ff1886b33f02dd6cdf8409587e945197` | 保存的完整方法证据：CameraControl SetXPosition/SetScale取自身Transform；NoteJudge.Start独立缓存Camera.main，没有证明父子层级。 |
| E03 | [source-freeze.json](../work/game-seek-20261117/owner-transition-r2/source-freeze.json) · `b6eb5f03802e86218e1a043542c2b0ca38067c695ca2e03a3f2030d74c519652` | 1512输入，相对helper-r2六文件变化；native/SDK/Examples不变。 |
| E04 | [managed-build-result.json](../work/game-seek-20261117/owner-transition-r2/managed-build-result.json) · `e1d9b271b725fb2bafc5180578ffdb9fc43b923839d965ee9594dc3282c0ce06`；[hooks-checks.log](../work/game-seek-20261117/owner-transition-r2/hooks-checks.log)；[camera-checks.log](../work/game-seek-20261117/owner-transition-r2/camera-checks.log)；[gameplay-checks.log](../work/game-seek-20261117/owner-transition-r2/gameplay-checks.log) | 七步自然0；Hook210、Camera63、formal-host3。r1误用缓存IsActive的测试已仅修断言并新运行，原件见 [camera-test-correction.json](../work/game-seek-20261117/owner-transition-r1/camera-test-correction.json)。 |
| E05 | [result.json](../work/game-seek-20261117/owner-transition-r2/checks-runner/checks-r1/result.json) · `18c3a65a4e36ac7111f7fb6fae25c0adf64bb6cc6fbac0ea3360b35a4ae6e117` | 新产品绑定 owned播放四模式338，23个actual Prepare/Commit、3pair；不签真实Stock foundation或Clock正向。 |
| E06 | [directed-checks-r1.json](../work/game-seek-20261117/owner-transition-r2/directed-checks-r1.json) · `00ca3dadb8335a749e4398255662f373e73fc825ca4a31f0f77087adff3e23eb` | 有界比较9、expected缓冲5、实际helper组67、负向transition dispatch137，共218。全部自然0，产品与当前DLL匹配。 |
| E07 | [host-checks-r1.json](../work/game-seek-20261117/owner-transition-r2/host-checks-r1.json) · `57b204eeb7198483ccb1d61db5effdd09f2cbb54aff013217abdb974281e9ae4` | 新链接实际Loader受控宿主4场景通过；不是旧链接结果继承。 |
| E08 | [owner-transition-preview-receipt.json](../artifacts/delivery-20261117/owner-transition-preview-receipt.json) · `0f73893994d6196c2308cfcb06c71787b3f1fa99ba54ee10cf14bf240672474d`；[install-observation.json](../work/game-seek-20261117/owner-transition-r2/install-observation.json) · `480d4dddc580cff95d11eb4f8b23eb63937775fee751c06bf2e1156f9cf9753e` | 新ZIP完整读回；25文件实际安装匹配。安装器没有请求启动。 |
| E09 | [initial-observation.json](../work/game-seek-20261117/owner-transition-r2/game-r1/initial-observation.json) · `7b5daebd2a9697fd9e7f4a2d5b372ae1a0e5c6420055a4bd8a486780cbe405b7` | 本包新PID3844于2026-11-17T20:01:03.2832855+08:00正式启动；仅进程观察，无完整游戏结论。 |

本批新总数 **210+63+3+338+9+5+67+137=832**，另4Host。旧687、736、788、原生442及Examples27/发送端7不计入本批新检查。137负向dispatch用真实物理组、真实observer association/Router构造，但test-only设置已拒绝状态、惰性pending marker和调度计数；没有模拟成功native票。final false回调到计数的实际游戏路径仍须实测。

## Finding

| ID | 严重度 / 置信度 / 状态 | 结论与边界 |
| --- | --- | --- |
| F01 | `n/a_re` / 高 / 已实现及离线通过，实机待测 | 相机原层级关系未经游戏方法证明。改以同一当前LevelInformation的controller→rig与controller→judge→cached Camera双支精确引用归属；保留唯一controller、run/generation、各对象class/active/loaded、backrefs/flags、Transform身份重读和setter读回（E01–E04）。测试继承已有test-only backend装配，不证明shipping PE准入或持续显示。 |
| F02 | `n/a_re` / 高 / 已实现及定向通过 | Host只对准确的“无armed predecessor”Unsupported建立负向调度hint，并要求没有可推进pending初始化/transfer/session。每轮仍处理故障和回执释放；同association与route状态未变才避开full Group。新run、pending状态、final MoveNext false计数或故障会重新全验证（E01/E03–E06）。计数是调度信号，不是native完成票；无效final false若高频仍可重新触发。 |
| F03 | `n/a_re` / 高 / 已实现及定向通过 | 每个seal维护精确长度独占scratch，Rent从immutable canonical全量复制；嵌套/并发不共享active数组；完成全组合比较才清零归还。异常不修改旧array也不回池，避免CompositionSection异常别名。只复用内存，不复用成功证据；会保留每长度最多一个idle数组（E03/E05/E06）。 |
| F04 | `n/a_re` / 高 / 未验证 | 实机响应、相机真实发现、ClockReady、完整Speed/Seek与B01–B19仍待本新会话。离线绿灯和旧source65秒不等于新游戏可交付（E08/E09）。 |

## Path

**P01（E01/E02/E03/E04 → F01）：** 真实相机首败 → 对照固定方法receiver来源 → controller当前Info双支身份重读 → 两标量独立注册/读写。未移除live run/引用/启用/字段/线程保护；仅删除没有证据的层级条件。实现 [Camera backend](../src/RizToolbox.Adapter/GameCamera/NativeGameCameraBackend.cs)。

**P02（E01/E03/E05/E06 → F02）：** 原TryObservedInitialization精确拒绝 → 捕获同observed association和无pending状态 → Host后续先PumpCompleted/PumpFaults → 无变化保留Unsupported等待 → 新实际调度状态恢复原完整Group/profile/准入。测试连续12次同失败状态避免进入故意缺省的完整profile，并验证新association/计数变化恢复全路径而不能获得Clock。实现 [StockRateHost](../src/RizToolbox.Adapter/Gameplay/NativePlaybackStockRateHost.cs)、[Router.Successor](../src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.Successor.cs)、[Router.ObservedInitialization](../src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.ObservedInitialization.cs)。

**P03（E03/E05/E06 → F03）：** exact-length独占租借 → 全canonical复制 → 原member composition及全部actual字节比较 → 成功clear归还，异常discard。原所有独立getter/Read/original/callback边界不变。实现 [ExecutableSeal](../src/RizToolbox.Runtime/Hooks/NativePlaybackExecutableSeal.cs)。

下一仅继续本新PID，核新session与25文件，实际开局查看相机绑定和source响应，再通过后台公开SDK等待原生Pause readiness后唯一Pause→独立Paused→Resume/ClockReady/SpeedSeek。不使用旧游戏或旧栈验新包。

## 本包真实游戏闭合

**E10 → F05 → P04：** [game-result.json](../work/game-seek-20261117/owner-transition-r2/game-r1/game-result.json)与其中列出的新会话完整日志/安装匹配：相机两个目标真实绑定；source61.075015秒，Pause-r2单请求Error19；Pause-r3没有fresh status未发。12:08:10Z原生生命周期Paused来源未知，不归因SDK；12:13:17Z PreparingClock，无Ready，无Resume/Speed/Seek。

**E11 → F06 → P05：** [12秒trace](../work/game-seek-20261117/owner-transition-r2/game-r1/paused-r1.nettrace)、[托管caller归属](../work/game-seek-20261117/owner-transition-r2/game-r1/paused-r1-profile-r2.json)、[实际线程栈](../work/game-seek-20261115/owner-transition-paused-r1-stack.log)确认调用仍集中在FreshCapture/Accessor/Rooted/Receipt的Group完整验证。主线程MemoryFact.Verify20.97%、VerifyReadable19.14%、ReadRanges17.21%、TweenSourceMemory11.26%，Memmove3.30%；这些是采样线程时间归属，非精确CPU或调用次数。[微软采样工具说明](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)。下一五file改成固定只读验证bracket与同借用graph去重；此实机证据不验之后新源码。

**E12 → F07 → P06：** [game-exit.json](../work/game-seek-20261117/owner-transition-r2/game-r1/game-exit.json)记录官方sky正常退出请求超时，之后至少141.9065907秒仍不响应，按PID/路径/Start三核结束。20:17:11已确认无进程、sky窗口消失。原生Paused到PreparingClock超过5分钟仍属功能阻塞，不能把本预览当生产交付。
