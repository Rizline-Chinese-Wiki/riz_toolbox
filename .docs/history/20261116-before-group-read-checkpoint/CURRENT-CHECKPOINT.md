# 当前续接点（2026-11-16，优先读本页）

## 当前候选：source-read-r1 已安装，实际新会话初始化中

`artifacts/delivery-20261116/RizToolbox-0.3.0-source-read-preview-20261116.zip`，37,304,655 B，SHA `05395217e74180632a09d1b8876a7cfb90251f5f8626fe712a2458c720458805`，35 ZIP 成员读回、25 运行文件安装匹配。首次安装未完成且无可恢复终态，不计成功；重试原 exec15175 自然0/8b2909，见 `install-source-read-r2-command.json` 和 `install-source-read-observation.json`。helper 沿用 native-coroutine，SHA `d099ae13726fd0ac94abf906506a99190de2260da704e6f91bea27b2e5164823`。

15:07:56 经既有官方 Steam 2272590 请求启动，新 PID42604，15:07:58 启动。`source-read-r1-start-a-observation.json` 已核25文件，07:09:32 UTC 最新 source prepare AudioPlayDelayed。**尚未初始化完成、未验本新包来源接管或 Speed/Seek。** 不借 clock-read 旧游戏接受新源码。

## 本批源码与新验证

- Runtime 三文件：`NativeFirstInstall.PlaybackMembership.cs`、`ExactHookBroker.Playback.Group.cs`、`NativePlaybackExecutableSeal.cs`。只在同锁下的一次 Group.VerifyCurrent 中使用显式私有 VerificationScope：入口/退出各执行完整 foundation 原验证；中间的 ImageRanges/Compose/VerifyMembers 共享该次已验证 retained ranges。绑定同实际 issuer/group/thread/token/epoch/reference generation，嵌套独立，域外仍 full fresh，不跨调用缓存。完整 canonical BeforeBytes 与整节逐字节比较保留。
- Adapter 三文件：`NativePlaybackFreshSourceCapture.Adoption.cs`、`NativePlaybackActualSource.cs`、`NativePlaybackStockRootedSource.Fresh.cs`。相邻纯 Binding/Origin 两组投影改成 ReadConstructionIdentity，每组一次完整 fresh 检查；元数据调用后的 BorrowRoots、七根、真正 getter/native 操作和生命周期前后检查保持独立。tuple 是数据，不授控制权。
- `source-read-r1-result.json` 十一步全0（原 exec89447 自然0/6efcee）：fresh owned fixture、Loader六组件/Gameplay/Examples/Hooks Release、Hooks210/210、clock14、host3、Gameplay201/201、FreshAdoptionMechanismChecks 构建与31项既有机制。**31项没有执行正式 Adoption，不能说新构造投影已正向验收。**
- `source-read-owned-r1/result.json` 九步全0（原 exec38318 自然0/042a58）：新MASM、新managed harness、normal/branch tamper/owner tamper/foreign preimage、真实自有 NativeJudge/Coroutine/Coroutine body-tail。打包核当前 Runtime/Core/SDK/helper 与这些实际执行字节一致。其 scope 仅自有机制，不实例化实际 stock foundation group，也不执行 Unity/GameAssembly。
- `pack_proof.py source-read-r1 source-read` 自然0/1bfdac。未改 helper，不重复原生长矩阵。此版尚无正式 group 新作用域正向 fixture，游戏实测继续补此边界。

## 上版实际结果与修复依据

clock-read PID41872/session `ada991c3617d4136a9b20efe5677aecb` 实际进入 GreatnessEarth Axya EASY4 FREE。首次 Level GAME CLOCK 从 monotonic219.4279244 到269.65041，50.2224856秒；前版 native-coroutine 为294.3280863秒，一条实际样本减少82.93656%，**不是延迟分布、完整游戏或 Speed/Seek 通过**。GameTime Chart/Real Seconds 语义已识别。完整日志 JSON 被原logger截断时只提取完整标量，不把截断文本当完整JSON。

06:40:56 UTC RunStarted，06:41:16 Host CapturingSource 后仍约9分钟未返回。只读栈 `clock-read-r1-source-a-stack.log` 指向 ActualSource 构造中 Adoption.Binding/AccessorBinding→FreshCapture.VerifyGraph→group/foundation/targets 重复完整验证。无新 composition 警告或 firstDiff。CUA Alt+F4 正常退出请求后仍无响应，14:50:08 严核实际 PID/路径/StartTime 后仅结束41872；`clock-read-r1-exit.json` 记录，**不是正常退出或 RunEnded**。CUA已观察窗口消失。两版时间对照和原日志哈希见 `clock-read-comparison.json`。

## 下一离线工作与实际续接

1. 等本 source-read 新会话完成初始化。`python -B work/game-seek-20261115/observe_startup.py source-read <unique-tag>` 核25文件并归档，仅消费本候选真实会话。
2. CUA列实际新窗口，进入菜单→GreatnessEarth/Axya/EASY4/FREE，核首个 Clock 耗时及 Host CapturingSource 是否返回，保存 run/session 和实际日志。若再次卡住，只取一份当前只读线程栈定位具体消费者，不重跑旧长矩阵。
3. 若实际返回，继续正式七根/完整 Seek 与 HoldEpoch、DSP/音频时间精度、Speed/Seek 正向和其余B项。若出现新未知改写，只按真正永久 owner/原跳板/配置回读修接线，不能对白名单猜测或 live rebaseline。
4. 当前源码已冻结；独立 Sol 正只读审查作用域与 source 投影，另一 Sol 仅更新本批报告/交付说明。构建无在途。不重做相机X/N1/退休批/rate/float/Play移交，不以旧fixture或旧包结果签新源码。

## 范围、约束与证据位置

完整SDK/B01–B19/production engine仍未完成。用户已允许必要游戏操作和恢复实测；无新停止信号，不为旧Escape重复询问。功能优先、不做旧兼容；主线程数十秒/分钟阻塞是当前可用性故障，非后置基准工作。代理统一Sol/max；构建串行BelowNormal，避免占满电脑；不操作账号/认证，不commit/push。主README保持SHA `CFBAC4691D36EDC5DABA513AA62D5856AE61E29BF553D0DC2979832750847364`。

本批原件 `work/game-seek-20261115/`；报告 `docs/2026-11-16_late-hook-composition-report.md`；交付说明 `artifacts/delivery-20261116/交付说明.md`。本页更新前原件逐字存于 `.docs/history/20261116-before-source-read-checkpoint/`，更早记录见其中链接。旧ZIP不改。