# 切出窗口继续播放（BackgroundPlay）

让 Rizline 在切到其他窗口或后台时**不自动暂停、继续走谱**。

- 插件 ID：`riz.background-play`
- 依赖能力：`riz.window.background-play`（需 Windows 版 RizToolbox）

## 安装与开关

1. 把 `riz.background-play` 文件夹放到游戏目录的 `BepInEx/RizToolbox/mods/` 下，然后启动游戏。
2. 游戏运行后，在仓库根目录执行以下命令开启（把路径换成你本机的 Rizline 安装目录）：

```powershell
./scripts/Send-HostCommand.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline' -Action enable -PluginId 'riz.background-play'
```

要关闭时，把上面的 `enable` 改成 `disable` 即可。宿主会记住你的开关状态。

## 注意

- 如果切回游戏时已经停在暂停界面，需要先手动点一次「继续」。
- 切到后台期间游戏仍按原规则判定和结算，漏掉的音符会正常计入 Miss。
