# r21 播放控制与自制谱验收

本次更新涉及 Steam 原版谱面的跳转、暂停后恢复，以及自制谱与游戏共用动画队列时的跟踪。SDK 版本仍为 1.3.0。

| 改动 | 验证结果 |
| --- | --- |
| 跳转准备复用本次操作已核对的播放时钟 | 专项用例中，实际跳转入口执行 2 次 GetTime 检查，整组和整映像重复扫描均为 0 次。GetTime 被外部改写后拒绝继续。 |
| 恢复播放时跟踪游戏的倒数 | 已覆盖正常倒数、错误调用来源、暂停状态下的进度回调，以及倒数中途退出当前对局。 |
| 自制谱跟踪共享动画队列 | 验证了共享 Tween 的加入、更新及移除，并覆盖其他动画与本局动画交错操作同一个队列。 |

Release 解决方案构建完成，0 个错误，86 条警告。最终候选通过 11 套回归，共 2121/2121 项。使用打包组件运行的播放专项覆盖 18 种场景，336 项检查通过。Basic 和 Runtime 安装包均通过隔离安装验收，插件作者 SDK 通过宿主加载验证。

Steam 实机已从 r20 升级，安装清单中的 22 个文件全部吻合。游戏和官方谱测试插件已启动。启动时窗口曾短暂无响应，两次主线程采样分别落在 GetTime 安装和播放代码核对。当前仍待同局验证《Pastel Lines》EASY 暂停后调至 0.75 倍速、跳转至 100 秒、恢复后曲内时间与判定继续推进，以及自制单音符谱的结算和返回。

相关回归见[播放时钟](../../../tests/RizToolbox.PlaybackStockClock.Tests/Program.cs)、[播放组件核对](../../../tests/RizToolbox.Gameplay.Tests/NativePlaybackStockGroupProfileChecks.cs)和[共享动画队列](../../../tests/RizToolbox.AudioContent.Tests/NativeTweenSourceReplayTests.cs)。
