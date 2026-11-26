# r22 启动检查与自制谱重试

r21 启动时曾短暂无响应。两次游戏主线程采样分别落在 GetTime 安装前的校验和安装后的播放组件核对。r22 合并了这两处相邻的重复检查。下表记录同一离线用例在改动前后的检查次数。

| 操作 | r21 | r22 |
| --- | ---: | ---: |
| 首次安装 GetTime，整组校验次数 | 3 | 2 |
| 首次安装 GetTime，完整映像校验次数 | 21 | 15 |
| 关联播放组件并核对来源协议，整组校验次数 | 5 | 2 |
| 关联播放组件并核对来源协议，完整映像校验次数 | 431 | 163 |

首次安装仍会在写入前后校验。测试中修改 GetTime 或同组另一方法的元数据，物理提交次数都为 0。关联播放组件时仍核对 20 个游戏方法和 5 个只读方法。篡改已挂接的方法会触发拒绝，事后还原字节也无法重新启用这一实例。以上计数来自带受控输入的离线测试，Steam 实际启动耗时待测。

自制谱方面，若玩家的选曲状态在预制体准备期间发生变化，SDK 会释放这次尚未使用的预制体，返回可重试的 `NotReady`。如果释放失败，宿主仍保留错误。两个定向回归和 AudioContent 全套 1081/1081 项检查通过。

三项修复合并后，使用 1885 个冻结输入构建 Release，结果为 0 个错误、86 条警告。11 套标准回归共通过 2123/2123 项，覆盖 r21 的全部具名通过用例。其中 Runtime 398 项、Gameplay 261 项、AudioContent 1081 项，另外八套共 383 项。

另外七种启动专项场景在相同的 Runtime 和 Adapter 程序集上全部通过，涵盖播放组件关联、篡改后拒绝、两种时钟读取与准备，以及三种 GetTime 安装情形。

作为实机对照，r21 Basic 在一次 Steam 启动中记录了以下时间。起点为游戏进程创建。它们用于对比相同事件，期间也包含游戏自身运行和停留在启动画面的时间。

| 首次观测到的事件 | 距进程创建 |
| --- | ---: |
| SDK 宿主报告 Standard 模式 | 34.836 秒 |
| 官方谱测试插件就绪 | 44.054 秒 |
| 播放组件报告正在等待开曲 | 353.561 秒 |
| SDK 记录场景从 GameStart 切换至 MainMenu | 582.235 秒 |

旧的 `normal` 离线用例仅映射 GameAssembly，进入播放组件核对前就缺少 UnityPlayer，改动前后均在同处中止。本次使用同时映射两者的用例验证关联与篡改拒绝，分别通过 17 和 7 项检查。

对应测试见 [GetTime 安装](../../../tests/RizToolbox.PlaybackSourceGroup.Tests/GetTimeInstallationChecks.cs)、[播放组件关联](../../../tests/RizToolbox.Gameplay.Tests/NativePlaybackStockGroupProfileChecks.cs)和[自制谱重试](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalPlayLaunchSourceRegression.cs)。
