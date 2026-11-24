# 音源校验去重、helper 出口修复与相机诊断

当前预览已构建、**687 项检查与 4 个受控宿主场景通过**，19:05 安装25匹配，并完成同新包两局实机观察。真实 source 接管仍65.2372419秒；相机稳定首败为不成立的层级条件；第二局还复现同一无效切换反复校验导致倒计时拖慢。本包实机仍不能验收 ClockReady、Speed/Seek、B01–B19 或生产准入。当前游戏已正常退出，三项修复候选尚未合入此包。`flavor=null`。范围与初始时间线在对应case内保留，以下为截至打包的离线证据，并在末尾追加本次真实结果。

## 交付与复现

[预览 ZIP](../artifacts/delivery-20261117/RizToolbox-0.3.0-helper-image-preview-20261117.zip)，37,371,116 字节，40 成员/25 运行文件；完整 ZIP 读回通过，SHA-256 `2977df032836427ea380c8eff5532d2ed973b418eb36ebdfbda9a478760bc2a5`。不改公开 SDK、原生组件或 Examples 字节。项目主 README 未改。

在项目根目录可直接复核：

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath 'artifacts/delivery-20261117/RizToolbox-0.3.0-helper-image-preview-20261117.zip'
Get-Content -Raw -LiteralPath 'work/game-seek-20261117/helper-image-r2/source-freeze.json' | ConvertFrom-Json
Get-Content -Raw -LiteralPath 'work/game-seek-20261117/helper-image-r2/directed-checks-r1.json' | ConvertFrom-Json
Get-Content -Raw -LiteralPath 'work/game-seek-20261117/helper-image-r2/install-observation.json' | ConvertFrom-Json
```

检查的准确命令、绑定路径、子进程和退出码均存于下列 JSON。构建与运行脚本默认保护旧结果，不应原路径重复执行；复现实验应使用新输出目录并保持对应 source freeze。未安装或调用新的外部分析工具。

## Evidence

| ID | 来源与 SHA-256 | 直接事实 |
| --- | --- | --- |
| E01 | [source-freeze.json](../work/game-seek-20261117/helper-image-r2/source-freeze.json) · `27500ccdb238e42a9df5b5884754d8c4929052e2e4f234901dbaafd6551ee882` | 1512 输入；相对 persistent-reference 六文件变化，相对 helper-image-r1 两文件变化。原生、公开 SDK 未改。 |
| E02 | [helper-image-exit-r2.log](../work/game-seek-20261117/helper-image-r1/helper-image-exit-r2.log) · `18161c977cf92a8f8bc9c4685ba7a6d7c862d2204916ce73e02249e0d8fc5646`；[helper-exit-fix.json](../work/game-seek-20261117/helper-image-r1/helper-exit-fix.json) · `a2dd878a3386ac086c94135bc653b6f27cd017c55b749714f664c29f13ec04dc` | r1 的实际 22-source/23-member 组在受控 ObjectClass 元数据投影期间修改 helper，退出未拒绝，group 仍 active。原失败与修复前源码保留。 |
| E03 | [managed-build-result.json](../work/game-seek-20261117/helper-image-r2/managed-build-result.json) · `a24cbc0aa48bfdad7c2eef5b0deb66fb026b100cd54a44f47c1050ac0183f3e2`；[hooks-checks.log](../work/game-seek-20261117/helper-image-r2/hooks-checks.log)；[camera-checks.log](../work/game-seek-20261117/helper-image-r2/camera-checks.log) | Loader 与受影响测试构建共五步骤自然退出 0；Hook 210、相机 63。 |
| E04 | [result.json](../work/game-seek-20261117/helper-image-r2/checks-runner/checks-r1/result.json) · `2ecd1da2619e04b701b3fbbb631bf1f8a84a5268baf791e496ea077f4a7a5b94` | 当前新 DLL 绑定 owned 播放链四模式 338 项通过。23 次实际 Prepare/Commit，含三个 native pair；无真实游戏、Stock foundation 或 Clock 正向。 |
| E05 | [directed-checks-r1.json](../work/game-seek-20261117/helper-image-r2/directed-checks-r1.json) · `945b5512b9513fe64f36998d04387910d62ee629067f41fa04c6d2c2a2438a6b`；[helper-image-exit.log](../work/game-seek-20261117/helper-image-r2/helper-image-exit.log) | 新 Runtime 下 9 个有界读取案例与实际 helper 组 67 项，共 76 通过；正常、helper 入口篡改、受控元数据投影后篡改三进程均 0。篡改被拒绝，恢复仍 sticky；无 root 泄漏或 Clock 读取。 |
| E06 | [host-checks-r1.json](../work/game-seek-20261117/helper-image-r2/host-checks-r1.json) · `fa8b2c33e0a20707174081d1bc212c141cea61398a59834fcd38b8821989ba3b` | 新链接 Loader 的四个受控实际宿主场景通过；所有产品绑定已与当前 DLL 核对。 |
| E07 | [helper-image-preview-receipt.json](../artifacts/delivery-20261117/helper-image-preview-receipt.json) · `3821a24fcec660f06aef8472a23dd92b204700c0abfbe9ece15698458faec339`；[install-observation.json](../work/game-seek-20261117/helper-image-r2/install-observation.json) · `fc461ac3cb2f7d7e2f8cb8c13523031576ffa714faa1f946f28619bd4f354b16` | 包完整读回，25 运行文件实际安装匹配；安装器没有请求 Steam/游戏启动。 |
| E08 | [上一个包的实际游戏结果](../work/game-seek-20261117/persistent-reference-r1/game-r1/game-result-reconciled.json) | 旧包音源接管 70.221616 秒，Pause sender 至回执 76.514717 秒，未 Ready。仅为故障基线，不验证本批新源码。 |

新检查总数为 **210 + 63 + 338 + 9 + 67 = 687**，另有四个宿主场景。以前的 736/788、原生 442 和 Examples 27/发送端 7 不计入本批新检查。r1 夹具曾有路径预检和旧 mode 白名单错误，修复后才产生 E02 的实际产品失败；这些原件均保留，本批通过来自 r2 新产品。

## Finding

| ID | 严重度 / 置信度 / 状态 | 结论与边界 |
| --- | --- | --- |
| F01 | `n/a_re` / 高 / 代码及离线验证通过，实机待测 | 同一个 helper seal 在尾部 22 成员校验中至少扫描 66 次。现在保留逐成员注册/slot/epoch/entry/original/info/owner 核对，合为两段纯查询批量完整进出，共四次；traversal 另有独立完整 helper 入口/最终出口。组合映像仍单独检查当前节成员，尾部全查。扫描次数来自源码静态计数，不能当耗时测量（E01/E03–E05）。 |
| F02 | `n/a_re` / 高 / 定向通过 | 完整 image 和 RangeSeal 比较用 4096 字节加 15 字节诊断预读的固定栈缓冲，消除逐次整段 actual[]；保留完整字节比较、页/分配身份、首差 RVA/offset 和最多 16 字节实际窗口。9 例覆盖空/单字节、4095/4096/4097、尾部与跨两个可读 region、保护改变拒绝。构造期 Capture 的完整快照保持；不保证瞬态恢复 ABA（E01/E03/E05）。 |
| F03 | `n/a_re` / 高 / 已修复并复验 | E02 证明 helper 最终出口缺失。r2 在真实 traversal 入口保留并验证原 helper seal，Dispose 即使其他出口失败也尝试该原 seal，并核 hub 身份；投影后篡改现被拒绝，恢复后 group 和 GetTime lease 仍失效（E01/E02/E05）。 |
| F04 | `n/a_re` / 高 / 诊断已实现，实际修复待定位 | 相机通过原 Discover 入口报告首个失败谓词；新增三组用例覆盖 ancestry、Behaviour disabled、捕获后 flag 改变，公开错误行为不变。测试继承已有 test-only backend 装配，不能证明 shipping loaded-PE gate 或真实相机已可用（E01/E03）。 |
| F05 | `n/a_re` / 高 / 尚未验证 | 包已安装，但没有本包新游戏 run。旧包外观发现恢复、Pause 返回和分钟级阻塞均不能签署本包实际效果；ClockReady/Speed/Seek 与生产验收仍开放（E07/E08）。 |

## Path

**P01（E01/E03–E05 → F01/F03）：** 加锁创建 actual traversal → 保留 helper 完整入口 → 当前映像每节只组合该节真实成员 → 每成员 body/metadata 校验 → helper entry reservation 批量进出 → 各成员 proof、PumpCompleted 和 relay 独立执行 → 新 helper entry/info/owner 批量进出 → targets/Dobby/helper/foundation 最终出口。helper 两批之间没有可复用的成功票；每个真实 getter、Read、original 或 callback 的独立调用仍重新取得证据。完整异常路径保持 sticky。实现见 [Group](../src/RizToolbox.Runtime/Hooks/ExactHookBroker.Playback.Group.cs)、[SourceOwner](../src/RizToolbox.Runtime/Hooks/ExactHookBroker.Playback.SourceOwner.cs)、[Hub.MemberEvidence](../src/RizToolbox.Runtime/Hooks/NativePlaybackStockSourceHub.MemberEvidence.cs)。

**P02（E01/E03/E05 → F02）：** 现有范围页身份检查 → 逐个有界块复制到局部 stack snapshot → 对 expected 相同长度完整比较 → 首差窗口从同一块快照取得。每个新范围和每次独立调用都读取实际内存；不缓存实际成功。实现见 [ExecutableSeal](../src/RizToolbox.Runtime/Hooks/NativePlaybackExecutableSeal.cs)。

**P03（E01/E03 → F04）：** 原相机 CaptureRun/Current 条件和短路顺序保持 → false 时附具体首败码 → 服务继续按原错误拒绝写入。下一通过本包新游戏日志定位实际失败条件。实现见 [NativeGameCameraBackend](../src/RizToolbox.Adapter/GameCamera/NativeGameCameraBackend.cs)。

下一工作是本新包真实开局，先核实际会话/25 文件与相机首败码，再确认 source 接管响应、SDK Pause/Resume、ClockReady 和 Speed/Seek。当前电脑操作接口未提供，已请求用户手动开局；未据此声称整体任务完成。记录截至 2026-11-17T19:07:55.904043+08:00。

旧包实际Pause补证：唯一回执后约0.227秒有同run lifecycle Paused=true。它属于旧persistent-reference包；本helper-r2尚无新实际会话，不能复用旧采样验本字节。


## 本包实机续证与下一修复

**E09**：[实际双run结果](../work/game-seek-20261117/helper-image-r2/game-r1/game-result.json) · `fe23b4dd2232f095d3faecc045d15835323d6e942153e43e00a9218e73fa3353`；[本会话日志](../work/game-seek-20261117/helper-image-r2/game-r1/current-session.log) · `32a01a74ea0f3c9847456ccc08969a86f05ec590c3dc0990efb8de50a9e4f234`；[正常退出记录](../work/game-seek-20261117/helper-image-r2/game-r1/game-exit.json)；[真实主线程栈](../work/game-seek-20261115/helper-r2-retry-r1-stack.log)。启动/开局时各25安装字节仍匹配。

**F06（高置信度，实际阻断）**：新PID42392/Start19:17:16.5611536的首次run a0dac737…，11:21:51.4802251Z CapturingSource→11:22:56.7174670Z WaitingForPause，65.2372419秒。第二run f5478123…/controller7，Host报TransitionUnavailable：没有可消费的armed predecessor；主线程仍经GetOrPrepareObservedSession→HasOrdinarySuccessorBoundary→Group完整校验反复进入。原生canDoOperation多次为false，SDK Pause请求nonce9546e8f2421649cf84d3717da8e33660收到Error19，未调用original Pause（native hook count为0）。之前pause-r1发送器在无run时拒绝、没有发布请求；之后pause-r3等待可操作被精确取消，也没有新请求。运输层exit0不等于SDK成功，pause-r2-arm原passed字段只是收到回执，不当功能通过。

**F07（高置信度，新功能修复依据）**：相机首败始终camera-outside-rig-transform，说明其前面唯一controller、双方Info backrefs、run/generation、对象active/loaded、flags和Transform重读已通过。旧静态完整方法证明CameraControl改自身Transform，NoteJudge.Start单独缓存Camera.main；未证明Camera为rig后代。隔离camera-ownership-next用当前同LevelInformation的双支引用关系替代无证据层级假设，仍待新源码验证与新包游戏正向。

**P04（E09→F06）**：Retry新run被Sampler识别→同transition缺少armed predecessor而明确拒绝→宿主每frame重复完整物理/映像检查→倒计时和UI被拖慢。下一修复只缓存该精确失败分支的调度状态；native faults、drain、actual initializer/handoff/pending状态或新HostAssociation变化仍处理，任何新准入继续完整核验，不复用成功票。

**P05（E09→F07）**：精确首败诊断→对照旧完整方法证据→修正错误游戏对象关系。另expected-buffer-next只池化精确长度scratch，使用前从canonical全量复制、正常完整比较后归还，异常丢弃，以保留旧CompositionSection可能仍引用的数组；候选尚未由本687验证，不据此宣称速度改善。

19:38:49已确认正常Alt+F4退出，无Stop-Process结束游戏。官方computer-use `@oai/sky`运行库已恢复控制能力，无需继续等待人工开局；旧“电脑操作接口不可用”的文字仅为当时状态。下一合三个候选、跑必要新检查后，安装新包再测；本包结果不能验未来新源码。
