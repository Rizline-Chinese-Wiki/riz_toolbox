# RizToolbox r25 · Steam Windows x64

r25 基于提交 `23f6bc58af6a2f0888d63b2ba7a6224ee972fd61`，SDK 合同版本为 1.6.0，适配 Steam 构建 `25977441`。需要升级已有 Steam 专用候选安装时，请选择 Steam Basic 或 Steam Runtime 包。游戏目录里的 `.doorstop_version` 可区分启动组件：Steam 候选系列为 `4.5.0-riz-engine-transport-candidate.5`，通用 Actions 预览为 `4.5.0-riz-preinit.2`。

## Steam 包怎么选

| 文件 | 用途 | 字节数 | SHA-256 |
| --- | --- | ---: | --- |
| `RizToolbox-r25-Steam-Basic.zip` | 玩家版，含成绩、主题、快捷面板三个内置插件 | 43,042,494 | `57844c112710aaa1622324dea0c59550e48be43b2f84a6ec27973143fa1c0523` |
| `RizToolbox-r25-Steam-Runtime.zip` | 只安装宿主，自行选择插件 | 42,959,724 | `a29d3c4c77feeec713044002e2c37dc84e4d8fd0bda2e1432e15c03f51ce5c4f` |
| `RizToolbox-r25-Steam-Author-Kit.zip` | 作者工具，含离线编译依赖、插件与曲包模板、示例和说明 | 12,768,012 | `badfab209c4a9585c96235c0b3405e17edbcb051b7bddd2d922f62b0f6573fca` |

退出游戏，把玩家 ZIP 完整解压到游戏目录之外。在解压根目录打开 PowerShell 7.2 或更高版本，按随包的《安装说明.md》准备 Python 3.10+ 与 `pefile`。将下方路径替换成 Steam「浏览本地文件」打开的目录，先执行预检。

```powershell
$game = 'D:/SteamLibrary/steamapps/common/Rizline'
./scripts/Install-EngineTransportCandidate.ps1 -GameDirectory $game -CandidateDirectory ./candidate -ValidateOnly
```

预检返回 `passed: true` 后，执行安装，再从 Steam 启动游戏。

```powershell
./scripts/Install-EngineTransportCandidate.ps1 -GameDirectory $game -CandidateDirectory ./candidate
```

## 测试曲包

1. 在游戏关闭时，将 `TideSignal-LOCAL-Mods.zip` 内的 `demo.tide-signal` 和 `tools.local-catalog` 两个目录解压到游戏的 `BepInEx/RizToolbox/mods/`。[下载该次构建的曲包](https://github.com/Rizline-Chinese-Wiki/riz_toolbox/actions/runs/45813769257)
2. 启动后，在解压的 Steam 玩家包根目录打开 PowerShell 7.2 或更高版本。将下面的路径改为实际游戏目录，分别启用两个插件。首次启用可能先返回 `waiting`，宿主会保留已经提交的启用意愿。

   ```powershell
   $game = 'D:/SteamLibrary/steamapps/common/Rizline'
   ./scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId demo.tide-signal -WaitSeconds 30
   ./scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId tools.local-catalog -WaitSeconds 30
   ```
3. 进入选曲页，在 **LOCAL catalog** 窗口选中 **Tide Signal / Easy**，再点击 **Request play**。返回选曲页或主菜单后，曲库会显示这次请求对应的首局结算。另一次请求被拒绝时，之前的结果仍可读取。

## 已验证的结果显示

在隔离宿主中加载了 GitHub 编译的两个插件包和同次 Basic 的六个程序集。受控的一局结束并退役后，重建的曲库窗口显示了对应的曲名、难度、分数 735219，以及输入的四项判定。读取结束后释放该局观察句柄。旧版本在同一用例中仅提交开局请求，未订阅结算。UI 与输入回归为 98/98，包括同名曲目重新发布、请求被拒绝、反复更换选择和停用清理。[源码与用例](https://github.com/Rizline-Chinese-Wiki/riz_toolbox/pull/24)

## Steam 包的验收范围

用上一版 Steam Basic 在独立目录中建立安装，再用表中的 r25 Basic 升级。升级后按安装清单核对了 22 个文件，8 份自有文件与升级前逐字节一致。把 Actions 产出的测试曲包解压后，实际安装的 SDK 报告 1.6.0，Standard 宿主发现了三个内置插件、示例曲包和曲库窗口。用作者包分别生成插件和曲包，离线编译均为零错误、零警告。

已安装的六个程序集也用于加载发行曲包与曲库窗口。在受控的场景、开局和结算中，显式点击播放只发起一次请求，返回主菜单后能读到同一曲目修订的分数和判定。Steam 官方谱的暂停、变速、跳转、恢复与随后结算另做实机验收。

另用 r22 Basic 直接升级至 r25 Basic，22 个受管文件中更新 9 个、沿用 13 个，11 个用户文件逐字节一致。上述两次升级都在独立目录完成。

### 实机启动待修

Steam 实机从 r22 升级至 r24 时，22 个受管文件通过校验，58 个用户文件保持原内容。r24 在启用测试插件的启动会话中出现严重停顿。最初 21 次适配器更新中位耗时约 6.4 秒，最慢一次约 57.3 秒。随后同次会话的滚动中位耗时回落到 0.1 毫秒以内。该次数据定位到适配器更新阶段，尚无法分辨具体模块。r25 的上述离线验收没有覆盖这项启动耗时。
