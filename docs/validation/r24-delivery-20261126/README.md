# RizToolbox r24 离线交付验收（2026-11-26）

r24 交付基于提交 `c7da86c28099371acb0674cefafd760ae00e08c5`，随包 SDK 为 1.5.0。插件可以用 `RequestPlayWithResult` 获取自己发起的 LOCAL 单局结果句柄，并在主线程读取结算。以下验收均在隔离环境完成。 [LOCAL 单局结算](../../Audio-content.md#读取自己发起的-local-单局结算)

## 最终包

| 文件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| `RizToolbox-r24-Steam-Basic.zip` | 43,029,804 | `ec2ea7025a76c2414e1c61c94aa9935f850dbacdbe3230e4e7b776368cfdb1b8` |
| `RizToolbox-r24-Steam-Runtime.zip` | 42,947,032 | `4fb5a3299eeb3f70632c866eda321ac6bbbe72d297388a1c335c886c2e01251a` |
| `RizToolbox-r24-Plugin-SDK.zip` | 11,982,043 | `f7f17052efab5f9acdaeb841136d9a0203b6573038fe3b47214318cd7f83aae0` |
| `RizToolbox-0.3.0-V5-live-fixes-candidate-r24.zip` | 8,745,959 | `56fce5f3b0c499e23aa59faf810b59e941565d4f3d6ab62259fc8e22046bcb07` |

## 已执行的离线验收

- 候选包和 Standard 玩家包的最终验收均通过，交付阶段 **28/28**。用与候选包逐字节一致的托管程序集运行 11 套测试，共通过 **2,139/2,139** 项。候选包另通过 20 个离线播放场景的 360 项检查，以及 4 个隔离部署场景。
- 独立使用候选包的六个托管 DLL 运行 Loader 测试，**33/33** 通过，其中启动邮箱 **8/8**。测试覆盖保留已保存的启用意图、首次轮询前的新命令只执行一次、忽略旧会话命令，以及当前会话复用命令 ID。 [启动邮箱回归](../../../tests/RizToolbox.Loader.Tests/CommandMailboxChecks.cs)
- Basic 全新安装与卸载、Runtime → Basic → Runtime 切换均通过。两组卸载后合计剩余 0 个安装器管理文件。两个玩家包的 16 个核心文件均与候选包逐字节一致。Basic 随包测试通过 15/15，离线宿主测试通过 7/7。Runtime 和 Basic 分别发现 0 个和 3 个模组。
- 从 r23 Basic 升级至 r24 Basic，22 个交付文件中替换 9 个、沿用 13 个，新增 0 个。卸载后保留用户的 `settings.json`。
- 用与实测机器相同的 r22 Basic 在独立目录升级到 r24，22 个交付文件全部匹配，11 个原有用户文件的内容一致。随后依次启用官方谱、自制谱、官方谱三个测试场景，每次切换都复核了这 22 个交付文件。
- SDK 作者包的模板和参考包均声明 1.5.0，隔离环境仅用随包 NuGet 源完成插件还原和编译。

## 待 Steam 实机验收

实测机器当前运行 r22 Basic。升级到 r24 后，先核对游戏启动和模组加载，再用原版 Play 按钮进入《Pastel Lines》EASY，核对 `RunStarted.ChartId`。在同一局暂停、切至 0.75 倍速、跳至第 100 秒并恢复，观察媒体时间和后续判定。自制谱另开一局，记录单个音符的判定，通过句柄读取结算，正常返回选曲后比较前后两份官方成绩摘要。
