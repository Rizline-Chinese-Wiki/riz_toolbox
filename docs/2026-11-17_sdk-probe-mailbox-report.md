# SDK 调试请求入口

本批为已有 `example.panel` 调试插件补上显式请求入口：使用插件自己的 Data 文件收取 Pause、Resume、SetSpeed 和 Seek 请求，再在 FrameUpdate 中调用现有公开 SDK。这样可提前安排后台请求，避免电脑工具激活游戏窗口时触发焦点自动暂停，便于区分真正 SDK Pause 和焦点暂停。入口已构建、检查、打包并安装；尚无本包的实际开局结果。

范围为用户已授权的本地兼容底层工程，普通报告，flavor=null。不改 SDK 合同、Runtime、Adapter、Loader、原生组件或主 README，不增加控制权限，不接触账号。原运行时目标组校验优化原样继承。

| ID | 原件 | 事实与边界 |
|---|---|---|
| E01 | [上一游戏结果](../work/game-seek-20261117/composition-read/composition-r1-game-result.json) | Pause 按钮之前已采样 Paused=true，按钮回执前置条件拒绝，不能证明 SDK Pause。此文件只说明验证障碍，不验新包。 |
| E02 | [r3 源码冻结](../work/game-seek-20261117/module-control-mailbox/r3/source-freeze.json) | 1512 输入；相对 target-family 冻结变化8文件，包括两处 Examples 源码、脚本/文档/测试及构建登记。Runtime/Adapter/Loader/SDK/native 未改。 |
| E03 | [r3 构建](../work/game-seek-20261117/module-control-mailbox/r3/managed-build-result.json) | Examples、SDK-only 检查项目 Release 构建和检查执行三步骤均退出0；六个宿主组件字节与 target-family 相同。 |
| E04 | [检查输出](../work/game-seek-20261117/module-control-mailbox/r3/probe-checks-r1.log)及[正式测试项目](../tests/RizToolbox.ProbeControl.Tests/Program.cs) | 27/27，链接同一生产 helper 源、使用 SDK 接口替身；不执行真实游戏、Clock、native 或来源签发。 |
| E05 | [发送端结果](../work/game-seek-20261117/module-control-mailbox/sender-checks/result.json) | 7个假目录场景退出0，绑定发送脚本、文档与夹具 SHA；包含无 DateKind 参数的旧式 PowerShell JSON 时间解析。 |
| E06 | [打包回执](../artifacts/delivery-20261117/sdk-probe-r3-preview-receipt.json) | 40成员/25运行文件全部读回，只有五份 Examples DLL 不同，其余20项逐字节继承。 |
| E07 | [安装记录](../work/game-seek-20261117/module-control-mailbox/r3/install-observation.json) | 15:30:59 本r3修正版安装25文件全部匹配；没有请求 Steam 或游戏启动。 |

F01（E01–E04）：已有 opt-in 配置为 `config/plugin-example.panel/config-runtime-module-proof.json` 的 `Enabled=true`。只有该 scope 内才创建 helper，现有每秒一次 FrameUpdate 传入已观察控制状态。固定三个逻辑名通过 `IPluginStorageService` Data 读写；无请求不执行控制。状态不是新的控制提供者，也不额外读取或制造 Clock Ready。

F02（E02–E04）：请求只接受当前 session、GUID nonce、非空 run、正 controller generation 和四种动作，字段重复/未知、大小异常、过期、错误单位或非法数值均拒绝。nonce 在 Acquire 前消费；每次激活最多32个，验证失败也计入。实际 lease 的 run/generation 必须匹配，Acquire 返回后再次核对取消及期限，再调用显式 `Execute(request, scope.Lifetime)`。lease 释放不代表回滚动作。

F03（E02–E05）：回执保留实际 SDK Result 和事务状态。写结果失败只补写原结果，不再次调用；SDK 抛异常记录效果可能未知且不重放。请求 ID 大小写归一，重新激活换 session，旧请求只写状态错误，不覆盖旧实际回执，也不能在新 scope 执行；旧请求没有回执时仍保持未知结果。发送器以单写者锁与同目录原子替换发布，拒绝覆盖未获匹配回执的请求；超时报告 Outcome unknown，不自动重发。消费者先用 List 核大小，再 Read 并核长度；4KiB 是协议接受上限，底层 SDK 读取仍受其自身文件预算约束。

P01（E02–E05支持F02–F03）：`后台显式发送 → 当前 session/run/generation 请求 → FrameUpdate → 有界解析及 nonce 消费 → 实际 Acquire → lease 身份/取消/期限核对 → 显式 Execute → 原样 Result → Data 回执`。这条路径没有桌面输入模拟、Loader 分支或 native 快捷通道。

首次 r1 的24项已通过，随后补了 Acquire 期间过期/取消保护及旧 PowerShell 时间解析，r2为26项通过。复审进一步发现旧session的拒绝回执会覆盖旧实际回执，挡住后续发送；r3将该分支改成仅状态错误，新增有旧真实回执跨激活原字节保留检查，无旧结果仍未知。r1/r2原件保留，不能验r3新源码。最终仅以 E02–E05 的27项和7场景验本批。正式测试项目已进入 `.sln` 和 `scripts/Build.ps1`。独立 Sol 对最终旧会话分支再复审，没有必要修点。

新 Examples DLL：212992字节，SHA-256 `49fb1e95b5ed6a00d8b4d2194eb8fecee164ea6d58f720a7156d8a41bb0c03bc`。新包：[RizToolbox-0.3.0-sdk-probe-r3-preview-20261117.zip](../artifacts/delivery-20261117/RizToolbox-0.3.0-sdk-probe-r3-preview-20261117.zip)，37366478字节，SHA-256 `ac74e0e22fa1062aec7e079c95e6fe98c5bfc5739ef41eecea20ab25e01ebd14`。

原691项及4个宿主场景只按未变组件字节继承，不算本轮新执行，也不能验新示例或真实游戏。操作见[请求入口说明](Runtime-probe-control.md)。当前电脑操作接口不可用，已请求用户手动开局并保持前台；尚未发送实际控制请求。后续必须核对新会话 SDK Pause、Resume、Clock Ready、Speed/Seek 及 exact 内部 scheduled child。分钟级卡顿是否改善、B01–B19整体和生产准入仍未闭合，goal保持 active。
