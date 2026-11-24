# RizToolbox 候选包安装

适用于 Windows 64 位 Steam 版 Rizline，支持的游戏构建为 25977441。解压整个 ZIP 到游戏目录之外的文件夹，在该文件夹打开 PowerShell 7.2 或更高版本。

## 选择版本

标准玩家包有纯底层版和带基础模组的版本。解压后，可在 PowerShell 中查看这个包实际选择的模组：

```powershell
(Get-Content -Raw './candidate/evidence/base-package-manifest.json' | ConvertFrom-Json).basicPlugins
```

`basic.song-scores` 提供歌曲和最好成绩面板，`basic.theme` 调整主题颜色，`basic.quick-panel` 用 F8 开关面板并播放自备 WAV。纯底层版的选择为空。带模组的 ZIP 内附 `docs/Basic-plugins.md` 和所选模组的配置样例。

## 首次安装或升级

1. 退出 Rizline。在 Steam 库中右键点击 Rizline，选择「管理 → 浏览本地文件」，复制打开的目录路径。
2. 安装器使用 Python 3.10 或更高版本和 `pefile` 核对游戏文件。下面的命令检查当前解释器。如果提示缺少 `pefile`，运行 `python -m pip install pefile==2024.8.26`。

   ```powershell
   python -c "import sys, pefile; print(sys.version, pefile.__version__)"
   ```

3. 把下方游戏路径换成刚复制的路径。先运行检查。看到 `"passed":true` 后，执行第二条安装命令。

   ```powershell
   $game = 'D:\SteamLibrary\steamapps\common\Rizline'
   ./scripts/Install-EngineTransportCandidate.ps1 -GameDirectory $game -CandidateDirectory ./candidate -ValidateOnly
   ./scripts/Install-EngineTransportCandidate.ps1 -GameDirectory $game -CandidateDirectory ./candidate
   ```

   如果使用专用 Python 环境，在两条命令末尾都加上 `-Python '解释器的完整路径'`。压缩包已包含安装所需的 BepInEx 运行库。

4. 从 Steam 正常启动游戏。进入主菜单后，在同一窗口运行以下命令，查看宿主已完成的更新次数和发现的插件。

   ```powershell
   $status = Get-Content -Raw -LiteralPath "$game/BepInEx/RizToolbox/status.json" | ConvertFrom-Json
   $status.lastCompletedHostTickSequence
   $status.plugins | Select-Object Id, State, DesiredEnabled
   ```

已有安装会保留启用意愿。升级后若快捷面板处于停用状态，可以在游戏运行时执行以下命令启用它。

```powershell
./scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId basic.quick-panel
```

## 卸载

退出游戏，在解压目录运行以下命令。卸载器根据安装记录移除内容仍匹配的文件，保留用户改动、生成的数据和重装所需的归属标记。

```powershell
./scripts/Uninstall.ps1 -GameDirectory $game
```
