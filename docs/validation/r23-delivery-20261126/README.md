# RizToolbox r23 交付验收（2026-11-26）

r23 提供 SDK 1.4.0 的所选官方谱面读取接口，并减少播放组件准备阶段的重复校验。本次交付验收在隔离环境完成。[官方谱面读取](../sdk-1.4-official-chart-20261126/README.md) · [播放准备回归](../../../tests/RizToolbox.PlaybackSourceGroup.Tests/SourcePreparationChecks.cs)

## 最终包

| 文件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| `RizToolbox-r23-Steam-Basic.zip` | 42,996,672 | `bc35fcc1b8bb129a4a575cfaa509968cbbaab493b2e5c51fa794a576c528e34c` |
| `RizToolbox-r23-Steam-Runtime.zip` | 42,913,891 | `24f7ebed7a753e099e79f2f00d0d7f361e1a598bd17370c3e49838eca5148445` |
| `RizToolbox-r23-Plugin-SDK.zip` | 11,981,228 | `5aaee094a07b2cb2024debc2145b6fd74aca6b1b188c2dc2f70c052e92bd20b6` |
| `RizToolbox-0.3.0-V5-live-fixes-candidate-r23.zip` | 8,723,616 | `2d27e1bc91d84d5b23c5aa6c057de49f305c1f3db791b26e168af870b06a2159` |

## 已执行的离线验收

- 28 个交付阶段全部通过。用封包中的托管程序集重跑 11 套测试，通过 2130/2130 项。封存的候选包另通过 20 个播放场景、共 360 项检查，以及 4 个隔离部署场景。
- Basic 全新安装与卸载、Runtime → Basic → Runtime 切换均通过。两组卸载后合计剩余 0 个安装器管理文件。两个玩家包的 16 个核心文件均与候选包逐字节一致。Basic 随包测试 15/15，离线宿主测试 7/7，Runtime 和 Basic 分别发现 0 个和 3 个模组。
- 从已验收的 r22 Basic 升级至 r23 Basic，22 个交付文件中替换 9 个、沿用 13 个，新增 0 个。卸载后保留用户设置。SDK 作者包通过仅用随包源的离线插件编译，候选宿主、模板声明和参考包的 SDK 版本均为 1.4.0。

三类探针使用最终作者包内的 SDK 参考程序集编译，并在最终 Basic 包对应的隔离宿主上复核，共通过 **25/25** 项。

| 探针 | 离线结果 |
| --- | ---: |
| 官方谱播放控制 | 15/15 |
| 一次性读取所选官方谱面及宿主命令 | 5/5 |
| 单音符自制谱包 | 5/5 |

## 待 Steam 实机验收

最近一次只读核对确认 Steam 安装目录与 r22 Basic 的 22 个交付文件相符。r23 待核对实际升级与启动、正常选曲及切换难度后的谱面读数、官方谱同局暂停、变速、跳转、恢复及恢复后的推进与判定，以及自制谱的开局、结算与返回。
