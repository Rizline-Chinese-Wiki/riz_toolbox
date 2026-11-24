# 切出窗口时继续播放

插件 ID 为 `riz.background-play`。需要提供 `riz.window.background-play` 的 Windows 版 RizToolbox。安装时把 `riz.background-play` 文件夹放进游戏的 `BepInEx/RizToolbox/mods`，随后启动游戏。

游戏运行时，从工具仓库执行以下命令开启。把游戏路径换成本机的 Steam 安装目录。

```powershell
./scripts/Send-HostCommand.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline' -Action enable -PluginId 'riz.background-play'
```

关闭时把 `enable` 改为 `disable`。宿主通过原有的插件启用设置保存选择。已经停在暂停页时，先点一次「继续」。

这个选项用于把窗口切给其他应用后继续走谱。游戏仍按原规则记录判定和结算，漏掉的音符仍会计入 Miss。
